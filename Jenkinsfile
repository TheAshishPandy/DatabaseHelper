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
                    env.NUGET_VERSION = gitVersion.FullSemVer
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
                bat "dotnet build --configuration Release /p:Version=${env.NUGET_VERSION}"
            }
        }

        stage('Pack') {
            steps {
                bat "dotnet pack --configuration Release --output ./nupkgs /p:PackageVersion=${env.NUGET_VERSION}"
            }
        }

        stage('Publish to NuGet') {
            steps {
                catchError(buildResult: 'UNSTABLE', stageResult: 'FAILURE') {
                    script {
                        def nupkgFiles = findFiles(glob: 'nupkgs/*.nupkg')
                        if (nupkgFiles.size() == 0) {
                            error "❌ No .nupkg files found in 'nupkgs' folder."
                        }

                        for (file in nupkgFiles) {
                            echo "📦 Uploading ${file.name}..."
                            bat """
                                dotnet nuget push "${file.path}" ^
                                    --api-key ${env.NUGET_API_KEY} ^
                                    --source https://api.nuget.org/v3/index.json ^
                                    --skip-duplicate
                            """
                            echo "✅ Successfully uploaded: ${file.name}"
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
        }
        unstable {
            echo "⚠️ Build marked as UNSTABLE. Some NuGet uploads may have failed."
        }
        success {
            echo "✅ Jenkins pipeline completed successfully!"
        }
    }
}
