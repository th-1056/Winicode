Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-RepositoryRoot {
    [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
}

function Get-ReleaseVersion {
    param([string]$Root = (Get-RepositoryRoot))
    [xml]$project = Get-Content -LiteralPath (Join-Path $Root 'src/Winicode/Winicode.csproj') -Raw
    $versions = @($project.Project.PropertyGroup.Version | Where-Object { $_ })
    if ($versions.Count -ne 1) { throw 'The application project must have exactly one Version.' }
    $version = [string]$versions[0]
    # Three numeric core components, SemVer prerelease/build identifiers, safe for filenames and ISPP.
    if ($version -cnotmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$') {
        throw "Invalid project release version: $version"
    }
    $prerelease = ($version -split '\+', 2)[0] -split '-', 2
    if ($prerelease.Count -eq 2) {
        foreach ($identifier in ($prerelease[1] -split '\.')) {
            if ($identifier -match '^0[0-9]+$') { throw 'Numeric prerelease identifiers cannot have leading zeroes.' }
        }
    }
    $contracts = Get-Content -LiteralPath (Join-Path $Root 'src/Winicode/Contracts.cs') -Raw
    if ($contracts -notmatch ('\bVersion\s*=\s*"' + [regex]::Escape($version) + '"')) {
        throw 'BuildInfo.Version and the project Version must match before packaging.'
    }
    $version
}

function Resolve-BuildTool {
    param([ValidateSet('dotnet', 'inno')][string]$Name)
    $root = Get-RepositoryRoot
    $override = if ($Name -eq 'dotnet') { $env:WINICODE_DOTNET } else { $env:WINICODE_ISCC }
    if ($override) { if (-not(Test-Path -LiteralPath $override -PathType Leaf)) {throw 'Configured build tool was not found.'};return [IO.Path]::GetFullPath($override) }
    $local = if ($Name -eq 'dotnet') { '.tools/dotnet/dotnet.exe' } else { '.tools/inno/ISCC.exe' }
    $command = if ($Name -eq 'dotnet') { 'dotnet.exe' } else { 'ISCC.exe' }
    $localPath = Join-Path $root $local
    if (Test-Path -LiteralPath $localPath -PathType Leaf) { return [IO.Path]::GetFullPath($localPath) }
    $cacheTool=Join-Path (Split-Path $root -Parent) ('WinicodeBuildCache/tools/'+$(if($Name -eq 'dotnet'){'dotnet/dotnet.exe'}else{'inno/ISCC.exe'}))
    if(Test-Path -LiteralPath $cacheTool -PathType Leaf){return [IO.Path]::GetFullPath($cacheTool)}
    $found = Get-Command $command -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -eq $found) { throw "Missing $command. Supply $local or put $command on this process's PATH. No tool is installed automatically." }
    $found.Source
}

function Invoke-Checked {
    param([string]$File, [string[]]$Arguments)
    & $File @Arguments | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "$(Split-Path $File -Leaf) failed with exit code $LASTEXITCODE." }
}

function Assert-DotnetSdk {
    param([string]$Dotnet)
    $sdk = (& $Dotnet --version | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or $sdk -notmatch '^10\.[0-9]+\.[0-9]+$') {
        throw "A stable .NET 10 SDK is required; resolved '$sdk'. CI uses 10.0.402."
    }
    $sdk
}

function Get-InnoVersion {
    param([string]$Compiler)
    $info = (Get-Item -LiteralPath $Compiler).VersionInfo
    $version = '{0}.{1}.{2}' -f $info.FileMajorPart, $info.FileMinorPart, $info.FileBuildPart
    if ([version]$version -ge [version]'6.7.0') { return $version }
    # Official portable Inno 7 binaries can carry a zeroed Windows version resource.
    $reported = (& $Compiler --version 2>&1 | Out-String).Trim()
    if ($LASTEXITCODE -eq 0 -and $reported -match '(?m)\b([0-9]+\.[0-9]+\.[0-9]+)\b') { return $Matches[1] }
    $help = (& $Compiler '/?' 2>&1 | Out-String)
    if ($help -match '(?im)^.*Compiler.*\r?\n(?:.*\r?\n)*?\s*(?:Compiler engine version:\s*)?([0-9]+\.[0-9]+\.[0-9]+)\b') { return $Matches[1] }
    throw 'Cannot determine the Inno Setup compiler version from its file resource or CLI.'
}

function Assert-ChildPath {
    param([string]$Path, [string]$Parent)
    $full = [IO.Path]::GetFullPath($Path)
    $prefix = [IO.Path]::GetFullPath($Parent).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    if (-not $full.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'The target must be a child of the designated output directory.'
    }
    $full
}

function Write-Utf8File {
    param([string]$Path, [string]$Text)
    [IO.File]::WriteAllText($Path, $Text, [Text.UTF8Encoding]::new($false))
}

function Get-RelativeFileName {
    param([string]$Root, [string]$Path)
    $prefix = [IO.Path]::GetFullPath($Root).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    $full = Assert-ChildPath $Path $Root
    $full.Substring($prefix.Length).Replace('\', '/')
}

function Get-PayloadFiles {
    param([string]$Root)
    $items = @(Get-ChildItem -LiteralPath $Root -Recurse -Force)
    foreach ($item in $items) {
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Reparse points cannot enter a release payload.' }
        $relative = Get-RelativeFileName $Root $item.FullName
        if ($relative -match '(?i)(^|/)(bin|obj|\.git|\.tools|\.vs|userdata|logs|artifacts|dist)(/|$)' -or
            $relative -match '(?i)\.(pdb|log|pfx|p12|pem|key|user|suo|dmp|png|jpg|zip|ps1)$' -or
            $relative -match '(?i)(^|/)(settings|aliases|credentials|secrets|cookies)\.json$' -or
            $relative -match '(?i)testhost|test-results|target-text|screenshot') {
            throw "Development or user content found in payload: $relative"
        }
        if (-not $item.PSIsContainer) { $item }
    }
}

function Write-PayloadManifest {
    param([string]$Root)
    $rows = foreach ($file in (Get-PayloadFiles $Root | Sort-Object FullName)) {
        [ordered]@{ path = Get-RelativeFileName $Root $file.FullName; size = $file.Length; sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
    }
    Write-Utf8File (Join-Path $Root 'payload-manifest.json') (ConvertTo-Json -InputObject @($rows) -Depth 4)
}
