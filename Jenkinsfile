pipeline {
    agent any

    environment {
        NUGET_API_KEY = credentials('nuget-key')
    }

    stages {
        stage('Generate Version') {
            steps {
                script {
                    // First try to get version from GitVersion
                    def version = "1.0.0" // Default fallback version
                    try {
                        bat 'gitversion /output json > gitversion.json'
                        def gitVersionJson = readFile('gitversion.json')
                        def gitVersion = new groovy.json.JsonSlurper().parseText(gitVersionJson)
                        version = gitVersion.NuGetVersionV2 ?: version
                    } catch (Exception e) {
                        echo "⚠️ Could not determine version from GitVersion, using fallback: ${version}"
                    }
                    env.NUGET_VERSION = version
                    echo "📌 Using NuGet Version: ${env.NUGET_VERSION}"
                }
            }
        }

        stage('Restore') {
            steps {
                bat 'dotnet restore'
            }
        }

        stage('Build') {
            steps {
                script {
                    // First build without warnaserror to identify null reference issues
                    bat "dotnet build --configuration Release /p:Version=${env.NUGET_VERSION}"
                    
                    // Then build with warnaserror if first build succeeds
                    bat "dotnet build --configuration Release /p:Version=${env.NUGET_VERSION} /warnaserror"
                }
            }
        }

        stage('Pack') {
            steps {
                bat """
                    dotnet pack --configuration Release ^
                    --output ./nupkgs ^
                    /p:PackageVersion=${env.NUGET_VERSION} ^
                    /p:NoBuild=true ^
                    /p:IncludeSymbols=true ^
                    /p:SymbolPackageFormat=snupkg
                """
            }
        }

        stage('Publish to NuGet') {
            steps {
                catchError(buildResult: 'UNSTABLE', stageResult: 'FAILURE') {
                    script {
                        def nupkgFiles = bat(script: '@dir /b nupkgs\\*.nupkg', returnStdout: true).trim().split('\r\n')
                        
                        if (nupkgFiles.size() == 0 || nupkgFiles[0].isEmpty()) {
                            error "❌ No .nupkg files found in 'nupkgs' folder."
                        }

                        withCredentials([string(credentialsId: 'nuget-key', variable: 'SECURE_NUGET_API_KEY')]) {
                            for (file in nupkgFiles) {
                                def fullPath = "nupkgs\\${file}"
                                echo "📦 Uploading ${file}..."
                                
                                bat """
                                    dotnet nuget push "${fullPath}" ^
                                    --api-key "%SECURE_NUGET_API_KEY%" ^
                                    --source https://api.nuget.org/v3/index.json ^
                                    --skip-duplicate
                                """
                                echo "✅ Successfully uploaded: ${file}"
                            }
                        }
                    }
                }
            }
        }
    }

    post {
        failure {
            echo "❌ Build failed. Check the logs for details."
            archiveArtifacts artifacts: '**/bin/**/*.dll,**/bin/**/*.pdb', allowEmptyArchive: true
        }
        unstable {
            echo "⚠️ Build marked as UNSTABLE. Some NuGet uploads may have failed."
            archiveArtifacts artifacts: 'nupkgs/*.nupkg', allowEmptyArchive: true
        }
        success {
            echo "✅ Jenkins pipeline completed successfully!"
            archiveArtifacts artifacts: 'nupkgs/*.nupkg,nupkgs/*.snupkg', allowEmptyArchive: true
        }
        always {
            cleanWs()
        }
    }
}
