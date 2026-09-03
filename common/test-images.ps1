[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string] $DockerContext,

    [Parameter(Mandatory)]
    [ValidateSet('linux', 'windows')]
    [string] $ExpectedOs,

    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string] $Image,

    [switch] $Pull
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

function Invoke-Docker {
    param(
        [Parameter(Mandatory)]
        [string[]] $Arguments
    )

    & docker --context $DockerContext @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Docker command failed with exit code ${LASTEXITCODE}: docker --context $DockerContext $($Arguments -join ' ')"
    }
}

function Invoke-DockerOutput {
    param(
        [Parameter(Mandatory)]
        [string[]] $Arguments
    )

    $output = & docker --context $DockerContext @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Docker command failed with exit code ${LASTEXITCODE}: docker --context $DockerContext $($Arguments -join ' ')`n$($output | Out-String)"
    }
    return ($output | Out-String).Trim()
}

function Wait-ContainerHealthy {
    param(
        [Parameter(Mandatory)]
        [string] $Container
    )

    for ($attempt = 1; $attempt -le 150; $attempt++) {
        $state = Invoke-DockerOutput @('inspect', '--format', '{{.State.Status}}|{{if .State.Health}}{{.State.Health.Status}}{{else}}missing{{end}}', $Container)
        if ($state -eq 'running|healthy') {
            return
        }
        if (-not $state.StartsWith('running|')) {
            Invoke-Docker @('logs', $Container)
            throw "Container $Container stopped before becoming healthy: $state"
        }
        Start-Sleep -Seconds 2
    }

    Invoke-Docker @('logs', $Container)
    throw "Container $Container did not become healthy within 300 seconds"
}

function Remove-TestContainer {
    param(
        [Parameter(Mandatory)]
        [string] $Container
    )

    $exists = & docker --context $DockerContext container inspect $Container *> $null
    if ($LASTEXITCODE -eq 0) {
        & docker --context $DockerContext container rm --force $Container *> $null
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to remove test container $Container"
        }
    }
}

$daemonOs = Invoke-DockerOutput @('version', '--format', '{{.Server.Os}}')
if ($daemonOs -ne $ExpectedOs) {
    throw "Docker context '$DockerContext' targets '$daemonOs', expected '$ExpectedOs'"
}

if ($Pull) {
    Invoke-Docker @('pull', $Image)
}

$id = [Guid]::NewGuid().ToString('N').Substring(0, 12)
$container = "unreal-ddc-test-$id"
$installVolume = "unreal-ddc-test-$id-install"
$dataVolume = "unreal-ddc-test-$id-data"
$releaseVersion = 'v5.8.20'

if ($ExpectedOs -eq 'windows') {
    $installPath = 'C:/unreal-ddc/install'
    $dataPath = 'C:/unreal-ddc/data'
    $zen = "C:/unreal-ddc/install/$releaseVersion/windows/zen.exe"
} else {
    $installPath = '/unreal-ddc/install'
    $dataPath = '/unreal-ddc/data'
    $zen = "/unreal-ddc/install/$releaseVersion/linux/zen"
}

try {
    Invoke-Docker @('volume', 'create', $installVolume)
    Invoke-Docker @('volume', 'create', $dataVolume)

    Invoke-Docker @(
        'run', '--detach',
        '--name', $container,
        '--env', 'UNREAL_CREDENTIALS_USR',
        '--env', 'UNREAL_CREDENTIALS_PSW',
        '--volume', "${installVolume}:${installPath}",
        '--volume', "${dataVolume}:${dataPath}",
        $Image
    )
    Wait-ContainerHealthy $container

    $version = Invoke-DockerOutput @('exec', $container, $zen, 'version', 'http://127.0.0.1:8558')
    if ($version -ne '5.8.20') {
        throw "Unexpected Zen version: expected '5.8.20', got '$version'"
    }

    Invoke-Docker @(
        'exec', $container, $zen,
        'bench', 'http',
        '--url', 'http://127.0.0.1:8558/health/ready',
        '--count', '20',
        '--concurrency', '4'
    )
    Invoke-Docker @(
        'exec', $container, $zen,
        'bench', 'cacheload',
        '--hosturl', 'http://127.0.0.1:8558',
        '--namespace', 'integration.ddc',
        '--bucket', 'persistence',
        '--sizes', '4KiB:60,64KiB:30,1MiB:10',
        '--count', '64',
        '--seed', '20260903',
        '--concurrency', '8',
        '--seed-only'
    )

    Invoke-Docker @('stop', '--time', '30', $container)
    Remove-TestContainer $container

    # The second start deliberately receives no credentials. It proves both the
    # verified installation and the seeded cache survive container replacement.
    Invoke-Docker @(
        'run', '--detach',
        '--name', $container,
        '--volume', "${installVolume}:${installPath}",
        '--volume', "${dataVolume}:${dataPath}",
        $Image
    )
    Wait-ContainerHealthy $container
    Invoke-Docker @(
        'exec', $container, $zen,
        'bench', 'cacheload',
        '--hosturl', 'http://127.0.0.1:8558',
        '--namespace', 'integration.ddc',
        '--bucket', 'persistence',
        '--sizes', '4KiB:60,64KiB:30,1MiB:10',
        '--count', '64',
        '--seed', '20260903',
        '--concurrency', '8',
        '--requests', '512',
        '--skip-seed'
    )
} finally {
    Remove-TestContainer $container
    foreach ($volume in @($installVolume, $dataVolume)) {
        $exists = & docker --context $DockerContext volume inspect $volume *> $null
        if ($LASTEXITCODE -eq 0) {
            & docker --context $DockerContext volume rm $volume *> $null
            if ($LASTEXITCODE -ne 0) {
                Write-Warning "Failed to remove test volume $volume"
            }
        }
    }
}
