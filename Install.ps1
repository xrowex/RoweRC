#requires -Version 7.0
param([string]$GameFolder = '')
$ErrorActionPreference = 'Stop'
if (!$GameFolder) {
    $manifestFolder = 'C:\ProgramData\Epic\EpicGamesLauncher\Data\Manifests'
    $matches = @(Get-ChildItem -LiteralPath $manifestFolder -Filter '*.item' | ForEach-Object {
        $m = Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json
        if ($m.DisplayName -eq 'RCTandems') { $m.InstallLocation }
    })
    if ($matches.Count -ne 1) { throw 'Pass -GameFolder with the RC Tandems installation folder.' }
    $GameFolder = $matches[0]
}
$gameRoot = [IO.Path]::GetFullPath($GameFolder).TrimEnd('\')
$exe = Join-Path $gameRoot 'RCDriftSim\Binaries\Win64\RCDriftSim-Win64-Shipping.exe'
if (!(Test-Path -LiteralPath $exe) -or (Get-FileHash -LiteralPath $exe).Hash.ToLowerInvariant() -ne '5269616427df15ffc507970aa290193fddd60f83b7807231067455aa1a292dab') {
    throw 'This installer requires RC Tandems build 3c46a39f90.'
}
if (Get-Process -Name 'RCDriftSim-Win64-Shipping' -ErrorAction SilentlyContinue) { throw 'Close RC Tandems before installing.' }
if (Get-Process -Name 'RoweRC' -ErrorAction SilentlyContinue) { throw 'Wait for the RoweRC helper to exit after closing the game.' }
$payload = Join-Path $PSScriptRoot 'InstallFiles'
if (!(Test-Path -LiteralPath $payload)) { throw 'InstallFiles is missing. Run this script from the extracted prototype package.' }
$receiptPath = Join-Path $gameRoot 'RCDriftSim\Binaries\Win64\RoweRC\install-receipt.json'
$old = @{}
if (Test-Path -LiteralPath $receiptPath) { $old = (Get-Content -LiteralPath $receiptPath -Raw | ConvertFrom-Json -AsHashtable).Files }
$incoming = @()
foreach ($f in Get-ChildItem -LiteralPath $payload -File -Recurse) {
    $relative = [IO.Path]::GetRelativePath($payload, $f.FullName)
    $target = [IO.Path]::GetFullPath((Join-Path $gameRoot $relative))
    if (!$target.StartsWith($gameRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Payload path escapes the game folder.' }
    $hash = (Get-FileHash -LiteralPath $f.FullName).Hash.ToLowerInvariant()
    $skip = $relative.EndsWith('RoweRC\settings.json') -and (Test-Path -LiteralPath $target)
    if ((Test-Path -LiteralPath $target) -and !$skip) {
        $current = (Get-FileHash -LiteralPath $target).Hash.ToLowerInvariant()
        if ($current -ne $hash -and (!$old.ContainsKey($relative) -or $current -ne $old[$relative])) {
            throw "Existing modified/foreign file needs manual review before replacement: $target"
        }
    }
    $incoming += [PSCustomObject]@{Source=$f.FullName; Relative=$relative; Target=$target; Hash=$hash; Skip=$skip}
}
# All conflicts are checked before the first file is changed.
$files = @{}
foreach ($f in $incoming) {
    if (!$f.Skip) {
        New-Item -ItemType Directory -Path (Split-Path $f.Target -Parent) -Force | Out-Null
        Copy-Item -LiteralPath $f.Source -Destination $f.Target -Force
    }
    $files[$f.Relative] = (Get-FileHash -LiteralPath $f.Target).Hash.ToLowerInvariant()
}
@{Version='0.1.5-experimental'; GameBuild='3c46a39f90'; InstalledUtc=[DateTime]::UtcNow.ToString('o'); Files=$files} |
    ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $receiptPath -Encoding utf8
Write-Output 'RoweRC installed. Launch RC Tandems normally and press F8 for Friends + Maps.'
