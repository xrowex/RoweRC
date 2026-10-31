#requires -Version 7.0
param([string]$Version = '0.1.4', [string]$Repository = 'xrowex/RoweRC')
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+$' -or $Repository -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$') { throw 'Invalid version or repository.' }
$stage = Join-Path $PSScriptRoot 'dist\RoweRC\InstallFiles'
$paths = @(
    'RCDriftSim/Binaries/Win64/RoweRC/RoweRC.exe',
    'RCDriftSim/Binaries/Win64/RoweRC/bootstrap.dll',
    'RCDriftSim/Binaries/Win64/RoweRC/steam_api64.dll',
    'RCDriftSim/Binaries/Win64/ue4ss/Mods/RoweRC/Scripts/main.lua',
    'RCDriftSim/Binaries/Win64/ue4ss/Mods/RoweRC/Scripts/menu.lua',
    'RCDriftSim/Binaries/Win64/ue4ss/Mods/RoweRC/Scripts/animation.lua',
    'RCDriftSim/Binaries/Win64/ue4ss/Mods/RoweRC/Scripts/smoothing.lua',
    'RCDriftSim/Binaries/Win64/ue4ss/Mods/RoweRC/Scripts/appearance.lua'
)
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
        Version=$Version; GameBuild='3c46a39f90'; Size=(Get-Item -LiteralPath $output).Length
        Url="https://github.com/$Repository/releases/download/v$Version/update-$Version.zip"
        Sha256=(Get-FileHash -LiteralPath $output).Hash.ToLowerInvariant()
        Notes='Frame-synchronized remote rendering, faster pose delivery and smoother interpolation.'
    } | ConvertTo-Json -Compress
    $bytes = [Text.Encoding]::UTF8.GetBytes($payload)
    $signature = $rsa.SignData($bytes, [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pss)
    @{Payload=[Convert]::ToBase64String($bytes); Signature=[Convert]::ToBase64String($signature)} | ConvertTo-Json |
        Set-Content -LiteralPath (Join-Path $PSScriptRoot 'dist\update.json') -Encoding utf8NoBOM
} finally { $rsa.Dispose() }
Write-Output "Prepared signed update $Version. No files have been published."
