param(
    [Parameter(Mandatory)]
    [string] $Namespace,
    
    [Parameter(Mandatory)]
    [string] $Name,

    [Parameter(Mandatory)]
    [string] $Variant,
    
    [Parameter(Mandatory)]
    [string] $Context,
    
    [Parameter(Mandatory)]
    [string] $Image,

    [Parameter(Mandatory)]
    [string] $Os,

    [Parameter(Mandatory)]
    [AllowEmptyCollection()]
    [string[]] $DockerRunArguments
)

BeforeAll {
    . (Join-Path $PSScriptRoot '../.jenkins/Docker.ps1')

    $launcher = $Os -eq 'windows' ? 'C:/unreal-ddc/UnrealDDC.exe' : '/unreal-ddc/UnrealDDC'

    function Remove-TestContainer {
        param(
            [Parameter(Mandatory)]
            [string] $Container
        )

        $result = Get-DockerCommandResult -Context $Context -Arguments @('container', 'inspect', $Container)
        if ($result.ExitCode -eq 0) {
            Invoke-Docker -Context $Context -Arguments @('container', 'rm', '--force', '--volumes', $Container)
        }
    }
}

Describe "Unreal DDC runtime [$Context, $Image]" {
    It 'uses the expected platform runtime' {
        if ($Os -eq 'linux') {
            Invoke-Docker -Context $Context -Arguments @(
                'run', '--rm', $Image,
                'grep', '--fixed-strings', 'VERSION_CODENAME=trixie', '/etc/os-release'
            ) -RunArguments $DockerRunArguments
        }
    }

    It 'prints launcher version' {
        Invoke-Docker -Context $Context -Arguments @(
            'run', '--rm', $Image,
            $launcher, '--launcher-version'
        ) -RunArguments $DockerRunArguments
    }
}

Describe "Unreal DDC service [$Context, $Image]" {
    BeforeAll {
        $container = "unreal-ddc-test-$([guid]::NewGuid().ToString('N'))"
        Invoke-Docker -Context $Context -Arguments @(
            'run', '--detach', '--name', $container, $Image
        ) -RunArguments $DockerRunArguments

        try {
            $deadline = [DateTime]::UtcNow.AddMinutes(5)
            do {
                $state = Invoke-DockerOutput -Context $Context -Arguments @(
                    'inspect', '--format',
                    '{{.State.Status}}|{{if .State.Health}}{{.State.Health.Status}}{{else}}missing{{end}}',
                    $container
                )
                if ($state -eq 'running|healthy') {
                    break
                }
                if (-not $state.StartsWith('running|')) {
                    throw "Container stopped before becoming healthy: $state"
                }
                Start-Sleep -Seconds 2
            } while ([DateTime]::UtcNow -lt $deadline)

            if ($state -ne 'running|healthy') {
                throw "Container did not become healthy within 5 minutes: $state"
            }
        } catch {
            $logs = Get-DockerCommandResult -Context $Context -Arguments @('logs', $container)
            Write-Host ($logs.Output | Out-String)
            throw
        }
    }

    AfterAll {
        if ($container) {
            Remove-TestContainer -Container $container
        }
    }

    It 'is online' {
        Invoke-DockerOutput -Context $Context -Arguments @(
            'inspect', '--format', '{{.State.Status}}|{{.State.Health.Status}}', $container
        ) | Should -Be 'running|healthy'

        Invoke-Docker -Context $Context -Arguments @('exec', $container, $launcher, '--health')
    }
}
