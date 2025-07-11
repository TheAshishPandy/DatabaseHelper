pipeline {
    agent any

    environment {
        NUGET_API_KEY = credentials('nuget-key')  // Use Jenkins credential
    }

    stages {
        stage('Versioning') {
            steps {
                bat 'gitversion /output json /showvariable FullSemVer > version.txt'
                script {
                    env.NUGET_VERSION = readFile('version.txt').trim()
                    echo "Using version: ${env.NUGET_VERSION}"
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
                bat "dotnet pack --configuration Release --output ./nupkgs /p:Version=${env.NUGET_VERSION}"
            }
        }

        stage('Publish to NuGet') {
            steps {
                bat """
                dotnet nuget push ./nupkgs/*.nupkg ^
                --api-key %NUGET_API_KEY% ^
                --source https://api.nuget.org/v3/index.json ^
                --skip-duplicate
                """
            }
        }
    }
}
