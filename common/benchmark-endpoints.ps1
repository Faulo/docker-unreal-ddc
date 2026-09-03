[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string] $ZenClient,

    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string[]] $Endpoint,

    [ValidateRange(1, 20)]
    [int] $Trials = 3,

    [ValidateRange(1, 300)]
    [int] $DurationSeconds = 10
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$zen = (Get-Item -LiteralPath $ZenClient -ErrorAction Stop).FullName
$targets = foreach ($value in $Endpoint) {
    $separator = $value.IndexOf('=')
    if ($separator -le 0 -or $separator -eq ($value.Length - 1)) {
        throw "Endpoint '$value' must use the form Label=http://host:port"
    }

    $label = $value.Substring(0, $separator).Trim()
    $urlText = $value.Substring($separator + 1).Trim().TrimEnd('/')
    if ([string]::IsNullOrWhiteSpace($label)) {
        throw "Endpoint '$value' must start with a non-empty label"
    }
    $uri = $null
    $isValidUri = [Uri]::TryCreate($urlText, [UriKind]::Absolute, [ref] $uri)
    if (-not $isValidUri -or $uri.Scheme -notin @('http', 'https')) {
        throw "Endpoint '$value' must contain an absolute HTTP or HTTPS URL"
    }

    [PSCustomObject]@{
        Label = $label
        Url = $urlText
    }
}

function Invoke-Zen {
    param(
        [Parameter(Mandatory)]
        [string[]] $Arguments
    )

    & $zen @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Zen command failed with exit code ${LASTEXITCODE}: $zen $($Arguments -join ' ')"
    }
}

$namespace = 'docker.unreal.ddc.benchmark'
$bucket = 'production'
$sizes = '4KiB:50,64KiB:30,1MiB:15,8MiB:5'
$count = '512'
$seed = '20260903'
$concurrency = '16'

foreach ($target in $targets) {
    Write-Output "Checking $($target.Label) at $($target.Url)"
    Invoke-Zen -Arguments @('version', $target.Url)
    Invoke-Zen -Arguments @(
        'bench', 'cacheload',
        '--hosturl', $target.Url,
        '--namespace', $namespace,
        '--bucket', $bucket,
        '--sizes', $sizes,
        '--count', $count,
        '--seed', $seed,
        '--concurrency', $concurrency,
        '--seed-only'
    )
}

for ($trial = 1; $trial -le $Trials; $trial++) {
    $orderedTargets = @($targets)
    if (($trial % 2) -eq 0) {
        [Array]::Reverse($orderedTargets)
    }

    foreach ($target in $orderedTargets) {
        Write-Output "Trial $trial/$Trials - $($target.Label)"
        Invoke-Zen -Arguments @(
            'bench', 'cacheload',
            '--hosturl', $target.Url,
            '--namespace', $namespace,
            '--bucket', $bucket,
            '--sizes', $sizes,
            '--count', $count,
            '--seed', $seed,
            '--concurrency', $concurrency,
            '--duration', $DurationSeconds.ToString([Globalization.CultureInfo]::InvariantCulture),
            '--skip-seed'
        )
    }
}
