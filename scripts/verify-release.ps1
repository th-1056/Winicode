[CmdletBinding()]
param([string]$Directory = (Join-Path $PSScriptRoot '../dist'))
. (Join-Path $PSScriptRoot 'common.ps1')
$version = Get-ReleaseVersion
$setupName = "Winicode-Setup-$version-win-x64.exe"
$zipName = "Winicode-Portable-$version-win-x64.zip"
$expected = @($setupName, $zipName)
$lines = @(Get-Content -LiteralPath (Join-Path $Directory 'SHA256SUMS.txt') | Where-Object { $_.Trim() })
if ($lines.Count -ne 2) { throw 'SHA256SUMS.txt must contain exactly the installer and portable archive.' }
$seen = @{}
foreach ($line in $lines) {
    if ($line -cnotmatch '^([a-f0-9]{64})  ([A-Za-z0-9.+-]+)$') { throw 'Malformed SHA256SUMS.txt line.' }
    $hash = $Matches[1]; $name = $Matches[2]
    if ($name -cnotin $expected -or $seen.ContainsKey($name)) { throw 'Unexpected or duplicate release asset.' }
    if ((Get-FileHash -LiteralPath (Join-Path $Directory $name) -Algorithm SHA256).Hash -ne $hash) { throw "Checksum mismatch: $name" }
    $seen[$name] = $true
}
$setup = [IO.File]::OpenRead((Join-Path $Directory $setupName))
try {
    if ($setup.Length -lt 1024 -or $setup.ReadByte() -ne 77 -or $setup.ReadByte() -ne 90) { throw 'Installer is not a Windows executable.' }
} finally { $setup.Dispose() }

Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::OpenRead((Join-Path $Directory $zipName))
try {
    $entries = @{}
    foreach ($entry in $zip.Entries) {
        $name = $entry.FullName
        if ($name -match '\\|:|^/|(^|/)\.\.?(/|$)' -or $entries.ContainsKey($name)) { throw 'Unsafe or duplicate ZIP entry.' }
        if ($name -match '(?i)(^|/)(userdata|logs|\.tools|\.git|bin|obj|artifacts|dist)(/|$)|\.(pdb|log|pfx|pem|key|dmp|ps1|png|jpg)$|testhost|target-text') { throw "Unwanted portable content: $name" }
        if (-not $name.EndsWith('/')) { $entries[$name] = $entry }
    }
    foreach ($name in @('Winicode.exe', 'Winicode.dll', 'Winicode.runtimeconfig.json', 'coreclr.dll', 'hostfxr.dll', 'PresentationFramework.dll', 'portable.flag',
            'LICENSE', 'THIRD-PARTY-NOTICES.md', 'licenses/dotnet-LICENSE.txt', 'licenses/dotnet-THIRD-PARTY-NOTICES.txt', 'licenses/InnoSetup-LICENSE.txt',
            'data/LICENSE.txt', 'data/sources.json', 'data/UnicodeData.txt', 'data/DerivedName.txt', 'data/NameAliases.txt', 'data/ReadMe.txt', 'data/math-aliases.tsv',
            'docs/user-guide.md', 'docs/troubleshooting.md', 'docs/privacy.md', 'release-manifest.json', 'payload-manifest.json')) {
        if (-not $entries.ContainsKey($name)) { throw "Portable archive lacks $name" }
    }
    if ($entries.ContainsKey('install-mode.json')) { throw 'Portable archive contains the installed-mode marker.' }
    function Read-ZipText([string]$Name) {
        $reader = [IO.StreamReader]::new($entries[$Name].Open())
        try { $reader.ReadToEnd() } finally { $reader.Dispose() }
    }
    $manifest = Read-ZipText 'release-manifest.json' | ConvertFrom-Json
    if ($manifest.version -cne $version -or $manifest.runtimeIdentifier -ne 'win-x64' -or $manifest.selfContained -ne $true -or $manifest.trimmed -ne $false -or $manifest.singleFile -ne $false) { throw 'Unexpected release manifest.' }
    $runtime = (Read-ZipText 'Winicode.runtimeconfig.json' | ConvertFrom-Json).runtimeOptions
    $frameworks = @($runtime.includedFrameworks | ForEach-Object { $_.name })
    if ($runtime.tfm -ne 'net10.0' -or 'Microsoft.NETCore.App' -notin $frameworks -or 'Microsoft.WindowsDesktop.App' -notin $frameworks) { throw 'Expected a self-contained .NET 10 Windows Desktop application.' }
    $payload = @(Read-ZipText 'payload-manifest.json' | ConvertFrom-Json)
    $listed = @{}
    foreach ($item in $payload) {
        if (-not $entries.ContainsKey($item.path) -or $listed.ContainsKey($item.path) -or $item.path -eq 'payload-manifest.json') { throw 'Invalid payload manifest file list.' }
        $entry = $entries[$item.path]
        if ($entry.Length -ne $item.size) { throw "Payload size mismatch: $($item.path)" }
        $stream = $entry.Open(); $sha = [Security.Cryptography.SHA256]::Create()
        try { $actual = ([BitConverter]::ToString($sha.ComputeHash($stream))).Replace('-', '').ToLowerInvariant() }
        finally { $stream.Dispose(); $sha.Dispose() }
        if ($actual -cne $item.sha256) { throw "Payload hash mismatch: $($item.path)" }
        $listed[$item.path] = $item
    }
    if ($listed.Count -ne $entries.Count - 1) { throw 'The ZIP contains files absent from its payload manifest.' }
    $unicode = Read-ZipText 'data/sources.json' | ConvertFrom-Json
    if ($unicode.version -ne '18.0.0' -or $unicode.files.Count -ne 5) { throw 'Unexpected packaged Unicode metadata.' }
    foreach ($entry in $unicode.files) {
        $name = 'data/' + $entry.path
        if (-not $listed.ContainsKey($name) -or $listed[$name].sha256 -ne $entry.sha256) { throw 'Packaged Unicode data failed its official pin.' }
    }
} finally { $zip.Dispose() }
Write-Host 'Installer header, both SHA-256 values, portable manifest, mode, runtime and Unicode hashes verified. No application was executed.'
