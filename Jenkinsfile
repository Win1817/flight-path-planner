pipeline {
    agent any

    stages {
        stage('Checkout') {
            steps {
                // Checkout the repository
                checkout scm
            }
        }

        stage('Build') {
            steps {
                script {
                    // Build the Docker image
                    sh 'docker build -t uas-tool:latest .'
                }
            }
        }

        stage('Deploy') {
            steps {
                // In a real-world scenario, you would push the Docker image to a registry
                // and then deploy it to a container orchestration platform like Kubernetes.
                echo 'Deploying the application...'
                // Example: sh 'docker push your-registry/uas-tool:latest'
                // Example: sh 'kubectl apply -f kubernetes-deployment.yaml'
            }
        }
    }

    post {
        always {
            // Clean up workspace
            cleanWs()
        }
    }
}
