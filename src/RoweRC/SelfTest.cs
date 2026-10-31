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
            var wheels = Enumerable.Range(0, 48).Select(i => i % 6 < 3 ? i * .125 : 170.0).ToArray();
            var audio = Enumerable.Range(0, 18).Select(i => i % 3 == 0 ? .5 : 1.0).ToArray();
            var motion = new CarMotion(100.25, wheels, audio);
            var animatedCar = hostCar with { Motion = motion };
            var animatedPacket = PeerEnvelope.Pose(advertised, animatedCar).Encode();
            Check(animatedPacket.Length < Protocol.MaxPacket && PeerEnvelope.Read(animatedPacket, advertised)?.Car?.Motion?.Wheels.SequenceEqual(wheels) == true,
                "wheel and audio payload fits packet budget and survives network round trip");
            var animatedLine = line.Replace("\t0\t123\n", "\t" + motion.Tsv + "\t123\n");
            Check(Protocol.ReadLocal(animatedLine)?.Motion?.Audio.SequenceEqual(audio) == true, "native wheel and motor layers survive local bridge parsing");
            Check(Protocol.ReadLocal(animatedLine.Replace("100.25", "NaN")) == null, "reject non-finite animation timestamp");
            Check(!new CarMotion(1, new double[47], audio).Valid, "reject missing wheel channels");
            var loud = audio.ToArray(); loud[0] = 5;
            Check(!new CarMotion(1, wheels, loud).Valid, "bound remote sound volume before playback");
            var invalidMotion = animatedCar with { Motion = new CarMotion(1, wheels, loud) };
            Check(PeerEnvelope.Read(PeerEnvelope.Pose(advertised, invalidMotion).Encode(), advertised) == null, "network rejects malformed animation instead of passing it to Unreal");
            bridge.WriteRemotes([new(42, "friend", animatedCar, 0)]);
            var remoteLine = File.ReadAllLines(Path.Combine(bridge.DirectoryPath, "remote.tsv"))[2].Split('\t');
            Check(remoteLine.Length == 11 && remoteLine[8] == hostCar.Sequence.ToString() && CarMotion.Read(remoteLine[9])?.Valid == true,
                "remote bridge preserves sequence and complete wheel/audio payload for smoothing");
            var appearanceText = "RRA1\trun-1\t1\np\t1\tBody\t/Game/Vehicle/Test/Body.Body\t0\t0,0,0,0,0,0,1,1,1,1,1,1\nm\t1\t0\t/Game/Vehicle/Test/Paint.Paint\nv\t1\t0\tBodyColor\t0.8,0.1,0.2,1\nend\t1\t1\n";
            var appearance = CarAppearance.Read(appearanceText);
            Check(appearance is { Parts: 1, Run: "run-1" }, "validate stock mesh and independent paint snapshot");
            Check(CarAppearance.Read(appearanceText.Replace("/Game/Vehicle/Test/Body.Body", "/Game/Maps/Other.Other")) == null,
                "appearance cannot load maps, remote files or unrelated assets");
            Check(CarAppearance.Read(appearanceText.Replace("0.8,0.1", "NaN,0.1")) == null, "reject invalid peer paint values");
            Check(CarAppearance.Read(appearanceText.Replace("end\t1\t1", "end\t1\t2")) == null, "reject torn local appearance snapshot");
            var receiver = new AppearanceReceiver();
            var appearanceChunks = appearance!.Chunks();
            var envelope = new PeerEnvelope(Protocol.Version, advertised.Room, advertised.Epoch, advertised.Content, "appearance", advertised.Level, true, null, appearanceChunks[0]);
            Check(envelope.Encode().Length < Protocol.MaxPacket && PeerEnvelope.Read(envelope.Encode(), advertised)?.Appearance?.Valid == true,
                "reliable appearance chunks fit transport packet limits");
            foreach (var chunk in appearanceChunks.Reverse()) receiver.Accept(chunk, 100);
            Check(receiver.Current?.Text == appearanceText, "appearance commits only after all chunks and checksum validate");
            var corruptReceiver = new AppearanceReceiver();
            var alteredChunk = appearanceChunks[0] with { Data = Convert.ToBase64String("tampered paint"u8.ToArray()) };
            Check(!corruptReceiver.Accept(alteredChunk, 100) && corruptReceiver.Current == null, "reject corrupted appearance before creating Unreal objects");
            Check(!receiver.Accept(appearanceChunks[0] with { Total = 99999 }, 100), "bound appearance assembly allocation");
            var newerAppearance = CarAppearance.Read(appearanceText.Replace("run-1\t1", "run-1\t2").Replace("end\t1\t1", "end\t1\t2").Replace("0.8,0.1", "0.2,0.1"))!;
            foreach (var chunk in newerAppearance.Chunks()) receiver.Accept(chunk, 200);
            foreach (var chunk in appearanceChunks) receiver.Accept(chunk, 300);
            Check(receiver.Current?.Revision == newerAppearance.Revision, "late appearance packets cannot rewind a newer paint choice");
            var multiPart = appearanceText;
            var bodyLines = appearanceText.Split('\n').Where(l => l.StartsWith("p\t") || l.StartsWith("m\t") || l.StartsWith("v\t")).ToArray();
            var builder = new System.Text.StringBuilder("RRA1\trun-1\t1\n");
            for (var i = 1; i <= 30; i++) foreach (var l in bodyLines) builder.Append(l.Replace("\t1\t", "\t" + i + "\t").Replace("\tBody\t", "\tBody" + i + "\t")).Append('\n');
            builder.Append("end\t30\t1\n");
            var largeAppearance = CarAppearance.Read(builder.ToString())!;
            var assembly = new AppearanceReceiver(); var largeChunks = largeAppearance.Chunks();
            foreach (var chunk in largeChunks.Skip(1).Reverse()) assembly.Accept(chunk, 400);
            Check(largeChunks.Length > 1 && assembly.Current == null, "incomplete appearance never replaces visible parts");
            assembly.Accept(largeChunks[0], 500);
            Check(assembly.Current?.Parts == 30, "out-of-order reliable chunks assemble a complete multi-part car");
            var ack = PeerEnvelope.Hello(advertised, hostCar) with { Ack = appearance.Revision };
            Check(PeerEnvelope.Read(ack.Encode(), advertised)?.Ack == appearance.Revision, "peer acknowledges appearance revision for reliable late joins");
            bridge.WriteRemotes([new(42, "friend", car!, 0, appearance)]);
            Check(File.ReadAllText(Path.Combine(bridge.DirectoryPath, "appearance.42.tsv")) == appearanceText
                && File.ReadAllText(Path.Combine(bridge.DirectoryPath, "remote.tsv")).Contains(appearance.Revision), "bridge associates appearance with the correct peer run");
            bridge.WriteRemotes([new(42, "friend", car! with { Run = "new-run" }, 0, appearance)]);
            Check(!File.ReadAllText(Path.Combine(bridge.DirectoryPath, "remote.tsv")).Contains(appearance.Revision), "stale appearance cannot attach to a different game run");
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
            var timing = new PeerLink(43, 0);
            timing.Accept(PeerEnvelope.Pose(advertised, animatedCar), PeerRoute.Messages, 1000);
            timing.Accept(PeerEnvelope.Pose(advertised, animatedCar with { Sequence = animatedCar.Sequence + 1, Motion = motion with { Time = motion.Time + .05 } }), PeerRoute.Messages, 1050);
            Check(timing.ArrivalJitterMs < .001 && Math.Abs(timing.SourceHz - 20) < .001, "constant transit delay does not create arrival jitter");
            timing.Accept(PeerEnvelope.Pose(advertised, animatedCar with { Sequence = animatedCar.Sequence + 3, Motion = motion with { Time = motion.Time + .15 } }), PeerRoute.Messages, 1180);
            Check(Math.Abs(timing.ArrivalJitterMs - 3) < .001 && timing.PoseGaps == 1, "delivery variation and skipped pose sequences are measured independently");
            const string shell = "-.22:-.10|.22:-.10|.22:.10|-.22:.10";
            var geometry=ContactGeometry.Parse(shell)!;
            Check(ContactGeometry.Parse("0:0|1:1|2:2") == null && ContactGeometry.Parse("0:0|.2:0|.1:.05|.2:.2|0:.2") == null,
                "colliders reject degenerate and concave network polygons");
            var contact=ContactGeometry.Solve(new(0,0),0,new(1,0),0,1.5,.03,geometry,new(.43,0),0,new(0,0),0,1.5,.03,geometry);
            Check(contact is { } centered && Math.Abs(centered.A.X+centered.B.X)<1e-8 && Math.Abs(centered.Wa)+Math.Abs(centered.Wb)<1e-8
                && centered.A.X<0 && centered.B.X>0, "centered bumper contact conserves momentum without inventing spin");
            Check(ContactGeometry.Solve(new(0,0),0,new(-1,0),0,1.5,.03,geometry,new(.43,0),0,new(1,0),0,1.5,.03,geometry)==null,
                "separating cars receive no second collision impulse");
            var glancing=ContactGeometry.Solve(new(0,0),0,new(1,.3),0,1.5,.03,geometry,new(.43,.12),0,new(0,0),0,1.5,.03,geometry);
            Check(glancing is { } offsetHit && Math.Abs(offsetHit.Wa)>.01 && Math.Abs(offsetHit.Wb)>.01
                && (new ContactVector(1,.3)+offsetHit.A).Length<Math.Sqrt(1.09), "off-center contact produces yaw and friction reduces sliding speed");
            Check(ContactGeometry.Solve(new(0,0),0,new(1,0),0,1.5,.03,geometry,new(2,0),0,new(0,0),0,1.5,.03,geometry)==null,
                "separated silhouettes do not collide");
            var physics=new CarPhysics(100.25,1,1,0,0,1.5,.03,0,true,shell);
            Check(CarPhysics.Read(physics.Tsv)==physics && CarPhysics.Read(physics.Tsv.Replace(",1.5,",",NaN,"))==null,
                "native dynamics frames round-trip and reject non-finite mass");
            var contactCar=animatedCar with { Physics=physics };
            var compactPacket=PeerEnvelope.Pose(advertised,contactCar).Encode();
            Check(!System.Text.Encoding.UTF8.GetString(compactPacket).Contains("\"Tsv\"")
                && PeerEnvelope.Read(compactPacket,advertised)?.Car?.Physics==physics,
                "wire packets contain dynamics once without duplicated bridge/debug properties");
            Check(Protocol.ReadLocal(line.Replace("\t0\t123\n","\t"+motion.Tsv+"\t"+physics.Tsv+"\t123\n"))?.Physics==physics,
                "bridge accepts optional dynamics while preserving old frames");
            var contactState=advertised with { Collisions=true };
            var hitPacket=new PeerEnvelope(Protocol.Version,advertised.Room,advertised.Epoch,advertised.Content,"contact",advertised.Level,true,null,
                Contact:new(1,contactCar.Run,1,100.25,0,0,-.5,0,0));
            Check(PeerEnvelope.Read(hitPacket.Encode(),contactState)?.Contact != null && PeerEnvelope.Read(hitPacket.Encode(),advertised)==null,
                "contact packets require the host's active contact epoch");
            Check(PeerEnvelope.Read((hitPacket with { Contact=hitPacket.Contact! with { Dvx=100 } }).Encode(),contactState)==null,
                "network contact impulses are bounded before reaching the game");
            var world=new ContactWorld();
            var bodyA=contactCar with { Pose=new(0,0,0,0,0,0) };
            var bodyB=contactCar with { Run="peer-run",Pose=new(43,0,0,0,0,0),Physics=physics with { Vx=0 } };
            var firstHits=world.Step([new(1,bodyA,0),new(2,bodyB,0)],1000).ToArray();
            Check(firstHits.Length==2 && firstHits[0].Hit.Sequence==firstHits[1].Hit.Sequence
                && !world.Step([new(1,bodyA,0),new(2,bodyB,0)],1008).Any(), "one host event updates both owners and suppresses repeat impulses before acknowledgement");
            Check(!world.Step([new(1,bodyA,.3),new(2,bodyB,.3)],1200).Any(), "stale physical states cannot create contacts");
            var sweptWorld=new ContactWorld();
            var fastCar=bodyA with { Pose=bodyA.Pose with { X=-49 },Physics=physics with { Vx=24.5 } };
            var stationary=bodyB with { Pose=bodyB.Pose with { X=0 } };
            sweptWorld.Step([new(1,fastCar,0),new(2,stationary,0)],1000).ToArray();
            Check(sweptWorld.Step([new(1,fastCar with { Sequence=fastCar.Sequence+1,Pose=fastCar.Pose with { X=49 } },0),new(2,stationary,0)],1040).Count()==2,
                "swept samples catch a car crossing another between host ticks");
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
