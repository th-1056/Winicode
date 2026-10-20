[CmdletBinding()]
param([string]$DataDirectory = (Join-Path $PSScriptRoot '../data'))
. (Join-Path $PSScriptRoot 'common.ps1')

$metadata = Get-Content -LiteralPath (Join-Path $DataDirectory 'sources.json') -Raw | ConvertFrom-Json
if ($metadata.version -ne '18.0.0' -or $metadata.status -ne 'final' -or $metadata.license -ne 'UNICODE LICENSE V3') {
    throw 'Expected the pinned Unicode 18.0.0 final data under Unicode License V3.'
}
$expected = @('UnicodeData.txt', 'DerivedName.txt', 'NameAliases.txt', 'ReadMe.txt', 'LICENSE.txt')
$paths = @($metadata.files | ForEach-Object { [string]$_.path })
if (@(Compare-Object ($expected | Sort-Object) ($paths | Sort-Object)).Count -gt 0) { throw 'Unexpected or duplicate official Unicode file list.' }
foreach ($entry in $metadata.files) {
    $uri = [uri]$entry.url
    if ($uri.Scheme -ne 'https' -or $uri.Host -cne 'www.unicode.org' -or $uri.Query -or $uri.Fragment -or
        $entry.sha256 -notmatch '^[0-9a-fA-F]{64}$') { throw 'Invalid official Unicode source metadata.' }
    if ($entry.path -ne 'LICENSE.txt' -and -not $uri.AbsolutePath.StartsWith('/Public/18.0.0/ucd/', [StringComparison]::Ordinal)) { throw 'Unicode source has an unexpected version.' }
    $actual = (Get-FileHash -LiteralPath (Join-Path $DataDirectory $entry.path) -Algorithm SHA256).Hash
    if ($actual -ne $entry.sha256) { throw "Unicode hash mismatch: $($entry.path)" }
}
if (-not (Test-Path -LiteralPath (Join-Path $DataDirectory 'math-aliases.tsv') -PathType Leaf)) { throw 'Missing curated mathematics aliases.' }
Write-Host 'Unicode 18.0.0: five official files match sources.json; curated aliases are present.'
