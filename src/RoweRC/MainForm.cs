using System.Diagnostics;
using System.Text.Json;

namespace RoweRC;

public sealed class MainForm : Form
{
    private readonly string root = AppContext.BaseDirectory;
    private readonly GameBridge bridge;
    private readonly SteamSession session;
    private List<MapEntry> catalog = [];
    private readonly TextBox game = new() { Text = @"C:\Program Files\Epic Games\RCTandemszQS5H", Width = 540 };
    private readonly TextBox appId = new() { Width = 110, PlaceholderText = "Steam App ID" };
    private readonly CheckBox development = new() { Text = "Use Spacewar (480) for development testing", AutoSize = true };
    private readonly TextBox joinCode = new() { Width = 260, PlaceholderText = "Paste friend's lobby code" };
    private readonly TextBox ownCode = new() { Width = 260, ReadOnly = true };
    private readonly ComboBox maps = new() { Width = 650, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ListBox players = new() { Width = 650, Height = 100 };
    private readonly Label status = new() { Width = 680, Height = 45 };
    private readonly Label gameState = new() { Width = 680, Height = 45 };
    private readonly Label notice = new() { Width = 680, Height = 56, Text = "Experimental mod prototype. Remote cars are visual ghosts with no car-to-car collisions. Game integration and two-PC play still need validation." };
    private readonly Button host = new() { Text = "Host friends", AutoSize = true };
    private readonly Button join = new() { Text = "Join lobby", AutoSize = true };
    private readonly Button leave = new() { Text = "Leave lobby", AutoSize = true };
    private readonly Button load = new() { Text = "Load selected map", AutoSize = true };
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 33 };
    private long lastUi;
    private string? validatedGame;
    public MainForm()
    {
        bridge = new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RoweRC", "bridge"));
        bridge.WriteRemotes([]);
        session = new(bridge, fingerprint => catalog.FirstOrDefault(m => m.Fingerprint == fingerprint));
        session.CanLoadMap = map => { EnsureMapAvailable(map); return true; };
        Text = "RoweRC — RC Tandems friends & maps (prototype)";
        ClientSize = new(740, 760); MinimumSize = new(755, 790);
        Font = new("Segoe UI", 10); AutoScaleMode = AutoScaleMode.Dpi;
        var layout = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new(20) };
        Controls.Add(layout);
        void Label(string text) => layout.Controls.Add(new Label { Text = text, AutoSize = true, Margin = new(0, 10, 0, 4) });
        FlowLayoutPanel Row(params Control[] controls)
        {
            var p = new FlowLayoutPanel { Width = 690, Height = 38, WrapContents = false };
            p.Controls.AddRange(controls); layout.Controls.Add(p); return p;
        }
        Button ActionButton(string text, Action action)
        {
            var b = new Button { Text = text, AutoSize = true };
            b.Click += (_, _) => Guard(action); return b;
        }
        Label("RC Tandems game folder");
        Row(game, ActionButton("Browse…", () => { using var d = new FolderBrowserDialog { InitialDirectory = game.Text }; if (d.ShowDialog() == DialogResult.OK) { game.Text = d.SelectedPath; validatedGame = null; } }));
        Row(ActionButton("Verify game", () => { VerifyGame(); MessageBox.Show(this, "Supported game files verified."); }),
            ActionButton("Start game", StartGame), ActionButton("Mod files…", () => OpenFolder(Path.Combine(root, "InstallFiles"))),
            ActionButton("Diagnostics", () => { EnsureGameHook(); bridge.Probe(); OpenFolder(bridge.DirectoryPath); }));
        Label("Steam multiplayer");
        Row(appId, development);
        Row(ActionButton("Connect Steam", () => session.Connect(development.Checked ? 480u : uint.Parse(appId.Text))));
        Label("Track / custom map"); layout.Controls.Add(maps);
        Row(load, ActionButton("Import map ZIP…", ImportMap), ActionButton("Refresh maps", ReloadMaps),
            ActionButton("Map folder…", () => OpenFolder(Path.Combine(root, "Maps"))));
        Row(host, joinCode, join, leave);
        Row(new Label { Text = "Your lobby code:", Width = 120, TextAlign = ContentAlignment.MiddleLeft }, ownCode,
            ActionButton("Copy code", () => { if (ownCode.Text.Length > 0) Clipboard.SetText(ownCode.Text); }));
        layout.Controls.Add(status); layout.Controls.Add(gameState); layout.Controls.Add(players); layout.Controls.Add(notice);
        session.Changed += RefreshUi;
        host.Click += (_, _) => Guard(() => { if (maps.SelectedItem is MapEntry m) { EnsureGameHook(); session.Host(m); } });
        join.Click += (_, _) => Guard(() => { EnsureGameHook(); session.Join(ulong.Parse(joinCode.Text.Trim())); });
        leave.Click += (_, _) => session.Leave();
        load.Click += (_, _) => Guard(() => { if (maps.SelectedItem is MapEntry m) { EnsureGameHook(); EnsureMapAvailable(m); bridge.OpenMap(m.Manifest.Level); } });
        timer.Tick += (_, _) => {
            try { bridge.Flush(); session.Tick(); }
            catch (Exception e) { timer.Stop(); session.Leave(); status.Text = "Session stopped: " + e.Message; }
            if (Environment.TickCount64 - lastUi > 500) { RefreshUi(); lastUi = Environment.TickCount64; }
        };
        FormClosed += (_, _) => { timer.Stop(); session.Dispose(); timer.Dispose(); };
        ReloadMaps(); timer.Start();
    }
    private void Guard(Action action)
    {
        try { action(); RefreshUi(); }
        catch (Exception e) { MessageBox.Show(this, e.Message, "RoweRC", MessageBoxButtons.OK, MessageBoxIcon.Information); }
    }
    private void ReloadMaps()
    {
        catalog = MapCatalog.Load(Path.Combine(root, "Maps"), out var errors);
        maps.DataSource = catalog;
        if (errors.Count > 0) MessageBox.Show(this, string.Join('\n', errors), "Maps skipped");
    }
    private void ImportMap()
    {
        if (session.Busy) throw new InvalidOperationException("Leave the lobby before importing maps.");
        using var dialog = new OpenFileDialog { Filter = "RC Tandems map package (*.zip)|*.zip" };
        if (dialog.ShowDialog() != DialogResult.OK) return;
        var entry = MapCatalog.ImportZip(dialog.FileName, Path.Combine(root, "Maps"));
        // Stage only. The user installs the reviewed payload before restarting the game.
        var target = Path.Combine(root, "InstallFiles", "RCDriftSim", "Content", "Paks", "~mods", "RoweRC", entry.Manifest.Id);
        Directory.CreateDirectory(target);
        foreach (var name in entry.Manifest.Files.Keys) File.Copy(Path.Combine(entry.Folder, name), Path.Combine(target, name), false);
        ReloadMaps(); maps.SelectedItem = catalog.First(m => m.Fingerprint == entry.Fingerprint);
        OpenFolder(Path.Combine(root, "InstallFiles"));
        MessageBox.Show(this, "Map imported and staged. Close RC Tandems, copy InstallFiles/RCDriftSim into the game's RCDriftSim folder's parent, then restart the game. See INSTALL.txt for the exact folder layout.");
    }
    private void EnsureGameHook()
    {
        if (bridge.Read() == null) throw new InvalidOperationException("No live game mod detected. Install the staged UE4SS mod, start RC Tandems, enter a track, and check ue4ss/UE4SS.log if it does not connect.");
    }
    private void EnsureMapAvailable(MapEntry map)
    {
        VerifyGame();
        if (map.BuiltIn) return;
        var current = MapCatalog.Validate(map.Folder);
        if (current.Fingerprint != map.Fingerprint) throw new InvalidDataException("Map changed after catalog scan. Refresh maps.");
        foreach (var (name, hash) in map.Manifest.Files)
        {
            var mounted = Path.Combine(game.Text, "RCDriftSim", "Content", "Paks", "~mods", "RoweRC", map.Manifest.Id, name);
            if (!File.Exists(mounted) || MapCatalog.Hash(mounted) != hash.ToLowerInvariant()) throw new InvalidDataException("Install the staged map payload and restart the game first: " + mounted);
        }
    }
    private void VerifyGame()
    {
        var folder = Path.GetFullPath(game.Text);
        if (validatedGame == folder) return;
        UseWaitCursor = true;
        try
        {
            foreach (var (relative, expected) in new Dictionary<string, string> {
                ["RCDriftSim/Binaries/Win64/RCDriftSim-Win64-Shipping.exe"] = MapCatalog.SupportedExe,
                ["RCDriftSim/Content/Paks/RCDriftSim-Windows.pak"] = "d8776af84340861259e2993416e6d5725562ef6a531fbe3041744e8bc4c8038b",
                ["RCDriftSim/Content/Paks/RCDriftSim-Windows.ucas"] = "90737f1f7ed6c3c9fde218135ea214a71bbad563d5a786aede630904cfa5bf8b",
                ["RCDriftSim/Content/Paks/RCDriftSim-Windows.utoc"] = "8ed6ab38ce80a7e3848ebe424e4fdf792f2d9a63ffadaf29eda31c8ca094fd60" })
            {
                var path = Path.Combine(folder, relative);
                if (!File.Exists(path) || MapCatalog.Hash(path) != expected) throw new InvalidDataException("Unsupported or changed RC Tandems build: " + relative);
            }
            validatedGame = folder;
        }
        finally { UseWaitCursor = false; }
    }
    private void StartGame()
    {
        VerifyGame();
        if (Process.GetProcessesByName("RCDriftSim-Win64-Shipping").Length > 0) throw new InvalidOperationException("RC Tandems is already running.");
        Process.Start(new ProcessStartInfo(Path.Combine(game.Text, "RCDriftSim.exe")) { WorkingDirectory = game.Text, UseShellExecute = true });
    }
    private void RefreshUi()
    {
        status.Text = session.Status;
        ownCode.Text = session.Code;
        host.Enabled = join.Enabled = session.Connected && !session.Busy;
        leave.Enabled = session.Busy;
        maps.Enabled = load.Enabled = !session.Busy;
        appId.Enabled = development.Enabled = !session.Connected;
        var car = bridge.Read();
        gameState.Text = car == null ? "Game mod: no live car data. Install the staged mod and enter a track." : "Game mod: live car data · " + car.Level;
        if (bridge.Error != null) gameState.Text = "Game bridge: " + bridge.Error;
        var lines = session.Players.ToArray();
        if (!players.Items.Cast<string>().SequenceEqual(lines)) { players.Items.Clear(); players.Items.AddRange(lines); }
    }
    private static void OpenFolder(string path) { Directory.CreateDirectory(path); Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
}
