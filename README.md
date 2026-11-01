# RoweRC 0.1.8 for RC Tandems

Experimental APEX city test release: grounded road height and banking, city obstacle contacts, and matching camera height. Local boundary and camera checks passed; a full city lap and two-PC acceptance remain unverified.

Steam lobbies, custom shells and experimental car contact for the Epic Windows RC Tandems **v2.2.5 / build `1801ffbdfe`**, Unreal Engine 5.7.4. Press **F8** for Online, Car and Maps. The Steam helper starts in the background with the game.

## Install or upgrade

1. Update RC Tandems in Epic to the supported build. Close RC Tandems and wait for RoweRC to exit.
2. Download and extract **RoweRC-prototype-0.1.8.zip** from the release.
3. Open PowerShell 7 in the extracted `RoweRC` folder and run `./Install.ps1`. Use `-GameFolder 'C:\Your\RCTandems'` for a different location. The installer checks the game build and existing file receipts before copying files; it preserves your settings.
4. Restart the game and open **F8 → Car** to select the Pickup or Supra RC shell and adjust rim metallic.

**The upgrade from 0.1.5 requires this full installer.** Existing 0.1.6 and 0.1.7 users can use the signed F8 updater, then close the game to install. The new helper carries and automatically extracts its verified terrain support, so the older updater does not need to install a new file type.

Download **APEX-Wangan-1.0.0-test.4.zip** separately from the same release. Keep the ZIP intact and copy it to `%LOCALAPPDATA%/RoweRC/Maps/Inbox`. In **F8 → Maps**, click **Reload custom maps**, restart RC Tandems, select **APEX Wangan City**, then choose **Load / change host map**. Both players need the mod update and the same map ZIP.

For a clean unmodded install, you can also copy the complete `InstallFiles/RCDriftSim` tree into the game root beside `RCDriftSim.exe`. Keep the loader, `ue4ss`, helper files and all six shell files together. For an existing mod installation, use the installer to detect conflicts.

## Play with a friend

Both players need the same RoweRC version, game build and cooked shell files, with Steam running on separate accounts.

- **Online:** Host friends or Host public, browse public lobbies, or enter a friend's lobby code. Copy the code yourself; the mod does not send invitations.
- **Maps:** Choose and load the track. The host controls maps; guests follow automatically. Load custom map ZIPs using Reload custom maps.
- **Car:** Select Pickup shell, Supra RC or Use game shell. Your native body color is retained. Rim metallic runs from 0 to 1 and can be restored to the game finish.

Close F8 and enter Drive. Steam transport uses development App ID 480, so Steam may display Spacewar. Both players must use the same App ID. The host can enable experimental car contact from Online; contact starts off in each new lobby.

## Collision only

Car-to-car contact stays available in Online. Cosmetic dent generation, procedural-mesh updates and dent replication have been removed. Shells stay rigid during impacts. Both bundled shells use offline collision outlines, so contact mode does not extract their high-poly render meshes while driving.

## Shells and maps

The Pickup and Supra RC shells are included as matching `.pak`, `.utoc` and `.ucas` triples under `RCDriftSim/Content/Paks/~mods/RoweRC/Shells`. Both peers install these locally; lobbies do not download models, textures or sounds. See [CUSTOM_SHELLS.md](CUSTOM_SHELLS.md).

Custom tracks must be cooked for UE 5.7 and use the documented package layout. See [CUSTOM_MAPS.md](CUSTOM_MAPS.md). The APEX city is a separate map download; it is not installed by the mod updater.

## Validation and limits

The two-shell UE 5.7.4 cook completed with zero errors and warnings. Local checks cover the three menu tabs, shell/material loading, rim finish, stock restoration and repeated shell changes through forced garbage collection. The shell-switching asset-cache crash is fixed by reacquiring Unreal assets after unload. Backend and Lua checks cover packets, materials, collision-only updates, physics adapters and smoothing.

Multiplayer remains experimental. Two-PC custom-shell visuals, physical impacts, high-latency contact feel and sustained tandem smoothness are not accepted yet. The host's native pair solver sends bounded, acknowledged responses to each owner; driving physics remains local. Latency can separate an apparent impact from its response. Prior two-PC testing established basic stock-car visibility and Steam traffic, not acceptance of this release's new features.

## Updates and diagnostics

After installing this release, F8 → Maps → Check for mod update requests the signed feed. An update installs after the game and helper close. Update receipts/backups are under `%LOCALAPPDATA%/RoweRC/updates`.

Runtime logs are `%LOCALAPPDATA%/RoweRC/bridge/session.log`, `error.txt`, `contact-error.txt` and `remote-appearance-error.txt`, plus `RCDriftSim/Binaries/Win64/ue4ss/UE4SS.log`. Helper startup errors use `%LOCALAPPDATA%/RoweRC/startup-error.txt`.

## Build

Restore the pinned dependencies in [THIRD_PARTY.md](THIRD_PARTY.md). Extract the six release shell files into `ShellPacks`, or provide their folder explicitly:

```powershell
./Build.ps1 -ShellPackFolder 'C:\CookedShellFiles'
dotnet src/RoweRC/bin/Release/net9.0-windows/win-x64/RoweRC.dll --self-test
python tests/check_lua.py
python tools/package.py
```

`ShellPacks/manifest.json` pins the cooked files' hashes. Source FBXs, extracted stock assets, authoring projects, caches and private signing keys are excluded from the source release.
