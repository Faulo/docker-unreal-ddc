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

    $missingFile = if ($Os -eq 'windows') {
        'C:/unreal-ddc/missing-pat'
    } else {
        '/unreal-ddc/missing-pat'
    }
}

Describe "PAT credential contract [$Context, $Image]" {
    It 'recognizes an unreadable PAT file without exposing its path' {
        $result = Get-DockerCommandResult -Context $Context -Arguments @(
            'run', '--rm', '--env', "UNREAL_CREDENTIALS_PAT_FILE=$missingFile", $Image
        )
        $output = $result.Output | Out-String

        $result.ExitCode | Should -Not -Be 0
        $output | Should -Match 'UNREAL_CREDENTIALS_PAT_FILE'
        $output | Should -Not -Match [regex]::Escape($missingFile)
    }

    It 'rejects direct and file-backed PAT together' {
        $result = Get-DockerCommandResult -Context $Context -Arguments @(
            'run', '--rm',
            '--env', 'UNREAL_CREDENTIALS_PAT=unused-test-token',
            '--env', "UNREAL_CREDENTIALS_PAT_FILE=$missingFile",
            $Image
        )
        $output = $result.Output | Out-String

        $result.ExitCode | Should -Not -Be 0
        $output | Should -Match 'UNREAL_CREDENTIALS_PAT_FILE'
        $output | Should -Not -Match 'unused-test-token'
    }

    It 'rejects mixing PAT with legacy credentials before reading either' {
        $result = Get-DockerCommandResult -Context $Context -Arguments @(
            'run', '--rm',
            '--env', 'UNREAL_CREDENTIALS_PAT=unused-test-token',
            '--env', 'UNREAL_CREDENTIALS_USR=unused-test-user',
            $Image
        )
        $output = $result.Output | Out-String

        $result.ExitCode | Should -Not -Be 0
        $output | Should -Match 'UNREAL_CREDENTIALS_PAT'
        $output | Should -Match 'UNREAL_CREDENTIALS_USR'
        $output | Should -Not -Match 'unused-test-token'
    }
}
