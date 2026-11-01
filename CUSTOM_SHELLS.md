# Custom shells — RoweRC 0.1.7

Open **F8 → Car** and choose **Use Pickup shell** or **Use Supra RC**. **Use game shell** restores the native shell and its materials. The choice is saved across restarts. The game's body-color picker still drives the custom shell's paint; rim metallic remains independent.

Both supplied FBXs were prepared in the existing Unreal 5.7.4 authoring project. Their original files are preserved in `exports/UE57/RCDriftSim/SourceAssets/Shells/`. The pickup has no original material assignments or textures and uses a basic paint finish. The Supra's paint, dark glass, trim, metal, red lenses and plastic are represented by six material groups. Neither source includes texture images.

The meshes are fitted to the RC chassis, use isolated `/Game/Vehicle/RoweRC/Shells/<id>/` asset paths. They use the current native body mounting transform; suspension, tires and the driving simulation stay on the game's car.

Each shell needs its matching **`.pak`, `.utoc` and `.ucas` files**, with identical stems. Keep all six files for this two-shell pack together under `RCDriftSim/Content/Paks/~mods/RoweRC/Shells/` and restart the game after installation. Do not install the cooker-generated `global.utoc` or `global.ucas`; the game retains its own global container.

Friends need the same shell pack and updated RoweRC scripts/helper installed. Shell files are not downloaded or transferred by the lobby. Missing assets leave the existing peer car visible and produce an appearance error. Two-PC custom-shell visuals require separate acceptance testing.

Rebuild tools:

1. Run `tools/prepare_shell_sources.py` with Blender in background mode to prepare the two source FBXs.
2. Run `tools/import_shells_ue57.py` in the existing UE 5.7 editor project.
3. Cook the physical `Content/Vehicle/RoweRC/Shells` directory for Windows, with shared material shader code disabled so materials carry their shader maps.
4. Run `python tools/package_shells.py` to generate the two IoStore containers and metadata paks. It uses the cook's package-store manifest and script-object metadata; only the shell namespace enters the distributable containers.

These shells are part of the experimental RoweRC 0.1.7 release. Cosmetic dents are removed; shells stay rigid. Two-PC custom-shell visuals and collision feel still need playtesting.
