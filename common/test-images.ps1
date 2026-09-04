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

function Get-ZenReleases {
    $headers = @{
        Accept = 'application/vnd.github+json'
        Authorization = "Bearer $env:UNREAL_CREDENTIALS_PSW"
        'X-GitHub-Api-Version' = '2022-11-28'
        'User-Agent' = 'docker-unreal-ddc-contract'
    }
    $releases = Invoke-RestMethod -Headers $headers -Uri 'https://api.github.com/repos/EpicGames/zen/releases?per_page=100'
    return @(
        $releases |
            Where-Object { -not $_.draft -and -not $_.prerelease -and $_.tag_name -match '^v(?<version>\d+\.\d+\.\d+)$' } |
            ForEach-Object {
                [pscustomobject]@{
                    Tag = $_.tag_name
                    Version = [Version] $Matches.version
                }
            } |
            Sort-Object Version -Descending
    )
}

function Assert-CleanStop {
    param(
        [Parameter(Mandatory)]
        [string] $Container
    )

    Invoke-Docker @('stop', '--timeout', '30', $Container)
    $state = Invoke-DockerOutput @('inspect', '--format', '{{json .State}}', $Container) | ConvertFrom-Json
    if ($state.Status -ne 'exited' -or $state.ExitCode -ne 0 -or $state.OOMKilled) {
        throw "Container $Container did not stop cleanly: $($state | ConvertTo-Json -Compress)"
    }
}

function Initialize-CredentialVolume {
    param(
        [Parameter(Mandatory)]
        [string] $Volume,

        [Parameter(Mandatory)]
        [string] $Path
    )

    if ($ExpectedOs -eq 'windows') {
        $script = @'
$ErrorActionPreference = 'Stop'
Set-Content -LiteralPath C:/credentials/username -Value $env:UNREAL_CREDENTIALS_USR -NoNewline
Set-Content -LiteralPath C:/credentials/token -Value $env:UNREAL_CREDENTIALS_PSW -NoNewline
'@
        Invoke-Docker @(
            'run', '--rm',
            '--env', 'UNREAL_CREDENTIALS_USR',
            '--env', 'UNREAL_CREDENTIALS_PSW',
            '--volume', "${Volume}:${Path}",
            '--entrypoint', 'powershell',
            $Image,
            '-NoLogo', '-NoProfile', '-NonInteractive', '-Command', $script
        )
        return
    }

    $script = 'printf %s "$UNREAL_CREDENTIALS_USR" > /credentials/username && printf %s "$UNREAL_CREDENTIALS_PSW" > /credentials/token'
    Invoke-Docker @(
        'run', '--rm',
        '--user', '0',
        '--env', 'UNREAL_CREDENTIALS_USR',
        '--env', 'UNREAL_CREDENTIALS_PSW',
        '--volume', "${Volume}:${Path}",
        '--entrypoint', '/bin/sh',
        $Image,
        '-c', $script
    )
}

function Get-ZenCommandLine {
    param(
        [Parameter(Mandatory)]
        [string] $Container
    )

    if ($ExpectedOs -eq 'windows') {
        $script = '$process = Get-CimInstance Win32_Process | Where-Object Name -EQ "zenserver.exe" | Select-Object -First 1; if ($null -eq $process) { exit 1 }; $process.CommandLine'
        return Invoke-DockerOutput @('exec', $Container, 'powershell', '-NoLogo', '-NoProfile', '-NonInteractive', '-Command', $script)
    }

    $script = 'for file in /proc/[0-9]*/comm; do if [ "$(cat "$file")" = "zenserver" ]; then directory="${file%/comm}"; tr "\000" "\n" < "$directory/cmdline"; exit 0; fi; done; exit 1'
    return Invoke-DockerOutput @('exec', $Container, 'sh', '-c', $script)
}

function Assert-StartedVersion {
    param(
        [Parameter(Mandatory)]
        [string] $Container,

        [Parameter(Mandatory)]
        [Version] $ExpectedVersion
    )

    $logs = Invoke-DockerOutput @('logs', $Container)
    if ($logs -notmatch "docker-unreal-ddc: starting Epic Zen $([Regex]::Escape($ExpectedVersion.ToString()))(?:\s|$)") {
        throw "Container $Container did not start expected Zen $ExpectedVersion`n$logs"
    }
    $zenLines = @($logs -split "`r?`n" | Where-Object { $_ -and -not $_.StartsWith('docker-unreal-ddc:') })
    if ($zenLines.Count -eq 0) {
        throw "Container $Container did not mirror any Zen log output`n$logs"
    }
}

function Get-CacheEntryCount {
    param(
        [Parameter(Mandatory)]
        [string] $Container,

        [Parameter(Mandatory)]
        [string] $Zen
    )

    $output = Invoke-DockerOutput @(
        'exec', $Container, $Zen,
        'cache-info',
        '--hosturl', 'http://127.0.0.1:8558',
        '--namespace', 'integration.ddc',
        '--bucket', 'persistence'
    )
    $match = [Regex]::Match($output, '"DiskEntryCount"\s*:\s*(?<count>\d+)')
    if (-not $match.Success) {
        throw "Zen cache-info did not report DiskEntryCount:`n$output"
    }
    return [long] $match.Groups['count'].Value
}

$daemonOs = Invoke-DockerOutput @('version', '--format', '{{.Server.Os}}')
if ($daemonOs -ne $ExpectedOs) {
    throw "Docker context '$DockerContext' targets '$daemonOs', expected '$ExpectedOs'"
}

if ($Pull) {
    Invoke-Docker @('pull', $Image)
}

$configuration = Invoke-DockerOutput @('image', 'inspect', '--format', '{{json .Config}}', $Image) | ConvertFrom-Json
$cmdProperty = $configuration.PSObject.Properties['Cmd']
if ($null -ne $cmdProperty -and @($cmdProperty.Value).Count -ne 0) {
    throw "Image $Image must have an empty CMD"
}
if (@($configuration.Env | Where-Object { $_ -like 'ZEN_RELEASE_VERSION=*' }).Count -ne 0) {
    throw "Image $Image still exposes obsolete ZEN_RELEASE_VERSION"
}
if (@($configuration.Env | Where-Object { $_ -like 'ZEN_VERSION=*' }).Count -ne 1) {
    throw "Image $Image must expose exactly one default ZEN_VERSION"
}

$id = [Guid]::NewGuid().ToString('N').Substring(0, 12)
$container = "unreal-ddc-test-$id"
$installVolume = "unreal-ddc-test-$id-install"
$dataVolume = "unreal-ddc-test-$id-data"
$credentialVolume = "unreal-ddc-test-$id-credentials"
if ($ExpectedOs -eq 'windows') {
    $installPath = 'C:/unreal-ddc/install'
    $dataPath = 'C:/unreal-ddc/data'
    $credentialPath = 'C:/credentials'
    $launcher = 'C:/unreal-ddc/UnrealDDC.exe'
    $platformName = 'windows'
    $clientName = 'zen.exe'
} else {
    $installPath = '/unreal-ddc/install'
    $dataPath = '/unreal-ddc/data'
    $credentialPath = '/credentials'
    $launcher = '/unreal-ddc/UnrealDDC'
    $platformName = 'linux'
    $clientName = 'zen'
}

$expectedHealthcheck = @('CMD', $launcher, '--health')
if (Compare-Object @($configuration.Healthcheck.Test) $expectedHealthcheck -SyncWindow 0) {
    throw "Image $Image must use '$launcher --health' as its healthcheck"
}

$releases = Get-ZenReleases
$latest = $releases | Where-Object { $_.Version.Major -eq 5 } | Select-Object -First 1
if ($null -eq $latest) {
    throw 'The contract requires a stable Zen release in major version 5'
}
$previousMinor = $releases |
    Where-Object { $_.Version.Major -eq $latest.Version.Major -and $_.Version.Minor -lt $latest.Version.Minor } |
    Select-Object -First 1
if ($null -eq $previousMinor) {
    throw 'The contract requires at least two stable Zen minor lines in major version 5'
}
$firstVersion = ($releases | Where-Object { $_.Version.Major -eq $previousMinor.Version.Major -and $_.Version.Minor -eq $previousMinor.Version.Minor } | Select-Object -First 1).Version
$firstSelector = $firstVersion.ToString()
$secondSelector = $latest.Version.Major.ToString()
$firstZen = "$installPath/v$firstVersion/$platformName/$clientName"
$secondZen = "$installPath/v$($latest.Version)/$platformName/$clientName"

try {
    Invoke-Docker @('volume', 'create', $installVolume)
    Invoke-Docker @('volume', 'create', $dataVolume)
    Invoke-Docker @('volume', 'create', $credentialVolume)
    Initialize-CredentialVolume $credentialVolume $credentialPath

    # The initial installation uses a mixed direct/file-backed pair, matching
    # common Docker and Portainer secret deployments.
    Invoke-Docker @(
        'run', '--detach',
        '--name', $container,
        '--env', 'UNREAL_CREDENTIALS_USR',
        '--env', "UNREAL_CREDENTIALS_PSW_FILE=$credentialPath/token",
        '--env', "ZEN_VERSION=$firstSelector",
        '--env', 'ZEN_GC_DISKSIZE_SOFTLIMIT=100GB',
        '--env', 'ZEN_GC_LOW_DISKSPACE_THRESHOLD=1000MB',
        '--env', 'ZEN_GC_CACHE_DURATION=1Y60S',
        '--volume', "${installVolume}:${installPath}",
        '--volume', "${dataVolume}:${dataPath}",
        '--volume', "${credentialVolume}:${credentialPath}:ro",
        $Image
    )
    Wait-ContainerHealthy $container

    Invoke-Docker @('exec', $container, $launcher, '--health')
    Assert-StartedVersion $container $firstVersion
    $commandLine = Get-ZenCommandLine $container
    foreach ($expectedArgument in @(
        '--gc-disksize-softlimit=100000000000',
        '--gc-low-diskspace-threshold=1000000000',
        '--gc-cache-duration-seconds=31536060'
    )) {
        if (-not $commandLine.Contains($expectedArgument)) {
            throw "Zen command line does not contain '$expectedArgument': $commandLine"
        }
    }

    Invoke-Docker @(
        'exec', $container, $firstZen,
        'bench', 'http',
        '--url', 'http://127.0.0.1:8558/health/ready',
        '--count', '20',
        '--concurrency', '4'
    )
    Invoke-Docker @(
        'exec', $container, $firstZen,
        'cache-gen',
        '--hosturl', 'http://127.0.0.1:8558',
        '--namespace', 'integration.ddc',
        '--bucket', 'persistence',
        '--count', '64',
        '--min-size', '4096',
        '--max-size', '1048576',
        '--min-attachments', '0',
        '--max-attachments', '0'
    )
    $seededEntryCount = Get-CacheEntryCount $container $firstZen
    if ($seededEntryCount -lt 64) {
        throw "Zen cache contains only $seededEntryCount entries after seeding"
    }

    Assert-CleanStop $container
    Remove-TestContainer $container

    # The second start uses both file-backed forms, broadens the selector, and
    # must discover the newest compatible release while retaining the cache.
    Invoke-Docker @(
        'run', '--detach',
        '--name', $container,
        '--env', "UNREAL_CREDENTIALS_USR_FILE=$credentialPath/username",
        '--env', "UNREAL_CREDENTIALS_PSW_FILE=$credentialPath/token",
        '--env', "ZEN_VERSION=$secondSelector",
        '--volume', "${installVolume}:${installPath}",
        '--volume', "${dataVolume}:${dataPath}",
        '--volume', "${credentialVolume}:${credentialPath}:ro",
        $Image
    )
    Wait-ContainerHealthy $container
    Invoke-Docker @('exec', $container, $launcher, '--health')
    Assert-StartedVersion $container $latest.Version
    $upgradedEntryCount = Get-CacheEntryCount $container $secondZen
    if ($upgradedEntryCount -lt $seededEntryCount) {
        throw "Zen cache lost entries during upgrade: $seededEntryCount before, $upgradedEntryCount after"
    }
    Assert-CleanStop $container
    Remove-TestContainer $container

    # Once a matching verified installation is active, a restart must remain
    # available when the secret provider is temporarily unavailable.
    Invoke-Docker @(
        'run', '--detach',
        '--name', $container,
        '--env', "ZEN_VERSION=$secondSelector",
        '--volume', "${installVolume}:${installPath}",
        '--volume', "${dataVolume}:${dataPath}",
        $Image
    )
    Wait-ContainerHealthy $container
    Assert-StartedVersion $container $latest.Version
    Assert-CleanStop $container
    Remove-TestContainer $container
} finally {
    Remove-TestContainer $container
    foreach ($volume in @($installVolume, $dataVolume, $credentialVolume)) {
        $exists = & docker --context $DockerContext volume inspect $volume *> $null
        if ($LASTEXITCODE -eq 0) {
            & docker --context $DockerContext volume rm $volume *> $null
            if ($LASTEXITCODE -ne 0) {
                Write-Warning "Failed to remove test volume $volume"
            }
        }
    }
}
