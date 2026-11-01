# Custom map packages

The loader accepts user-supplied cooked Unreal maps. Map creation and cooking use your own Unreal Editor; this project does not reconstruct the game's source or redistribute its assets.

## Archive layout

```text
garage-loop.zip
  map.json
  RoweRC_garage_loop_P.pak
  RoweRC_garage_loop_P.utoc   (if using IoStore)
  RoweRC_garage_loop_P.ucas   (if using IoStore)
```

`map.json` example:

```json
{
  "Format": 1,
  "Id": "garage-loop",
  "Name": "Garage Loop",
  "Author": "Your name",
  "Version": "1.0.0",
  "Engine": "5.7",
  "Level": "/Game/RoweRCMaps/garage_loop/Track",
  "Files": {
    "RoweRC_garage_loop_P.pak": "the actual 64-character lowercase SHA-256 of this file"
  }
}
```

The ID uses lowercase letters, digits and hyphens. Its Unreal directory replaces hyphens with underscores. Payload names may contain letters, digits, underscores and hyphens, followed by `.pak`, `.utoc` or `.ucas`. Paths and subfolders are rejected in the ZIP. Maximum unpacked payload is 4 GiB, with at most 32 payload files. Existing map IDs are not silently replaced.

## Author a track

1. Use **Unreal Engine 5.7**, matching the game's engine generation. The installed 5.8 Editor is not a validated cooker for this game.
2. Use a content project named `RCDriftSim` so cooked `/Game` packages mount at `../../../RCDriftSim/Content`. Keep your map and original assets under `Content/RoweRCMaps/garage_loop/`.
3. Build your drivable surface near the origin. Unreal uses centimeters; account for RC vehicle scale. Include collision, lighting and a usable starting area. Test the game's actual tire behavior on your surface.
4. Do not reference unavailable editor-only or game-private source classes. The loader supplies `/Script/RCDriftSim.RCDriftGameMode` when it opens a level. Native spawn/camera assumptions must still be checked on your map.
5. Cook for Windows with UE 5.7, including all required assets, materials, shaders and physical materials. Prefer an independently cooked DLC/container workflow. Merely adding uncooked `.umap` files to a ZIP is insufficient.
6. Give every map a distinct content namespace and container name. Do not include stock game configuration, executables, other mods, or replacements for the game's existing maps.

The loader validates manifest consistency and file hashes. It cannot prove from a hash that a cooked map has correct mount paths, collision, lighting, shaders, spawn behavior or engine compatibility. Those require an in-game acceptance test.

### Native vehicle limits in the verified game build

In RC Tandems 2.2.5 / build `1801ffbdfe`, the stock pawn converts its planar simulation coordinates to a world position with Z fixed at zero. Loading an elevated road with Unreal mesh collision does not make this pawn follow that road. The native recovery check also pauses driving when either simulation coordinate exceeds 18 metres from the origin, displaying `Arena boundary reached. Reset to recover.` This is a coordinate limit, rather than distance measured from the selected PlayerStart.

RoweRC 0.1.8 adds a grounded adapter for `/Game/RoweRCMaps/apex_wangan/Tandyz_Overcast_With_APEX_Repaired`. It preserves the city's geometry, follows road height and banking, offsets the absolute camera by road height, resolves contacts against baked city geometry, and replaces the fixed arena limit for that pawn. Native attitude/lift recovery remains enabled. Stock maps retain their original pawn tick. This supplies grounded driving; airborne vehicle dynamics are not implemented. A missing support surface stops the car instead of snapping to a different road deck.

The adapter's terrain file is embedded in the mod helper and is extracted and SHA-256 checked automatically. Both players need the matching mod and map: the city fingerprint includes its vehicle support profile. Geometry or placement changes require rebaking `tools/bake_apex_terrain.py` from the source FBX, updating the validated terrain hash, and repeating the driving checks. This experimental test release has passed local native height, camera, barrier and four-axis boundary probes; a full lap and two-player checks remain unverified.

## Build a distribution ZIP

Put only the cooked payload containers into a folder, then run the supplied helper with the following arguments. It generates the manifest and SHA-256 values and writes the ZIP; it does not cook assets.

```powershell
$helper = 'C:\Program Files\Epic Games\RCTandemszQS5H\RCDriftSim\Binaries\Win64\RoweRC\RoweRC.exe'
Start-Process -FilePath $helper -Wait -ArgumentList @(
  '--pack-map', 'E:\MyTrack\CookedPayload', 'garage-loop',
  '"Garage Loop"', '"Your Name"', '1.0.0',
  '/Game/RoweRCMaps/garage_loop/Track', 'E:\MyTrack\garage-loop.zip'
)
```

For easier developer invocation, call `dotnet <path-to-RoweRC.dll> --pack-map ...` from a build output directory. The result log is `map-pack-result.txt` beside that executable. In a protected game installation, run the packer from a writable copy of the helper.

## Import and verify

Copy the ZIP into `%LOCALAPPDATA%/RoweRC/Maps/Inbox`, then click **Reload custom maps** in F8. Restart RC Tandems after successful installation. Select the map and use **Drive selected map**. Check the track surface, reset position, camera, tuning and a complete lap before hosting friends.

Test with two accounts after both have imported exactly that package. The lobby advertises a content fingerprint derived from the map ID, version, level path, and sorted payload hashes. A guest with different content is refused rather than being shown cars on incompatible geometry. There is no automatic peer-to-peer map download.
