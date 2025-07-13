pipeline {
    agent any

    environment {
        NUGET_API_KEY = credentials('nuget-key') // Jenkins credential ID
    }

    stages {
        stage('Generate Version') {
            steps {
                // Generate GitVersion output as JSON
                bat 'gitversion /output json > gitversion.json'

                script {
                    // Parse the version from JSON
                    def gitVersionJson = readFile('gitversion.json')
                    def gitVersion = new groovy.json.JsonSlurper().parseText(gitVersionJson)
                    env.NUGET_VERSION = gitVersion.NuGetVersionV2  // Using NuGet-compatible version format
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
                bat "dotnet build --configuration Release /p:Version=${env.NUGET_VERSION} /warnaserror"
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
                        // Secure way to find files without Pipeline Utility Steps plugin
                        def nupkgFiles = bat(script: '@dir /b nupkgs\\*.nupkg', returnStdout: true).trim().split('\r\n')
                        
                        if (nupkgFiles.size() == 0 || nupkgFiles[0].isEmpty()) {
                            error "❌ No .nupkg files found in 'nupkgs' folder."
                        }

                        for (file in nupkgFiles) {
                            def fullPath = "nupkgs\\${file}"
                            echo "📦 Uploading ${file}..."
                            
                            // Secure credential handling
                            withCredentials([string(credentialsId: 'nuget-key', variable: 'SECURE_NUGET_API_KEY']) {
                                bat """
                                    dotnet nuget push "${fullPath}" ^
                                    --api-key "%SECURE_NUGET_API_KEY%" ^
                                    --source https://api.nuget.org/v3/index.json ^
                                    --skip-duplicate
                                """
                            }
                            echo "✅ Successfully uploaded: ${file}"
                        }

                        echo "🎉 All NuGet package(s) uploaded successfully!"
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
            // Clean up workspace
            cleanWs()
        }
    }
}
