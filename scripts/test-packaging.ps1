[CmdletBinding()]
param()
. (Join-Path $PSScriptRoot 'common.ps1')
$root = Get-RepositoryRoot
$failures = [Collections.Generic.List[string]]::new()
function Assert-Check([bool]$Condition, [string]$Message) {
    if (-not $Condition) { $failures.Add($Message) }
}

foreach ($file in (Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.ps1' -File)) {
    $tokens = $null; $parseErrors = $null
    [void][Management.Automation.Language.Parser]::ParseFile($file.FullName, [ref]$tokens, [ref]$parseErrors)
    foreach ($error in $parseErrors) { $failures.Add("$($file.Name): $($error.Message)") }
}
$parent = Join-Path $root 'artifacts'
$child = Join-Path $parent 'sample/file.txt'
Assert-Check ((Assert-ChildPath $child $parent) -eq [IO.Path]::GetFullPath($child)) 'A normal child path must be allowed.'
foreach ($outside in @($parent, (Join-Path $root 'artifacts-elsewhere/file.txt'), (Join-Path $parent '../README.md'))) {
    $rejected = $false
    try { [void](Assert-ChildPath $outside $parent) } catch { $rejected = $true }
    Assert-Check $rejected 'Path checks must reject the output root, sibling prefix and traversal.'
}
$allowlist = Get-Content -LiteralPath (Join-Path $root 'installer/source-allowlist.json') -Raw | ConvertFrom-Json
Assert-Check (@($allowlist.files | Select-Object -Unique).Count -eq $allowlist.files.Count) 'Source allowlist must not contain duplicate files.'
foreach ($name in $allowlist.files) {
    Assert-Check (Test-Path -LiteralPath (Join-Path $root $name) -PathType Leaf) "Source allowlist target is missing: $name"
    Assert-Check ($name -notmatch '(?i)(^|/)(\.tools|artifacts|dist|userdata|logs)(/|$)|implementation-plan|product-release-spec|development-contracts|packaging-plan') "Private source target: $name"
}
$installer = Get-Content -LiteralPath (Join-Path $root 'installer/Winicode.iss') -Raw
Assert-Check ($installer.Contains('AppId={{5B1D09D9-5EAC-4D73-BC78-4F6F9E32A971}')) 'The stable installer AppId changed.'
foreach ($required in @('PrivilegesRequired=lowest', 'ArchitecturesAllowed=x64os', 'UsePreviousTasks=yes', 'CloseApplications=no', 'RestartApplications=no',
        "'--exit --wait'", "'--remove-startup'", "'--cleanup-user-data'", 'if not UninstallSilent then', 'MB_YESNO or MB_DEFBUTTON2',
        'Check: IsNewInstall', '(not ExistingInstall)', 'CompareReleaseVersions')) {
    Assert-Check ($installer.Contains($required)) "Missing installer maintenance invariant: $required"
}
Assert-Check ($installer -notmatch '(?im)^\s*\[(Registry|UninstallDelete|InstallDelete)\]') 'Installer must delegate startup and bounded data cleanup to the application.'
Assert-Check ($installer -notmatch '(?i)taskkill|Stop-Process|StartupApproved|--data-dir') 'Installer contains a forbidden maintenance mechanism.'
$release = Get-Content -LiteralPath (Join-Path $root '.github/workflows/draft-release.yml') -Raw
Assert-Check ($release -match '(?m)^  workflow_dispatch:$' -and $release -notmatch '(?m)^  (push|release|schedule|pull_request):') 'Draft releases must be manually dispatched.'
Assert-Check ($release.Contains('--draft --prerelease --verify-tag') -and $release.Contains('-RequiredInnoVersion 7.1.0')) 'Draft workflow must pin compiler and retain draft status.'
Assert-Check ($release -notmatch '(?i)gh release (edit|publish)|draft:\s*false') 'Workflow must never publish a release automatically.'
foreach ($workflow in (Get-ChildItem -LiteralPath (Join-Path $root '.github/workflows') -Filter '*.yml' -File)) {
    foreach ($line in (Get-Content -LiteralPath $workflow.FullName)) {
        if ($line -match '^\s*- uses:\s+(.+)$') { Assert-Check ($Matches[1] -match '@[a-f0-9]{40}(?:\s|$)') 'Workflow Actions must be pinned to a commit.' }
    }
}
& (Join-Path $PSScriptRoot 'verify-unicode.ps1')
if ($failures.Count) { throw ($failures -join "`n") }
Write-Host 'Static packaging checks passed: PowerShell parsing, path boundaries, source list, installer safety and workflow gates. No build or installer was run.'
