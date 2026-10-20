[CmdletBinding()]
param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Release')
. (Join-Path $PSScriptRoot 'common.ps1')
$root = Get-RepositoryRoot
$dotnet = Resolve-BuildTool dotnet
$sdk = Assert-DotnetSdk $dotnet
Write-Host "Building with .NET SDK $sdk."
Push-Location $root
try {
    & (Join-Path $PSScriptRoot 'verify-unicode.ps1')
    Invoke-Checked $dotnet @('restore', 'tests/Winicode.Tests/Winicode.Tests.csproj')
    Invoke-Checked $dotnet @('build', 'tests/Winicode.Tests/Winicode.Tests.csproj', '-c', $Configuration, '--no-restore', '-p:ContinuousIntegrationBuild=true')
    # This repository has a dependency-free console test runner; dotnet test would skip it.
    Invoke-Checked $dotnet @('run', '--project', 'tests/Winicode.Tests/Winicode.Tests.csproj', '-c', $Configuration, '--no-build', '--no-restore')
} finally { Pop-Location }
