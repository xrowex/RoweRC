#requires -Version 7.0
param([string]$GameFolder='C:\Program Files\Epic Games\RCTandemszQS5H')
$ErrorActionPreference='Stop'
$gameRoot=[IO.Path]::GetFullPath($GameFolder)
foreach($relative in @('RCDriftSim\Binaries\Win64\dwmapi.dll','RCDriftSim\Binaries\Win64\ue4ss\UE4SS.dll','RCDriftSim\Binaries\Win64\RoweRC\RoweRC.exe')) {
    if(-not (Test-Path -LiteralPath (Join-Path $gameRoot $relative) -PathType Leaf)) {throw 'Install the working RoweRC mod before applying this shell/menu update.'}
}
& (Join-Path $PSScriptRoot 'Install.ps1') -GameFolder $gameRoot
Write-Output 'Shell/menu update installed. Restart RC Tandems, then open F8 > Car.'
