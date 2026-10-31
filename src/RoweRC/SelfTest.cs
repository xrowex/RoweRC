using System.IO.Compression;
using System.Text.Json;

namespace RoweRC;

internal static class SelfTest
{
    public static void Run()
    {
        var log = new List<string>();
        void Check(bool condition, string name) { if (!condition) throw new Exception("FAIL: " + name); log.Add("PASS: " + name); }
        void Reject(Action action, string name)
        {
            try { action(); } catch (InvalidDataException) { log.Add("PASS: " + name); return; }
            throw new Exception("FAIL: " + name);
        }
        var temp = Path.Combine(Path.GetTempPath(), "RoweRC-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            var line = "RRC1\trun-1\t123\t/Game/Test/Map\t1.5\t-2\t3\t0\t170\t-5\t0\t123\n";
            var car = Protocol.ReadLocal(line);
            Check(car?.Pose.X == 1.5 && car.Sequence == 123, "read complete local bridge frame");
            Check(Protocol.ReadLocal(line.Replace("\t123\n", "\t122\n")) == null, "reject torn frame");
            Check(Protocol.ReadLocal(line.Replace("1.5", "NaN")) == null, "reject NaN coordinates");
            Check(Protocol.ReadLocal(line.Replace("/Game/Test/Map", "../../command")) == null, "reject invalid map path");
            Check(!Protocol.ValidLevel("/Game/Test//Map") && !Protocol.ValidLevel("/Game/Test?listen"), "reject map options and malformed paths");
            var packet = Protocol.Encode(new(Protocol.Version, "room-a", "hash-a", car!));
            Check(Protocol.Decode(packet, "room-a", "hash-a", "/Game/Test/Map")?.Car == car, "wire frame round trip");
            Check(Protocol.Decode(packet, "room-b", "hash-a", "/Game/Test/Map") == null, "reject cross-lobby packet");
            Check(Protocol.Decode(packet, "room-a", "hash-b", "/Game/Test/Map") == null, "reject different map content");
            Check(Protocol.Decode(packet, "room-a", "hash-a", "/Game/Other") == null, "reject different loaded level");
            Check(Protocol.Decode(new byte[Protocol.MaxPacket + 1], "", "", "") == null, "reject oversized packet");
            Check(Protocol.Decode("{\"Protocol\":1,\"Car\":null}"u8.ToArray(), "", "", "") == null, "reject malformed/null JSON frame");
            var bridge = new GameBridge(Path.Combine(temp, "bridge"));
            File.WriteAllText(Path.Combine(bridge.DirectoryPath, "local.0.tsv"), line);
            Check(bridge.Read() == car, "bridge reads fresh frame");
            File.SetLastWriteTimeUtc(Path.Combine(bridge.DirectoryPath, "local.0.tsv"), DateTime.UtcNow.AddSeconds(-5));
            Check(bridge.Read() == null, "bridge rejects stale game process");
            bridge.WriteRemotes([new(42, "friend", car!, 0)]);
            Check(File.ReadAllText(Path.Combine(bridge.DirectoryPath, "remote.tsv")).Contains("42\t1.5\t-2"), "remote bridge publishes pose and heartbeat");
            var commandPath = Path.Combine(bridge.DirectoryPath, "command.tsv");
            File.WriteAllText(commandPath, "old command");
            using (var heldByGame = new FileStream(commandPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                bridge.OpenMap("/Game/Test/Map");
                Check(bridge.Error != null, "bridge survives game holding a file without delete sharing");
            }
            bridge.Flush();
            Check(bridge.Error == null && File.ReadAllText(commandPath).Contains("\topen\t/Game/Test/Map"), "deferred map command retries after game releases file");
            var mapFolder = Path.Combine(temp, "fixture"); Directory.CreateDirectory(mapFolder);
            File.WriteAllText(Path.Combine(mapFolder, "Track_P.pak"), "synthetic test payload, not a game map");
            var manifest = new MapManifest(1, "test-map", "Test map", "Tests", "1.0", "5.7", "/Game/RoweRCMaps/test_map/Track",
                new() { ["Track_P.pak"] = MapCatalog.Hash(Path.Combine(mapFolder, "Track_P.pak")) });
            void Save(MapManifest m) => File.WriteAllText(Path.Combine(mapFolder, "map.json"), JsonSerializer.Serialize(m));
            Save(manifest);
            var entry = MapCatalog.Validate(mapFolder);
            Check(entry.Fingerprint.Length == 64, "map content fingerprint");
            Save(manifest with { Name = "Different display name" });
            Check(MapCatalog.Validate(mapFolder).Fingerprint == entry.Fingerprint, "display metadata does not change map compatibility");
            Save(manifest with { Version = "2.0" });
            Check(MapCatalog.Validate(mapFolder).Fingerprint != entry.Fingerprint, "version changes map compatibility");
            Save(manifest with { Files = new() { ["../Track_P.pak"] = manifest.Files.First().Value } });
            Reject(() => MapCatalog.Validate(mapFolder), "reject manifest traversal");
            Save(manifest with { Level = "/Game/Maps/StockReplacement" });
            Reject(() => MapCatalog.Validate(mapFolder), "require isolated custom map namespace");
            Save(manifest with { Engine = "5.8" });
            Reject(() => MapCatalog.Validate(mapFolder), "reject incompatible cooking engine");
            Save(manifest);
            var zip = Path.Combine(temp, "map.zip"); ZipFile.CreateFromDirectory(mapFolder, zip);
            var imported = MapCatalog.ImportZip(zip, Path.Combine(temp, "maps"));
            Check(imported.Fingerprint == entry.Fingerprint, "import valid package transactionally");
            Reject(() => MapCatalog.ImportZip(zip, Path.Combine(temp, "maps")), "do not overwrite installed map");
            var evil = Path.Combine(temp, "traversal.zip");
            using (var z = ZipFile.Open(evil, ZipArchiveMode.Create)) { using var w = new StreamWriter(z.CreateEntry("../outside.pak").Open()); w.Write("bad"); }
            Reject(() => MapCatalog.ImportZip(evil, Path.Combine(temp, "maps")), "reject ZIP traversal");
            File.AppendAllText(Path.Combine(mapFolder, "Track_P.pak"), "tamper");
            Reject(() => MapCatalog.Validate(mapFolder), "reject modified map payload");
            var catalog = MapCatalog.Load(Path.Combine(temp, "builtin"), out _);
            var hostMap = catalog[2];
            var advertised = LobbyState.Create(hostMap, new string('a', 32), 1);
            Check(LobbyState.Read(advertised.ToJson()) == advertised, "host map advertisement commits as one metadata value");
            Check(advertised.SameMap(hostMap) && !advertised.SameMap(catalog[0]), "guest uses advertised host map instead of local selection");
            var hostCar = car! with { Level = hostMap.Manifest.Level };
            var posePacket = PeerEnvelope.Pose(advertised, hostCar).Encode();
            Check(PeerEnvelope.Read(posePacket, advertised)?.Car == hostCar, "peer pose packet round trip");
            Check(PeerEnvelope.Read(PeerEnvelope.Hello(advertised, null).Encode(), advertised) is { Kind: "hello", Ready: false }, "peer handshake works before game data exists");
            Check(PeerEnvelope.Read(PeerEnvelope.Hello(advertised, car).Encode(), advertised) is { Kind: "hello", Ready: false }, "peer handshake works while a different map is loading");
            var changedMap = LobbyState.Create(catalog[1], advertised.Room, 2);
            Check(PeerEnvelope.Read(posePacket, changedMap) == null, "old map packets are rejected after host changes maps");
            var returnToMap = LobbyState.Create(hostMap, advertised.Room, 3);
            Check(PeerEnvelope.Read(posePacket, returnToMap) == null, "stale packets are rejected when host returns to the same map");
            Check(LobbyState.Read(advertised.ToJson().Replace("\"Protocol\":2", "\"Protocol\":1")) == null, "old incompatible mod reports a version mismatch");
            var link = new PeerLink(42, 0);
            Check(link.TryLegacy(3000), "Steam fallback starts when primary handshake has not arrived");
            link.Accept(PeerEnvelope.Hello(advertised, hostCar), PeerRoute.Legacy, 3100);
            Check(link.Route(3200) == PeerRoute.Legacy && !link.HasCar(3200), "fallback handshake does not invent a remote car");
            link.Accept(PeerEnvelope.Pose(advertised, hostCar), PeerRoute.Legacy, 3300);
            link.Accept(PeerEnvelope.Pose(advertised, hostCar with { Sequence = hostCar.Sequence - 1 }), PeerRoute.Legacy, 3400);
            Check(link.HasCar(3500) && link.Car == hostCar && link.PosesReceived == 1, "duplicate or out-of-order poses cannot rewind remote cars");
            Check(!link.HasCar(7000), "silent peers lose their stale visual car");
            link.Accept(PeerEnvelope.Hello(advertised, hostCar), PeerRoute.Messages, 7100);
            Check(link.Route(7200) == PeerRoute.Messages, "working primary connection replaces fallback");
            var signedFeed = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "signed-update-fixture.json"));
            Check(AutoUpdater.Verify(signedFeed).Version == "9.9.9", "update signature is verified with the pinned public key");
            var signed = JsonSerializer.Deserialize<SignedUpdate>(signedFeed)!;
            var corrupt = JsonSerializer.Serialize(signed with { Payload = Convert.ToBase64String("tampered update"u8.ToArray()) });
            Reject(() => AutoUpdater.Verify(corrupt), "reject modified update feed before interpreting download details");
            Check(AutoUpdater.AllowedPath("RCDriftSim/Binaries/Win64/RoweRC/RoweRC.exe")
                && AutoUpdater.AllowedPath("RCDriftSim/Binaries/Win64/ue4ss/Mods/RoweRC/Scripts/menu.lua"), "updater permits only owned mod payloads");
            Check(!AutoUpdater.AllowedPath("RCDriftSim/Binaries/Win64/RCDriftSim-Win64-Shipping.exe")
                && !AutoUpdater.AllowedPath("RCDriftSim/Binaries/Win64/RoweRC/settings.json")
                && !AutoUpdater.AllowedPath("RCDriftSim/Binaries/Win64/ue4ss/Mods/Other/Scripts/main.lua")
                && !AutoUpdater.AllowedPath("RCDriftSim/Binaries/Win64/ue4ss/Mods/RoweRC/Scripts/../../elsewhere.lua"), "updater cannot replace the game, settings, other mods or traversal paths");
            var updateGame = Path.Combine(temp, "update-game");
            const string scriptRelative = "RCDriftSim/Binaries/Win64/ue4ss/Mods/RoweRC/Scripts/main.lua";
            const string menuRelative = "RCDriftSim/Binaries/Win64/ue4ss/Mods/RoweRC/Scripts/menu.lua";
            var scriptPath = Path.Combine(updateGame, scriptRelative);
            var menuPath = Path.Combine(updateGame, menuRelative);
            Directory.CreateDirectory(Path.GetDirectoryName(scriptPath)!);
            File.WriteAllText(scriptPath, "old script"); File.WriteAllText(menuPath, "old menu");
            var settingsPath = Path.Combine(updateGame, "RCDriftSim/Binaries/Win64/RoweRC/settings.json");
            Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!); File.WriteAllText(settingsPath, "user settings");
            var updateZip = Path.Combine(temp, "update.zip");
            using (var z = ZipFile.Open(updateZip, ZipArchiveMode.Create))
            {
                using (var w = new StreamWriter(z.CreateEntry(scriptRelative).Open())) w.Write("new script");
                using (var w = new StreamWriter(z.CreateEntry(menuRelative).Open())) w.Write("new menu");
            }
            var changed = new Dictionary<string, string?>();
            AutoUpdater.InstallPayload(updateZip, updateGame, Path.Combine(temp, "update-backup"), changed);
            Check(File.ReadAllText(scriptPath) == "new script" && File.ReadAllText(menuPath) == "new menu"
                && File.ReadAllText(settingsPath) == "user settings", "update installs both mod files while preserving settings");
            Check(AutoUpdater.Rollback(changed).Count == 0 && File.ReadAllText(scriptPath) == "old script"
                && File.ReadAllText(menuPath) == "old menu", "update rollback restores the previous mod files");
            changed.Clear(); var guards = 0; var interrupted = false;
            try { AutoUpdater.InstallPayload(updateZip, updateGame, Path.Combine(temp, "interrupted-backup"), changed, () => ++guards == 2); }
            catch (IOException) { interrupted = true; }
            Check(interrupted && changed.Count == 1, "update stops when the game restarts during installation");
            Check(AutoUpdater.Rollback(changed).Count == 0 && File.ReadAllText(scriptPath) == "old script"
                && File.ReadAllText(menuPath) == "old menu", "interrupted update rolls back the partially written payload");
            var unsafeUpdate = Path.Combine(temp, "unsafe-update.zip");
            using (var z = ZipFile.Open(unsafeUpdate, ZipArchiveMode.Create))
            {
                using (var w = new StreamWriter(z.CreateEntry(scriptRelative).Open())) w.Write("should not install");
                using (var w = new StreamWriter(z.CreateEntry("RCDriftSim/Binaries/Win64/RCDriftSim-Win64-Shipping.exe").Open())) w.Write("forbidden");
            }
            Reject(() => AutoUpdater.InstallPayload(unsafeUpdate, updateGame, Path.Combine(temp, "unsafe-backup"), []), "validate the entire update before changing any installed file");
            Check(File.ReadAllText(scriptPath) == "old script", "rejected update leaves existing scripts intact");
            log.Add($"{log.Count} checks passed. Game integration and two-PC networking are NOT covered.");
            File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "self-test.txt"), log);
        }
        catch (Exception e)
        {
            log.Add(e.ToString()); File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "self-test.txt"), log);
            Environment.ExitCode = 1;
        }
        finally
        {
            var resolved = Path.GetFullPath(temp);
            if (Path.GetDirectoryName(resolved) == Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar)
                && Path.GetFileName(resolved).StartsWith("RoweRC-test-", StringComparison.Ordinal)) Directory.Delete(resolved, true);
        }
    }
}

internal static class SteamSmokeTest
{
    public static void Run()
    {
        var log = new List<string>();
        try
        {
            var catalog = MapCatalog.Load(Path.Combine(AppContext.BaseDirectory, "Maps"), out _);
            var bridge = new GameBridge(Path.Combine(AppContext.BaseDirectory, "smoke-test-bridge"));
            using var session = new SteamSession(bridge, f => catalog.FirstOrDefault(m => m.Fingerprint == f));
            session.Changed += () => log.Add(session.Status);
            session.Connect(480);
            session.Host(catalog[0]);
            var until = Environment.TickCount64 + 20000;
            while (!session.InLobby && session.Busy && Environment.TickCount64 < until) { session.Tick(); Thread.Sleep(33); }
            if (!session.InLobby) throw new Exception("Steam lobby was not created within 20 seconds.");
            log.Add("PASS: Steam initialized and a friends-only development lobby was created.");
            log.Add("NOT TESTED: second-account joining, relay traffic, game visuals, custom-map gameplay.");
            session.Leave();
        }
        catch (Exception e) { log.Add(e.ToString()); Environment.ExitCode = 1; }
        File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "steam-smoke-test.txt"), log);
    }
}
