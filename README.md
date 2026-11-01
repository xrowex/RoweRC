# RoweRC 0.1.6 for RC Tandems

Steam lobbies, custom shells and experimental car contact for the Epic Windows RC Tandems **v2.2.5 / build `1801ffbdfe`**, Unreal Engine 5.7.4. Press **F8** for Online, Car and Maps. The Steam helper starts in the background with the game.

## Install or upgrade

1. Update RC Tandems in Epic to the supported build. Close RC Tandems and wait for RoweRC to exit.
2. Download and extract **RoweRC-prototype-0.1.6.zip** from the release.
3. Open PowerShell 7 in the extracted `RoweRC` folder and run `./Install.ps1`. Use `-GameFolder 'C:\Your\RCTandems'` for a different location. The installer checks the game build and existing file receipts before copying files; it preserves your settings.
4. Restart the game and open **F8 → Car** to select the Pickup or Supra RC shell and adjust rim metallic.

**The upgrade from 0.1.5 requires this full installer.** The 0.1.5 updater accepts the previous game build and cannot install the new shell containers. The new updater includes support for the six owned shell files in future updates.

For a clean unmodded install, you can also copy the complete `InstallFiles/RCDriftSim` tree into the game root beside `RCDriftSim.exe`. Keep the loader, `ue4ss`, helper files and all six shell files together. For an existing mod installation, use the installer to detect conflicts.

## Play with a friend

Both players need the same RoweRC version, game build and cooked shell files, with Steam running on separate accounts.

- **Online:** Host friends or Host public, browse public lobbies, or enter a friend's lobby code. Copy the code yourself; the mod does not send invitations.
- **Maps:** Choose and load the track. The host controls maps; guests follow automatically. Load custom map ZIPs using Reload custom maps.
- **Car:** Select Pickup shell, Supra RC or Use game shell. Your native body color is retained. Rim metallic runs from 0 to 1 and can be restored to the game finish.

Close F8 and enter Drive. Steam transport uses development App ID 480, so Steam may display Spacewar. Both players must use the same App ID. The host can enable experimental car contact from Online; contact starts off in each new lobby.

## Deformation

Cosmetic shell denting is included. Acknowledged mod car-contact impulses can dent the paint/body section; Reset Car repairs it, and changing shells clears its dents. Up to four dents are retained, each capped at 0.8 cm with a 1.2 cm total displacement limit. The wheel and chassis collision shapes remain unchanged. Wall-impact deformation is not connected in this release.

Pickup and Supra geometry, dent application and repair are checked locally. Dent state is included in reliable peer messages and accepts the custom-shell namespace. Real two-PC shell/dent visuals and impacts still need playtesting.

## Shells and maps

The Pickup and Supra RC shells are included as matching `.pak`, `.utoc` and `.ucas` triples under `RCDriftSim/Content/Paks/~mods/RoweRC/Shells`. Both peers install these locally; lobbies do not download models, textures or sounds. See [CUSTOM_SHELLS.md](CUSTOM_SHELLS.md).

Custom tracks must be cooked for UE 5.7 and use the documented package layout. See [CUSTOM_MAPS.md](CUSTOM_MAPS.md). No newly cooked track is included in this release.

## Validation and limits

The two-shell UE 5.7.4 cook completed with zero errors and warnings. Local checks cover the three menu tabs, shell/material loading, rim finish, stock restoration, cosmetic dent geometry and reset, and repeated shell changes through forced garbage collection. The shell-switching asset-cache crash is fixed by reacquiring Unreal assets after unload. Backend and Lua checks cover packets, materials, damage, updates, physics adapters and smoothing.

Multiplayer remains experimental. Two-PC custom-shell/dent visuals, physical impacts, high-latency contact feel and sustained tandem smoothness are not accepted yet. The host's native pair solver sends bounded, acknowledged responses to each owner; driving physics remains local. Latency can separate an apparent impact from its response. Prior two-PC testing established basic stock-car visibility and Steam traffic, not acceptance of this release's new features.

## Updates and diagnostics

After installing this release, F8 → Maps → Check for mod update requests the signed feed. An update installs after the game and helper close. Update receipts/backups are under `%LOCALAPPDATA%/RoweRC/updates`.

Runtime logs are `%LOCALAPPDATA%/RoweRC/bridge/session.log`, `error.txt`, `damage-error.txt` and `remote-appearance-error.txt`, plus `RCDriftSim/Binaries/Win64/ue4ss/UE4SS.log`. Helper startup errors use `%LOCALAPPDATA%/RoweRC/startup-error.txt`.

## Build

Restore the pinned dependencies in [THIRD_PARTY.md](THIRD_PARTY.md). Extract the six release shell files into `ShellPacks`, or provide their folder explicitly:

```powershell
./Build.ps1 -ShellPackFolder 'C:\CookedShellFiles'
dotnet src/RoweRC/bin/Release/net9.0-windows/win-x64/RoweRC.dll --self-test
python tests/check_lua.py
python tools/package.py
```

`ShellPacks/manifest.json` pins the cooked files' hashes. Source FBXs, extracted stock assets, authoring projects, caches and private signing keys are excluded from the source release.
