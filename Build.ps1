$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
if (!(Test-Path tools\ue4ss\dwmapi.dll) -or !(Test-Path tools\steam_api64.dll)) {
    throw 'Restore the pinned UE4SS and Steamworks.NET native dependencies described in THIRD_PARTY.md into tools first.'
}
dotnet publish src\RoweRC\RoweRC.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o dist\RoweRC
if ($LASTEXITCODE) { throw 'Managed build failed.' }
# Keep the pinned native library explicit even when incremental single-file publish skips it.
Copy-Item tools\steam_api64.dll dist\RoweRC\steam_api64.dll -Force
if (!(Test-Path tools\bootstrap\bootstrap.dll) -or (Get-Item src\bootstrap\bootstrap.cpp).LastWriteTimeUtc -gt (Get-Item tools\bootstrap\bootstrap.dll).LastWriteTimeUtc) {
    $vswhere = 'C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe'
    $vs = & $vswhere -latest -property installationPath
    $vcvars = Join-Path $vs 'VC\Auxiliary\Build\vcvars64.bat'
    New-Item -ItemType Directory -Path tools\bootstrap -Force | Out-Null
    Push-Location tools\bootstrap
    try {
        $cpp = Join-Path $PSScriptRoot 'src\bootstrap\bootstrap.cpp'
        & cmd.exe /d /s /c ('"' + $vcvars + '" >nul && cl /nologo /O2 /MT /LD /EHsc "' + $cpp + '" /link /OUT:bootstrap.dll /SUBSYSTEM:WINDOWS')
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
"RoweRC : 1`nKeybinds : 1" | Set-Content (Join-Path $stage 'ue4ss\Mods\mods.txt')
$helper = Join-Path $stage 'RoweRC'
New-Item -ItemType Directory -Path $helper -Force | Out-Null
Copy-Item dist\RoweRC\RoweRC.exe,tools\steam_api64.dll,tools\bootstrap\bootstrap.dll $helper -Force
if (Test-Path dist\RoweRC\licenses) { Copy-Item dist\RoweRC\licenses $helper -Recurse -Force }
'{"GameFolder":"","SteamAppId":480}' | Set-Content (Join-Path $helper 'settings.json')
Copy-Item README.md,CUSTOM_MAPS.md,THIRD_PARTY.md,Install.ps1 dist\RoweRC -Force
Write-Output 'Staged prototype at dist/RoweRC. Preserve licenses/ when packaging.'
