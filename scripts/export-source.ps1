[CmdletBinding()]
param([switch]$ListOnly)
. (Join-Path $PSScriptRoot 'common.ps1')
$root = Get-RepositoryRoot
$allowlist = Get-Content -LiteralPath (Join-Path $root 'installer/source-allowlist.json') -Raw | ConvertFrom-Json
$selected = @{}
foreach ($name in $allowlist.files) {
    $path = Assert-ChildPath (Join-Path $root $name) $root
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing required source file: $name" }
    $selected[$name] = $path
}
foreach ($tree in $allowlist.trees) {
    $treeRoot = Assert-ChildPath (Join-Path $root $tree.path) $root
    foreach ($file in (Get-ChildItem -LiteralPath $treeRoot -Recurse -File -Force)) {
        $name = Get-RelativeFileName $root $file.FullName
        if ($name -match '(?i)(^|/)(bin|obj|\.vs|\.tools|artifacts|userdata|logs)(/|$)') { continue }
        if ($file.Extension -in $tree.extensions) { $selected[$name] = $file.FullName }
    }
}
foreach ($name in @($selected.Keys)) {
    $path = $selected[$name]
    # Check every ancestor as well as the leaf to reject links escaping the checkout.
    $item = Get-Item -LiteralPath $path -Force
    while ($null -ne $item -and $item.FullName -ne $root) {
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Source links are not allowed: $name" }
        if ($item -is [IO.DirectoryInfo]) { $item = $item.Parent } else { $item = $item.Directory }
    }
    if ($name -match '(?i)(snapshot|screenshot|credentials|secrets|cookies|target-text)|\.(pfx|p12|pem|key|log|zip|pdb)$') { throw "Private or generated source path: $name" }
    if ([IO.Path]::GetExtension($name) -ne '.ico') {
        $text = Get-Content -LiteralPath $selected[$name] -Raw
        if ($text -match '(?i)[A-Z]:\\Users\\|[A-Z]:\\CodexProjects\\|BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY|gh[pousr]_[A-Za-z0-9]{30,}') {
            throw "Review local paths or credential-like text in $name before source export."
        }
    }
}
$names = @($selected.Keys | Sort-Object)
if ($ListOnly) { $names; return }
$directory = Join-Path $root ('artifacts/source-' + [guid]::NewGuid().ToString('N'))
[void](New-Item -ItemType Directory -Path $directory)
foreach ($name in $names) {
    $destination = Assert-ChildPath (Join-Path $directory $name) $directory
    [void](New-Item -ItemType Directory -Path (Split-Path $destination -Parent) -Force)
    Copy-Item -LiteralPath $selected[$name] -Destination $destination
}
Write-Host "Reviewed source export: $directory"
Write-Host 'Review this directory before publishing. This script does not initialize Git, stage files, commit or push.'
