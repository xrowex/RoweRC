# RoweRC 0.1.2 — experimental

F8 now includes a Steam lobby browser with public listings, friends-only room discovery, player counts, map names and one-click joining. Friends-only hosting remains available; public hosting is a separate explicit choice. The host still chooses the map and guests follow automatically.

Remote cars now receive the sender's stock body, visible chassis, rims, tires, body paint and rim paint through checked, acknowledged appearance snapshots. Each remote car owns independent material instances. Four wheel transforms and four hub transforms synchronize steering and wheel motion, and six native motor loops synchronize volume and pitch with gain based on car-to-car distance.

A 120 ms snapshot buffer replaces per-packet chasing. Body and wheel rotations use quaternion interpolation; prediction is limited to 100 ms, and car resets clear the buffer.

Both players need 0.1.2 for the new visual/audio features. From 0.1.1, use F8 → Check for mod update, wait for the download, then close and reopen RC Tandems. Future startup checks use the signed feed. Stock game build 3c46a39f90 and Steam development App ID 480 remain required.

Validation: zero-warning Release build, 70 backend checks, 11 smoothing checks, and five Lua syntax checks. A real public Steam lobby was created and found by the browser. Native local tests confirmed a remote body swap to Toyota Supra, independent red body paint, different rims, moving wheel transforms, active motor volume/pitch and cleanup across 80 components; a complete 29,880-byte native appearance snapshot validated and reassembled from 17 chunks. The original 0.1.1 updater also downloaded and verified the public 0.1.2 package, waited for game/helper exit, installed its eight files and wrote a successful receipt. The earlier 0.1.1 real two-PC session confirmed visible car updates through Steam Networking Messages.

The new appearance, wheel and motor-audio features still need a two-PC driving test. Friend-room discovery, sustained driving under varied latency, connected-guest map changes, custom cooked maps and cosmetics/tune persistence across map travel also remain unverified. Cars remain visual ghosts without physical contact. Custom asset/texture downloads, voice and chat are not included.
