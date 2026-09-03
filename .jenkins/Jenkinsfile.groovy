properties([
    parameters([
        choice(
            name: 'DOCKER_NAMESPACE',
            choices: ['faulo', 'tmp'],
            description: 'Docker image namespace to test'
        )
    ]),
    disableConcurrentBuilds(),
    disableResume()
])

def dockerNamespace = params.DOCKER_NAMESPACE ?: 'faulo'
def image = "${dockerNamespace}/unreal-ddc:latest"
def hosts = [
    [name: 'Dende', os: 'windows'],
    [name: 'Garl', os: 'linux']
]

stage('Integration Tests') {
    for (def host in hosts) {
        stage("${host.name} (${host.os})") {
            catchError(
                message: "Unreal DDC integration test failed on ${host.name}",
                stageResult: 'FAILURE',
                buildResult: 'FAILURE',
                catchInterruptions: false
            ) {
                node(host.name) {
                    timeout(time: 20, unit: 'MINUTES') {
                        deleteDir()
                        checkout scm
                        withCredentials([
                            usernamePassword(
                                credentialsId: 'Faulo-GitHub',
                                usernameVariable: 'UNREAL_CREDENTIALS_USR',
                                passwordVariable: 'UNREAL_CREDENTIALS_PSW'
                            )
                        ]) {
                            def pullArgument = dockerNamespace == 'faulo' ? ' -Pull' : ''
                            exec "pwsh -NoLogo -NoProfile -NonInteractive -File common/test-images.ps1 -DockerContext default -ExpectedOs ${host.os} -Image ${image}${pullArgument}"
                        }
                    }
                }
            }
        }
    }
}
