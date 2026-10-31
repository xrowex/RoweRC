# RoweRC 0.1.1 — experimental

The host chooses the lobby map. Guests join by code and load that map automatically; subsequent host map changes propagate through a single committed lobby-state value. Guests cannot override the map while connected.

Reliable peer handshakes now operate during map loading. A Steam P2P fallback retries stalled primary connections. Send failures are retained in status/logs, with peer readiness and packet counters in `%LOCALAPPDATA%/RoweRC/bridge/session.log`.

The in-game F8 menu checks a signed update feed. Future compatible releases download automatically and install after the game closes, with backups and rollback. Game files, settings and other mods are excluded from update payloads.

Both players must install 0.1.1 once; 0.1.0 lacks the updater and uses an incompatible wire protocol. Future updates use the signed feed. This remains an experimental mod using Steam development App ID 480. Remote cars mirror local appearance and do not have physical contact.

Validation: Release build without warnings, 49 standalone state/protocol/import/update checks, both Lua modules parse under Lua 5.4, real Steam lobby create/leave smoke check. The previous build's local native rendering test showed a separate car made from 80 static mesh components; that local test was not a real peer connection.

Two-PC verification on October 31, 2026: the guest confirmed a visible remote car. Local logs show a connected Steam Networking Messages session, matching Tandyz map paths and 1,985 accepted car updates without a connection error. Both-player visibility, map changes with a guest connected, and sustained driving under varied latency remain to be verified. The installed helper also verified the live signed update feed; applying a future release through the full download-and-exit flow remains to be tested.
