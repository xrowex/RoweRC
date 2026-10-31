# RoweRC for RC Tandems — experimental prototype

Steam friends lobbies and custom-map loading for the **Epic Windows build `3c46a39f90`**, Unreal Engine 5.7. All normal controls are inside the game: press **F8** for Friends + Maps. The Steam helper starts in the background with the mod.

## What works locally

- The runtime loader starts in the installed game and exports live car poses.
- The F8 menu renders in the game; its mouse buttons can create and leave a real friends-only Steam lobby and show its code.
- Loading the host's selected built-in level works.
- A simulated remote pose feed renders a separate car made from 80 visible car components.
- A real two-PC session received the host's car over Steam Networking Messages on Tandyz; the guest confirmed the car was visible. Local logs recorded 1,985 accepted car updates and matching host/guest map paths without a connection error.
- 49 standalone checks cover packet validation, bridge freshness, map hashes, and ZIP imports. Lua 5.4 syntax checks pass.

**Not yet verified:** both players seeing each other, host map changes with a connected guest, sustained tandem driving under varied latency, and a newly cooked custom map. Multiplayer remains experimental.

## Play with a friend

1. Both players install the same prototype onto the supported RC Tandems build. Steam must be running and signed in on separate accounts.
2. Launch RC Tandems normally. Press **F8**.
3. The host selects a track and clicks **Host selected track**. Reopen F8 after map travel to see the code, then use **Copy lobby code**. The host can change the lobby map without making everyone leave.
4. The friend pastes the code and clicks **Join friend's lobby — follow host map**. Guests do not select a map. The host's map loads automatically and guests follow subsequent changes.
5. Close F8 to drive. The mod requests the game's ResumeDrive action after a map load; the native **Drive / Enter** shortcut remains available if needed.

The current default is Valve's **Spacewar development App ID 480**. Steam may show Spacewar as running. This is a development transport test, not an RC Tandems Steam release or production App ID. An authorized App ID can be configured in `RCDriftSim/Binaries/Win64/RoweRC/settings.json`; both players must use the same ID.

## Install

Close the game. In the extracted distribution, run `./Install.ps1` with PowerShell 7; it locates the Epic installation and checks for file conflicts before copying anything. Use `-GameFolder 'C:\Your\RCTandems'` if needed. For a clean unmodded installation, you can instead copy the package's `InstallFiles/RCDriftSim` folder into the game root beside `RCDriftSim.exe`, merging directories. Keep the full `ue4ss` and `RoweRC` folders together with `dwmapi.dll` in `Binaries/Win64`.

No Steam friend invitations are sent automatically. Share the lobby code yourself.

Each player drives with the game's local physics. Remote cars are collision-free visual ghosts. Their appearance currently mirrors your local car; wheel steering/spin, remote cosmetics, audio, chat, voice, synchronized AI, physical contact, host migration, and automatic map downloads are not implemented. Native map travel can initialize the game's default car; cosmetic/tune persistence across raw level travel still needs validation.

## Automatic updates (0.1.1 onward)

Both players must install **0.1.1 once** to replace the original 0.1.0 prototype. The wire protocol changed, so mixed versions are refused with an explicit message.

The helper checks a signed GitHub release feed at startup. A newer compatible mod is downloaded and SHA-256 checked, then installed after both RC Tandems and the helper have closed. Update signatures use a pinned RSA public key. The updater backs up replaced mod files, restores them after an interrupted installation, and never updates the base game, tuning, settings, maps or other mods.

F8 shows update status and includes **Check for mod update**. Set `AutoUpdate` to `false` in `RoweRC/settings.json` to disable startup checks. Update results and backups are under `%LOCALAPPDATA%/RoweRC/updates`. The signed feed is live at `https://github.com/xrowex/RoweRC/releases/latest/download/update.json`; the installed 0.1.1 helper successfully verified it and reported that the mod was up to date. Applying a future release through the full download-and-exit flow still needs a live release test.

## Connection diagnostics

Reliable handshakes now continue while a map is loading. The primary Steam Networking Messages route has a legacy Steam P2P fallback. F8 distinguishes a connecting peer, a peer loading the host map, an actual send error, and a car whose updates are arriving. Detailed counters, map paths and Steam connection errors are written to `%LOCALAPPDATA%/RoweRC/bridge/session.log`. Base 0.1.0 hid send failures and discarded packets during map mismatch; the user's real two-PC attempt exposed that gap. In the 0.1.1 retry, the guest received and visibly rendered the host's car through the primary Messages route. The legacy fallback has not yet been verified across two PCs.

## Custom maps

Put a compatible map ZIP in `%LOCALAPPDATA%/RoweRC/Maps/Inbox`, open F8, and click **Reload custom maps**. Successful imports are checked, installed under the game's `Content/Paks/~mods/RoweRC`, and added to the track list. Restart the game after importing new cooked content. Everyone must have the identical package to exchange car updates.

Maps must be cooked for **Unreal 5.7**, use `/Game/RoweRCMaps/<map_id_with_underscores>/...`, and include a `map.json` plus the listed `.pak`/`.utoc`/`.ucas` files at the archive root. Ordinary FBX, Unity maps, and arbitrary maps from other games are not compatible packages. See [CUSTOM_MAPS.md](CUSTOM_MAPS.md) for the authoring format and pack tool. No custom track is included or claimed tested.

## Build

Requires .NET SDK 9 and the pinned dependencies recorded in [THIRD_PARTY.md](THIRD_PARTY.md).

```powershell
dotnet publish src/RoweRC/RoweRC.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o dist/RoweRC
dotnet src/RoweRC/bin/Release/net9.0-windows/win-x64/RoweRC.dll --self-test
python tests/check_lua.py
```

`RoweRC.exe --desktop` opens the optional developer diagnostics window. Normal launches use the in-game menu. `--steam-smoke-test` creates and leaves a development lobby; it does not test a second player.

## Troubleshooting / removal

Read `%LOCALAPPDATA%/RoweRC/bridge/session.log`, `%LOCALAPPDATA%/RoweRC/bridge/error.txt` and `RCDriftSim/Binaries/Win64/ue4ss/UE4SS.log`. A helper startup failure is logged to `%LOCALAPPDATA%/RoweRC/startup-error.txt`.

To disable the mod, close the game and move the added `dwmapi.dll` out of `RCDriftSim/Binaries/Win64`. To remove this clean installation, also remove the added `ue4ss` and `RoweRC` directories, and any installed custom map folders under `~mods/RoweRC`. Preserve other mods if you merge this into an existing modded installation. Game executable and base content archives are not patched.

