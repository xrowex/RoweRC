#requires -Version 7.0
param([string]$Version = '0.1.8', [string]$Repository = 'xrowex/RoweRC')
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+$' -or $Repository -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$') { throw 'Invalid version or repository.' }
$stage = Join-Path $PSScriptRoot 'dist\RoweRC\InstallFiles'
$paths = @(
    'RCDriftSim/Binaries/Win64/RoweRC/RoweRC.exe',
    'RCDriftSim/Binaries/Win64/RoweRC/bootstrap.dll',
    'RCDriftSim/Binaries/Win64/RoweRC/steam_api64.dll'
)
$scripts=Join-Path $stage 'RCDriftSim/Binaries/Win64/ue4ss/Mods/RoweRC/Scripts'
$paths+=@(Get-ChildItem -LiteralPath $scripts -Filter '*.lua' -File | Sort-Object Name | ForEach-Object {'RCDriftSim/Binaries/Win64/ue4ss/Mods/RoweRC/Scripts/'+$_.Name})
$manifest=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'ShellPacks/manifest.json') -Raw | ConvertFrom-Json -AsHashtable
$paths+=@($manifest.Files.Keys | Sort-Object | ForEach-Object {'RCDriftSim/Content/Paks/~mods/RoweRC/Shells/'+$_})
$output = Join-Path $PSScriptRoot "dist\update-$Version.zip"
if (Test-Path -LiteralPath $output) { Remove-Item -LiteralPath $output }
Add-Type -AssemblyName System.IO.Compression
$zip = [IO.Compression.ZipFile]::Open($output, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($relative in $paths) {
        $source = Join-Path $stage $relative
        if (!(Test-Path -LiteralPath $source)) { throw "Missing payload: $source" }
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $source, $relative, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
} finally { $zip.Dispose() }
$keyPath = Join-Path $env:USERPROFILE '.codex\keys\rowerc-updates-private.pem'
$rsa = [Security.Cryptography.RSA]::Create()
try {
    $rsa.ImportFromPem([IO.File]::ReadAllText($keyPath))
    $payload = @{
        Version=$Version; GameBuild='1801ffbdfe'; Size=(Get-Item -LiteralPath $output).Length
        Url="https://github.com/$Repository/releases/download/v$Version/update-$Version.zip"
        Sha256=(Get-FileHash -LiteralPath $output).Hash.ToLowerInvariant()
        Notes='APEX city grounded driving: road height and banking, camera height, city obstacle contacts, and custom-map arena-boundary support. Both players need this update and the matching city map. Deformation remains off.'
    } | ConvertTo-Json -Compress
    $bytes = [Text.Encoding]::UTF8.GetBytes($payload)
    $signature = $rsa.SignData($bytes, [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pss)
    @{Payload=[Convert]::ToBase64String($bytes); Signature=[Convert]::ToBase64String($signature)} | ConvertTo-Json |
        Set-Content -LiteralPath (Join-Path $PSScriptRoot 'dist\update.json') -Encoding utf8NoBOM
} finally { $rsa.Dispose() }
Write-Output "Prepared signed update $Version. No files have been published."
