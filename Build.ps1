param([string]$ShellPackFolder = (Join-Path $PSScriptRoot 'ShellPacks'))
$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
$shellManifest=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'ShellPacks\manifest.json') -Raw | ConvertFrom-Json -AsHashtable
foreach($name in $shellManifest.Files.Keys) {
    $source=Join-Path $ShellPackFolder $name
    if(-not (Test-Path -LiteralPath $source -PathType Leaf) -or (Get-FileHash -LiteralPath $source).Hash.ToLowerInvariant() -ne $shellManifest.Files[$name]) {
        throw "Missing or changed cooked shell file: $source. Extract the six shell files from the release into ShellPacks, or pass -ShellPackFolder."
    }
}
if (!(Test-Path tools\ue4ss\dwmapi.dll) -or !(Test-Path tools\steam_api64.dll)) {
    throw 'Restore the pinned UE4SS and Steamworks.NET native dependencies described in THIRD_PARTY.md into tools first.'
}
$terrain = Join-Path $PSScriptRoot 'Maps\Support\apex-wangan.rrcterrain'
$compressedTerrain = $terrain + '.gz'
if (!(Test-Path -LiteralPath $terrain) -and (Test-Path -LiteralPath $compressedTerrain)) {
    $terrainInput=[IO.File]::OpenRead($compressedTerrain);$terrainOutput=[IO.File]::Create($terrain)
    $terrainGzip=[IO.Compression.GZipStream]::new($terrainInput,[IO.Compression.CompressionMode]::Decompress)
    try {$terrainGzip.CopyTo($terrainOutput)} finally {$terrainGzip.Dispose();$terrainInput.Dispose();$terrainOutput.Dispose()}
}
if (!(Test-Path -LiteralPath $terrain)) { throw 'Bake the preserved APEX terrain with tools/bake_apex_terrain.py before building this adapter.' }
$terrainSource = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'src/RoweRC/TerrainSupport.cs') -Raw
$terrainExpected = [regex]::Match($terrainSource, 'DataSha256 = "([a-f0-9]{64})"').Groups[1].Value
if (!$terrainExpected -or (Get-FileHash -LiteralPath $terrain).Hash.ToLowerInvariant() -ne $terrainExpected) { throw 'Terrain data differs from the validated city support profile.' }
if (!(Test-Path -LiteralPath $compressedTerrain) -or (Get-Item -LiteralPath $compressedTerrain).LastWriteTimeUtc -lt (Get-Item -LiteralPath $terrain).LastWriteTimeUtc) {
    $terrainInput = [IO.File]::OpenRead($terrain)
    $terrainOutput = [IO.File]::Create($compressedTerrain)
    $terrainGzip = [IO.Compression.GZipStream]::new($terrainOutput,[IO.Compression.CompressionLevel]::Optimal)
    try { $terrainInput.CopyTo($terrainGzip) } finally { $terrainGzip.Dispose(); $terrainOutput.Dispose(); $terrainInput.Dispose() }
}
dotnet publish src\RoweRC\RoweRC.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o dist\RoweRC
if ($LASTEXITCODE) { throw 'Managed build failed.' }
# Keep the pinned native library explicit even when incremental single-file publish skips it.
Copy-Item tools\steam_api64.dll dist\RoweRC\steam_api64.dll -Force
if (!(Test-Path tools\bootstrap\bootstrap.dll) -or (Get-ChildItem src\bootstrap -File | Where-Object LastWriteTimeUtc -gt (Get-Item tools\bootstrap\bootstrap.dll).LastWriteTimeUtc)) {
    $vswhere = 'C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe'
    $vs = & $vswhere -latest -property installationPath
    $vcvars = Join-Path $vs 'VC\Auxiliary\Build\vcvars64.bat'
    New-Item -ItemType Directory -Path tools\bootstrap -Force | Out-Null
    Push-Location tools\bootstrap
    try {
        $cpp = Join-Path $PSScriptRoot 'src\bootstrap\bootstrap.cpp'
        $contactCpp = Join-Path $PSScriptRoot 'src\bootstrap\native_contact.cpp'
        $terrainCpp = Join-Path $PSScriptRoot 'src\bootstrap\native_terrain.cpp'
        & cmd.exe /d /s /c ('"' + $vcvars + '" >nul && cl /nologo /std:c++20 /O2 /MT /LD /EHsc "' + $cpp + '" "' + $contactCpp + '" "' + $terrainCpp + '" /link /OUT:bootstrap.dll /SUBSYSTEM:WINDOWS')
        if ($LASTEXITCODE) { throw 'Bootstrap build failed.' }
    } finally { Pop-Location }
}
$stage = Join-Path $PSScriptRoot 'dist\RoweRC\InstallFiles\RCDriftSim\Binaries\Win64'
New-Item -ItemType Directory -Path $stage -Force | Out-Null
Copy-Item tools\ue4ss\dwmapi.dll $stage -Force
Copy-Item tools\ue4ss\ue4ss $stage -Recurse -Force
$scripts = Join-Path $stage 'ue4ss\Mods\RoweRC\Scripts'
New-Item -ItemType Directory -Path $scripts -Force | Out-Null
Copy-Item mod\RoweRC\Scripts\*.lua $scripts -Force
$shellStage=Join-Path $PSScriptRoot 'dist\RoweRC\InstallFiles\RCDriftSim\Content\Paks\~mods\RoweRC\Shells'
New-Item -ItemType Directory -Path $shellStage -Force | Out-Null
foreach($name in $shellManifest.Files.Keys){Copy-Item -LiteralPath (Join-Path $ShellPackFolder $name) -Destination (Join-Path $shellStage $name) -Force}
"RoweRC : 1`nKeybinds : 1" | Set-Content (Join-Path $stage 'ue4ss\Mods\mods.txt')
$helper = Join-Path $stage 'RoweRC'
New-Item -ItemType Directory -Path $helper -Force | Out-Null
Copy-Item dist\RoweRC\RoweRC.exe,tools\steam_api64.dll,tools\bootstrap\bootstrap.dll $helper -Force
New-Item -ItemType Directory -Path (Join-Path $helper 'Terrain') -Force | Out-Null
Copy-Item -LiteralPath $terrain -Destination (Join-Path $helper 'Terrain\apex-wangan.rrcterrain') -Force
if (Test-Path dist\RoweRC\licenses) { Copy-Item dist\RoweRC\licenses $helper -Recurse -Force }
'{"GameFolder":"","SteamAppId":480}' | Set-Content (Join-Path $helper 'settings.json')
Copy-Item README.md,CUSTOM_MAPS.md,CUSTOM_SHELLS.md,MAP_RELEASES.md,RELEASE_NOTES.md,THIRD_PARTY.md,Install.ps1,Install-Shells.ps1 dist\RoweRC -Force
Write-Output 'Staged prototype at dist/RoweRC. Preserve licenses/ when packaging.'
