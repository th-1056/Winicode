[CmdletBinding()]
param([switch]$VerifyOnly)
. (Join-Path $PSScriptRoot 'common.ps1')
if ($VerifyOnly) { & (Join-Path $PSScriptRoot 'verify-unicode.ps1'); return }

# sources.json is the reviewed pin. A refresh never changes its version, URLs or hashes.
$root = Get-RepositoryRoot
$data = Join-Path $root 'data'
$metadata = Get-Content -LiteralPath (Join-Path $data 'sources.json') -Raw | ConvertFrom-Json
$staging = Join-Path $root ('artifacts/unicode-' + [guid]::NewGuid().ToString('N'))
[void](New-Item -ItemType Directory -Path $staging)
try {
    Copy-Item -LiteralPath (Join-Path $data 'sources.json') -Destination $staging
    Copy-Item -LiteralPath (Join-Path $data 'math-aliases.tsv') -Destination $staging
    $names = @('UnicodeData.txt', 'DerivedName.txt', 'NameAliases.txt', 'ReadMe.txt', 'LICENSE.txt')
    foreach ($entry in $metadata.files) {
        $uri = [uri]$entry.url
        if ($entry.path -cnotin $names -or $uri.Scheme -ne 'https' -or $uri.Host -cne 'www.unicode.org') { throw 'Invalid Unicode download pin.' }
        Invoke-WebRequest -Uri $uri -OutFile (Join-Path $staging $entry.path) -MaximumRedirection 0
    }
    & (Join-Path $PSScriptRoot 'verify-unicode.ps1') -DataDirectory $staging
    foreach ($entry in $metadata.files) {
        $destination = Join-Path $data $entry.path
        $source = Join-Path $staging $entry.path
        if (Test-Path -LiteralPath $destination) {
            [IO.File]::Replace($source, $destination, (Join-Path $staging ($entry.path + '.previous')))
        } else { [IO.File]::Move($source, $destination) }
    }
    & (Join-Path $PSScriptRoot 'verify-unicode.ps1')
} finally {
    $safe = Assert-ChildPath $staging (Join-Path $root 'artifacts')
    Remove-Item -LiteralPath $safe -Recurse -Force
}
