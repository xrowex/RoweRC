# RoweRC 0.1.1 — experimental

The host chooses the lobby map. Guests join by code and load that map automatically; subsequent host map changes propagate through a single committed lobby-state value. Guests cannot override the map while connected.

Reliable peer handshakes now operate during map loading. A Steam P2P fallback retries stalled primary connections. Send failures are retained in status/logs, with peer readiness and packet counters in `%LOCALAPPDATA%/RoweRC/bridge/session.log`.

The in-game F8 menu checks a signed update feed. Future compatible releases download automatically and install after the game closes, with backups and rollback. Game files, settings and other mods are excluded from update payloads.

Both players must install 0.1.1 once; 0.1.0 lacks the updater and uses an incompatible wire protocol. Future updates use the signed feed. This remains an experimental mod using Steam development App ID 480. Remote cars mirror local appearance and do not have physical contact. Real two-PC driving still requires acceptance testing.

Validation: Release build without warnings, 49 standalone state/protocol/import/update checks, both Lua modules parse under Lua 5.4, real Steam lobby create/leave smoke check. The previous build's local native rendering test showed a separate car made from 80 static mesh components; that local test was not a real peer connection.
