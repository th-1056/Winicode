[CmdletBinding()]
param()
. (Join-Path $PSScriptRoot 'common.ps1')
$root=Get-RepositoryRoot
$target=Join-Path $root '.tools/inno'
$compiler=Join-Path $target 'ISCC.exe'
if(Test-Path -LiteralPath $compiler){if((& $compiler --version).Trim() -eq '7.1.0'){return}}
$download=Join-Path $root '.tools/innosetup-7.1.0-x64.exe'
[void](New-Item -ItemType Directory -Path (Split-Path $download -Parent) -Force)
if(-not(Test-Path -LiteralPath $download)){
    Invoke-WebRequest 'https://github.com/jrsoftware/issrc/releases/download/is-7_1_0/innosetup-7.1.0-x64.exe' -OutFile $download
}
$hash=(Get-FileHash -LiteralPath $download -Algorithm SHA256).Hash
if($hash -ne '0362A383ED217D4C4239B5933866DD96D3EB2102737DA92F80F6057A4B40DF2F'){throw 'Inno compiler download hash mismatch.'}
$signature=Get-AuthenticodeSignature -LiteralPath $download
if($signature.Status -ne 'Valid'){throw 'Inno compiler signature is not valid.'}
# The official portable switch extracts tools without uninstall registration or shell integration.
$process=Start-Process -FilePath $download -ArgumentList @('/PORTABLE=1','/CURRENTUSER','/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/NOICONS','/TASKS=""',('/DIR="'+$target+'"')) -WindowStyle Hidden -PassThru
$process.WaitForExit()
if($process.ExitCode -ne 0 -or -not(Test-Path -LiteralPath $compiler)){throw 'Portable compiler extraction failed.'}
if((& $compiler --version).Trim() -ne '7.1.0'){throw 'Unexpected extracted compiler version.'}
Write-Host 'Verified Inno Setup 7.1.0 is ready in .tools/inno.'
