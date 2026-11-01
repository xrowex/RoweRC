# RoweRC 0.1.8 - APEX city test build

Preserves the imported 3D city. The city-only vehicle adapter follows road height and banking, moves the absolute driving camera by the same road height, and resolves swept contacts against baked city obstacles. The native 18-metre arena limit is replaced for this pawn while attitude and wheel-lift recovery remain enabled. Stock tracks retain their native tick. Missing support stops the car instead of snapping to another deck; airborne dynamics are not implemented.

Car-to-car contacts ignore cars on separate road decks. Both players need the matching mod update and city package; the city's compatibility fingerprint now includes its vehicle support profile. Terrain data ships with the update. Deformation remains off.

The signed F8 update supports the previous 0.1.6/0.1.7 updater: terrain support travels inside the helper and extracts automatically on startup. Download the separate APEX map ZIP from this release and import it through Maps. The full installer includes both shells and all mod dependencies.

This is an experimental test release. Validation includes 105 backend checks, 11 Lua collision checks, terrain/contact tests, live height/banking and camera checks, four-axis boundary probes, and a stock-track native-tick check. A full city lap and two-PC acceptance remain unverified. The source FBX references texture images that were not supplied, so some city surfaces use their imported material colors.

# RoweRC 0.1.7 - collision only

Removes cosmetic dents following collision-time lag reports: contacts no longer copy shell geometry, construct procedural meshes, deform vertices or replicate dents. Both bundled shells use mesh outlines extracted offline, so enabling contact avoids reflecting 301,383 Supra vertices into Lua. The Pickup and Supra shells, Online/Car/Maps menu and rim-metallic slider remain included.

Both players should install 0.1.7. Existing 0.1.6 users can use the signed F8 updater, then close the game to install. Alternatively close the game, extract RoweRC-prototype-0.1.7.zip and run RoweRC/Install.ps1 using PowerShell 7. Upgrades from 0.1.5 require the full installer. Supported Epic RC Tandems: v2.2.5 / build 1801ffbdfe, UE 5.7.4.

Car-to-car contact remains experimental and starts off in each new lobby; the host enables it in Online. Native driving stays local. Two-PC impact feel and sustained smoothness require playtesting; the earlier access-violation reports are still under investigation. This hotfix removes the expensive deformation path, without claiming that every crash is resolved.

Validation: 99 backend checks and 11 Lua collision checks passed. Six native solver-to-owner contacts (three on each shell) were acknowledged in an empty test lobby without a crash. This is a local fixture, not two-PC acceptance.
