namespace RoweRC;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--apply-update") { AutoUpdater.Apply(args[1]); return; }
        if (args.FirstOrDefault() == "--pack-map") { MapPackCommand.Run(args); return; }
        if (args.Contains("--self-test")) { SelfTest.Run(); return; }
        if (args.Contains("--contact-smoke-test")) { ContactSmoke.Run(); return; }
        if (args.Contains("--steam-smoke-test"))
        {
            SteamSmokeTest.Run(); return;
        }
        using var singleton = new Mutex(true, "Local\\RoweRC.Companion", out var first);
        if (!first) return;
        ApplicationConfiguration.Initialize();
        try { Application.Run(args.Contains("--desktop") ? new ApplicationContext(new MainForm()) : new BackendContext(!args.Contains("--background-only"))); }
        catch (Exception e) { var logRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RoweRC"); Directory.CreateDirectory(logRoot); File.WriteAllText(Path.Combine(logRoot, "startup-error.txt"), e.ToString()); MessageBox.Show(e.Message, "RoweRC"); }
    }
}
