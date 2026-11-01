# RoweRC 0.1.7 — collision only

Removes cosmetic dents following collision-time lag reports: contacts no longer copy shell geometry, construct procedural meshes, deform vertices or replicate dents. Both bundled shells use mesh outlines extracted offline, so enabling contact avoids reflecting 301,383 Supra vertices into Lua. The Pickup and Supra shells, Online/Car/Maps menu and rim-metallic slider remain included.

Both players should install 0.1.7. Existing 0.1.6 users can use the signed F8 updater, then close the game to install. Alternatively close the game, extract RoweRC-prototype-0.1.7.zip and run RoweRC/Install.ps1 using PowerShell 7. Upgrades from 0.1.5 require the full installer. Supported Epic RC Tandems: v2.2.5 / build 1801ffbdfe, UE 5.7.4.

Car-to-car contact remains experimental and starts off in each new lobby; the host enables it in Online. Native driving stays local. Two-PC impact feel and sustained smoothness require playtesting; the earlier access-violation reports are still under investigation. This hotfix removes the expensive deformation path, without claiming that every crash is resolved.

Validation: 99 backend checks and 11 Lua collision checks passed. Six native solver-to-owner contacts (three on each shell) were acknowledged in an empty test lobby without a crash. This is a local fixture, not two-PC acceptance.
