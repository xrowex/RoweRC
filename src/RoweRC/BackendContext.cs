using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace RoweRC;

public sealed record Settings(string GameFolder = "", uint SteamAppId = 480, bool AutoUpdate = true, string UpdateFeed = AutoUpdater.DefaultFeed);

internal sealed class BackendContext : ApplicationContext
{
    private readonly string root = AppContext.BaseDirectory;
    private readonly Settings settings;
    private readonly GameBridge bridge;
    private readonly SteamSession steam;
    private readonly AutoUpdater updater;
    private readonly StaPulse timer;
    private List<MapEntry> maps = [];
    private string requestSeen = "", lastError = "";
    private long lastUi, lastProcessSeen = Environment.TickCount64, lastUpdateCheck = Environment.TickCount64;
    private readonly Dictionary<string, int> needsRestart = [];
    public BackendContext(bool launchGame)
    {
        var config = Path.Combine(root, "settings.json");
        settings = File.Exists(config) ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(config))!
            : new();
        if (settings == null || settings.SteamAppId == 0) throw new InvalidDataException("Invalid RoweRC settings.json.");
        if (string.IsNullOrWhiteSpace(settings.GameFolder))
        {
            var installedRoot = Path.GetFullPath(Path.Combine(root, "../../../.."));
            settings = settings with { GameFolder = File.Exists(Path.Combine(installedRoot, "RCDriftSim.exe")) ? installedRoot : @"C:\Program Files\Epic Games\RCTandemszQS5H" };
        }
        bridge = new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RoweRC", "bridge"));
        bridge.WriteRemotes([]);
        requestSeen = ReadRequest()?.FirstOrDefault() ?? "";
        steam = new(bridge, fingerprint => maps.FirstOrDefault(m => m.Fingerprint == fingerprint), level => maps.FirstOrDefault(m => m.Manifest.Level == level));
        steam.CanLoadMap = m => { VerifyMap(m); return true; };
        updater = new(settings.GameFolder, settings.UpdateFeed, new SessionLog(bridge.DirectoryPath));
        if (settings.AutoUpdate) _ = updater.CheckAsync();
        RefreshMaps();
        if (launchGame && Process.GetProcessesByName("RCDriftSim-Win64-Shipping").Length == 0)
        {
            VerifyGame();
            Process.Start(new ProcessStartInfo(Path.Combine(settings.GameFolder, "RCDriftSim.exe")) { WorkingDirectory = settings.GameFolder, UseShellExecute = true });
        }
        timer = new StaPulse(Tick);
        Application.ApplicationExit += (_, _) => { timer.Dispose(); steam.Dispose(); bridge.WriteRemotes([]); };
    }
    private string[]? ReadRequest()
    {
        try
        {
            var path = Path.Combine(bridge.DirectoryPath, "ui-request.tsv");
            if (!File.Exists(path) || new FileInfo(path).Length > 1024) return null;
            var p = File.ReadAllText(path).TrimEnd('\r', '\n').Split('\t');
            return p.Length == 4 && p[0] == p[3] && long.TryParse(p[0], out _) ? p : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }
    private void Tick()
    {
        try
        {
            bridge.Flush(); steam.Tick();
            if (settings.AutoUpdate && Environment.TickCount64 - lastUpdateCheck >= 10 * 60 * 1000)
            { lastUpdateCheck = Environment.TickCount64; _ = updater.CheckAsync(); }
            var request = ReadRequest();
            if (request != null && request[0] != requestSeen)
            {
                requestSeen = request[0]; lastError = "";
                try { Handle(request[1], request[2]); }
                catch (Exception e) { lastError = e.Message; }
            }
            if (Environment.TickCount64 - lastUi > 200)
            {
                PublishUi(); lastUi = Environment.TickCount64;
                if (Process.GetProcessesByName("RCDriftSim-Win64-Shipping").Length > 0) lastProcessSeen = Environment.TickCount64;
                else if (Environment.TickCount64 - lastProcessSeen > 15000) ExitThread();
            }
        }
        catch (Exception e) { lastError = e.Message; PublishUi(); }
    }
    private void Handle(string action, string argument)
    {
        MapEntry Selected() => maps.FirstOrDefault(m => m.Manifest.Id == argument) ?? throw new InvalidDataException("Map is missing. Reload the map catalog.");
        void Connect() { if (!steam.Connected) steam.Connect(settings.SteamAppId); }
        switch (action)
        {
            case "shutdown": ExitThread(); break;
            case "refresh": RefreshMaps(); break;
            case "update": _ = updater.CheckAsync(); break;
            case "contact": VerifyGame(); steam.ToggleCollisions(); break;
            case "host": VerifyGame(); Connect(); steam.Host(Selected()); break;
            case "host-public": VerifyGame(); Connect(); steam.Host(Selected(), true); break;
            case "browse": Connect(); steam.Browser!.Refresh(settings.SteamAppId); break;
            case "join": VerifyGame(); Connect(); steam.Join(ulong.Parse(argument)); break;
            case "leave": steam.Leave(); break;
            case "copy": if (steam.Code.Length > 0) Clipboard.SetText(steam.Code); break;
            case "map":
                var m = Selected();
                if (steam.Hosting) steam.ChangeMap(m);
                else if (steam.Busy) throw new InvalidOperationException("The host selects the map. You follow automatically.");
                else { VerifyMap(m); bridge.OpenMap(m.Manifest.Level); }
                break;
        }
    }
    private void RefreshMaps()
    {
        var mapRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RoweRC", "Maps");
        var inbox = Path.Combine(mapRoot, "Inbox"); Directory.CreateDirectory(inbox);
        if (!steam.Busy)
            foreach (var zip in Directory.GetFiles(inbox, "*.zip"))
            {
                var done = zip + ".imported";
                if (File.Exists(done)) continue;
                try
                {
                    var entry = MapCatalog.ImportZip(zip, mapRoot);
                    var target = Path.Combine(settings.GameFolder, "RCDriftSim", "Content", "Paks", "~mods", "RoweRC", entry.Manifest.Id);
                    Directory.CreateDirectory(target);
                    foreach (var name in entry.Manifest.Files.Keys) File.Copy(Path.Combine(entry.Folder, name), Path.Combine(target, name), false);
                    var game = Process.GetProcessesByName("RCDriftSim-Win64-Shipping").FirstOrDefault();
                    if (game != null) needsRestart[entry.Fingerprint] = game.Id;
                    File.WriteAllText(done, entry.Fingerprint);
                    lastError = "Map imported. Restart RC Tandems to mount its new content.";
                }
                catch (Exception e) { lastError = Path.GetFileName(zip) + ": " + e.Message; }
            }
        maps = MapCatalog.Load(mapRoot, out var errors);
        if (errors.Count > 0) lastError = string.Join("; ", errors);
    }
    private void VerifyMap(MapEntry map)
    {
        VerifyGame();
        if (map.BuiltIn) return;
        if (needsRestart.TryGetValue(map.Fingerprint, out var pid) && Process.GetProcessesByName("RCDriftSim-Win64-Shipping").Any(p => p.Id == pid))
            throw new InvalidOperationException("Restart RC Tandems to mount this newly imported map.");
        if (MapCatalog.Validate(map.Folder).Fingerprint != map.Fingerprint) throw new InvalidDataException("Map content changed. Reload maps.");
        foreach (var (name, hash) in map.Manifest.Files)
        {
            var installed = Path.Combine(settings.GameFolder, "RCDriftSim", "Content", "Paks", "~mods", "RoweRC", map.Manifest.Id, name);
            if (!File.Exists(installed) || MapCatalog.Hash(installed) != hash.ToLowerInvariant()) throw new InvalidDataException("Map payload is not installed: " + name);
        }
    }
    private void VerifyGame()
    {
        foreach (var (relative, expected) in new Dictionary<string, string> {
            ["RCDriftSim/Binaries/Win64/RCDriftSim-Win64-Shipping.exe"] = MapCatalog.SupportedExe,
            ["RCDriftSim/Content/Paks/RCDriftSim-Windows.pak"] = "d8776af84340861259e2993416e6d5725562ef6a531fbe3041744e8bc4c8038b",
            ["RCDriftSim/Content/Paks/RCDriftSim-Windows.ucas"] = "90737f1f7ed6c3c9fde218135ea214a71bbad563d5a786aede630904cfa5bf8b",
            ["RCDriftSim/Content/Paks/RCDriftSim-Windows.utoc"] = "8ed6ab38ce80a7e3848ebe424e4fdf792f2d9a63ffadaf29eda31c8ca094fd60" })
        {
            var path = Path.Combine(settings.GameFolder, relative);
            if (!File.Exists(path) || MapCatalog.Hash(path) != expected) throw new InvalidDataException("Unsupported RC Tandems build: " + relative);
        }
    }
    private void PublishUi()
    {
        static string Clean(string s) => s.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
        var status = lastError.Length > 0 ? lastError : steam.Connected ? steam.Status : "Ready. Host or join to connect Steam.";
        var text = new StringBuilder($"time\t{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}\nstatus\t{Clean(status)}\ncode\t{steam.Code}\nactive\t{steam.ActiveMap?.Manifest.Id ?? ""}\nrole\t{steam.Role}\nversion\t{AutoUpdater.CurrentVersion}\n");
        text.Append("contact\t").Append(steam.Collisions ? "1" : "0").Append('\n');
        text.Append("contact-key\t").Append(steam.ContactKey).Append('\n');
        text.Append("physics-build\t").Append(steam.Collisions ? MapCatalog.SupportedBuild : "").Append('\n');
        text.Append("browser\t").Append(Clean(steam.Browser?.Status ?? "Refresh to find Steam lobbies")).Append('\n');
        foreach (var listing in steam.Browser?.Listings ?? [])
            text.Append("lobby\t").Append(listing.Id).Append('\t').Append(Clean($"{listing.Access} | {listing.Name} | {listing.Map} | {listing.Players}/{listing.Capacity}"))
                .Append('\t').Append(Clean(listing.Problem)).Append('\n');
        foreach (var peer in steam.Players.Take(3)) text.Append("peer\t").Append(Clean(peer)).Append('\n');
        text.Append("update\t").Append(Clean(updater.Status)).Append('\n');
        text.Append("update-ready\t").Append(updater.ReadyVersion).Append('\n');
        foreach (var m in maps) text.Append("map\t").Append(m.Manifest.Id).Append('\t').Append(Clean(m.Manifest.Name)).Append('\n');
        var path = Path.Combine(bridge.DirectoryPath, "ui-state.tsv");
        try { File.WriteAllText(path + ".tmp", text.ToString(), new UTF8Encoding(false)); File.Move(path + ".tmp", path, true); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
    protected override void ExitThreadCore() { timer.Dispose(); steam.Dispose(); bridge.WriteRemotes([]); base.ExitThreadCore(); }
}
