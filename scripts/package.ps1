[CmdletBinding()]
param([string]$RequiredInnoVersion = '')
. (Join-Path $PSScriptRoot 'common.ps1')
$root = Get-RepositoryRoot
$version = Get-ReleaseVersion $root
$dotnet = Resolve-BuildTool dotnet
$sdk = Assert-DotnetSdk $dotnet
$iscc = Resolve-BuildTool inno
$innoVersion = Get-InnoVersion $iscc
if ([version]$innoVersion -lt [version]'6.7.0' -or ($RequiredInnoVersion -and $innoVersion -ne $RequiredInnoVersion)) {
    throw "Inno Setup 6.7+ is required locally; requested '$RequiredInnoVersion', resolved '$innoVersion'. CI pins 7.1.0."
}
$dist = Join-Path $root 'dist'
[void](New-Item -ItemType Directory -Path $dist -Force)
$names = @("Winicode-Setup-$version-win-x64.exe", "Winicode-Portable-$version-win-x64.zip", 'SHA256SUMS.txt')
foreach ($name in $names) {
    if (Test-Path -LiteralPath (Join-Path $dist $name)) { throw "Existing dist/$name. Move reviewed outputs aside before packaging again." }
}
$work = Join-Path $root ('artifacts/package-' + [guid]::NewGuid().ToString('N'))
$published = Join-Path $work 'publish'
$installed = Join-Path $work 'installed'
$portable = Join-Path $work 'portable'
$outputs = Join-Path $work 'output'
foreach ($path in @($published, $installed, $portable, $outputs)) { [void](New-Item -ItemType Directory -Path $path -Force) }
Push-Location $root
try {
    # Never run this while another worker is editing the app. This also runs the console tests.
    & (Join-Path $PSScriptRoot 'build.ps1')
    Invoke-Checked $dotnet @('publish', 'src/Winicode/Winicode.csproj', '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '-o', $published,
        '-p:PublishTrimmed=false', '-p:PublishSingleFile=false', '-p:PublishAot=false', '-p:DebugType=None', '-p:DebugSymbols=false',
        '-p:CopyOutputSymbolsToPublishDirectory=false', '-p:ContinuousIntegrationBuild=true', '-p:Deterministic=true')
    foreach ($name in @('Winicode.exe', 'Winicode.dll', 'Winicode.runtimeconfig.json', 'coreclr.dll', 'hostfxr.dll', 'PresentationFramework.dll')) {
        if (-not (Test-Path -LiteralPath (Join-Path $published $name) -PathType Leaf)) { throw "Incomplete self-contained WPF publish: $name" }
    }
    [void]@(Get-PayloadFiles $published)
    foreach ($marker in @('portable.flag', 'install-mode.json', 'payload-manifest.json', 'release-manifest.json')) {
        if (Test-Path -LiteralPath (Join-Path $published $marker)) { throw "Unexpected marker in dotnet output: $marker" }
    }
    & (Join-Path $PSScriptRoot 'verify-unicode.ps1') -DataDirectory (Join-Path $published 'data')
    $publicDocs = @('README.md', 'LICENSE', 'THIRD-PARTY-NOTICES.md', 'CHANGELOG.md', 'CONTRIBUTING.md', 'SECURITY.md')
    foreach ($name in $publicDocs) { Copy-Item -LiteralPath (Join-Path $root $name) -Destination $published }
    [void](New-Item -ItemType Directory -Path (Join-Path $published 'docs'))
    foreach ($name in @('user-guide.md', 'troubleshooting.md', 'privacy.md', 'architecture.md', 'release-checklist.md', 'validation-0.1.0-beta.1.md')) {
        Copy-Item -LiteralPath (Join-Path $root "docs/$name") -Destination (Join-Path $published 'docs')
    }
    # The redistributable's .NET notices ship alongside our notices, with their original text.
    $dotnetRoot = Split-Path $dotnet -Parent
    $licenses = Join-Path $published 'licenses'
    [void](New-Item -ItemType Directory -Path $licenses)
    foreach ($item in @(@('LICENSE.txt', 'dotnet-LICENSE.txt'), @('ThirdPartyNotices.txt', 'dotnet-THIRD-PARTY-NOTICES.txt'))) {
        $source = Join-Path $dotnetRoot $item[0]
        if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw "Missing .NET redistribution notice $($item[0]) beside dotnet.exe. Supply the official SDK distribution." }
        Copy-Item -LiteralPath $source -Destination (Join-Path $licenses $item[1])
    }
    Copy-Item -LiteralPath (Join-Path $root 'installer/licenses/InnoSetup-LICENSE.txt') -Destination $licenses
    $commit = $null
    if ((Test-Path -LiteralPath (Join-Path $root '.git')) -and (Get-Command git.exe -ErrorAction SilentlyContinue)) {
        $revision = & git.exe rev-parse --verify HEAD 2>$null
        if ($LASTEXITCODE -eq 0 -and "$revision" -match '^[0-9a-f]{40,64}$') { $commit = "$revision" }
    }
    $manifest = [ordered]@{ product = 'Winicode'; version = $version; runtimeIdentifier = 'win-x64'; selfContained = $true; trimmed = $false;
        singleFile = $false; sdk = $sdk; innoSetup = $innoVersion; unicode = '18.0.0'; commit = $commit;
        builtAtUtc = [DateTime]::UtcNow.ToString('o'); repository = 'https://github.com/th-1056/Winicode'; signed = $false }
    Write-Utf8File (Join-Path $published 'release-manifest.json') (ConvertTo-Json $manifest -Depth 4)
    foreach ($directory in @($installed, $portable)) {
        Get-ChildItem -LiteralPath $published -Force | Copy-Item -Destination $directory -Recurse
    }
    Write-Utf8File (Join-Path $installed 'install-mode.json') (ConvertTo-Json ([ordered]@{ mode = 'installed'; appId = '5B1D09D9-5EAC-4D73-BC78-4F6F9E32A971'; version = $version }))
    Write-Utf8File (Join-Path $portable 'portable.flag') ''
    Write-PayloadManifest $installed
    Write-PayloadManifest $portable
    $compilerArguments = if ([version]$innoVersion -ge [version]'7.0.0') {
        @('-qp', "-dAppVersion=$version", "-dPayloadDir=$installed", "-dOutputDir=$outputs")
    } else {
        @('/Qp', "/DAppVersion=$version", "/DPayloadDir=$installed", "/DOutputDir=$outputs")
    }
    Invoke-Checked $iscc ($compilerArguments + @((Join-Path $root 'installer/Winicode.iss')))
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::CreateFromDirectory($portable, (Join-Path $outputs $names[1]), [IO.Compression.CompressionLevel]::Optimal, $false)
    $checksums = foreach ($name in $names[0..1]) {
        $hash = (Get-FileHash -LiteralPath (Join-Path $outputs $name) -Algorithm SHA256).Hash.ToLowerInvariant()
        "$hash  $name"
    }
    Write-Utf8File (Join-Path $outputs 'SHA256SUMS.txt') (($checksums -join "`n") + "`n")
    & (Join-Path $PSScriptRoot 'verify-release.ps1') -Directory $outputs
    # Commit the verified set last. Preserve any earlier release and failed staging for inspection.
    foreach ($name in $names) { [IO.File]::Move((Join-Path $outputs $name), (Join-Path $dist $name)) }
    Write-Host "Release files: $dist"
    Write-Host "Staging retained for inspection: $work"
} finally { Pop-Location }
