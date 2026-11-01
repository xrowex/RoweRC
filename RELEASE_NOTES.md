# RoweRC 0.1.6 — shells, cleaner menu and cosmetic dents

Both players can select the new **Pickup** and **Supra RC** shells from **F8 → Car**. The release includes all six cooked shell files, a saved rim-metallic slider, native-shell restoration, and a menu split into **Online / Car / Maps**. Shells retain the game's body color and the game retains local driving physics.

**Upgrade instructions:** update Epic RC Tandems to v2.2.5 / build `1801ffbdfe`, close the game, extract `RoweRC-prototype-0.1.6.zip`, and run `RoweRC/Install.ps1` in PowerShell 7. **Upgrading from 0.1.5 requires the full installer** because its updater only accepts the old game build and cannot install shell containers. The shell files are included in the archive; the new updater supports these files for future releases.

Cosmetic denting is included for body paint sections. Mod car-contact impulses can add dents; Reset Car repairs them. Dents do not alter wheel/chassis collision shapes. Wall impacts are not connected to deformation. Custom-shell dent messages now pass validation for reliable peer replication.

The shell-switching crash caused by retaining an Unreal asset wrapper after its mesh could unload is fixed. The corrected build passed 14 native shell changes and six forced garbage collections, with stock restoration working. Both shells cooked under UE 5.7.4 with zero errors and warnings and loaded in the installed game. Native deformation and reset checks, managed packet/updater checks and Lua lifecycle checks were also run.

Experimental contact uses the game's pinned native pair solver with actual mass/inertia, an upright body footprint and four tire shapes. Contact starts off and can be enabled by the host in Online. Native car contact, cosmetic dents and Steam names are included, but real two-PC impacts and custom-shell/dent visuals remain unverified. No newly cooked custom track is included.

Steam transport still uses development App ID 480. Both peers need this same release and the supported game build.
