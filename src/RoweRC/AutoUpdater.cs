using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RoweRC;

public sealed record UpdateRelease(string Version, string GameBuild, string Url, long Size, string Sha256, string Notes);
public sealed record SignedUpdate(string Payload, string Signature);
public sealed record PendingUpdate(string GameFolder, string Package, string Feed, int ParentPid);

public sealed class AutoUpdater
{
    public const string CurrentVersion = "0.1.8";
    public const string DefaultFeed = "https://github.com/xrowex/RoweRC/releases/latest/download/update.json";
    private readonly string gameFolder, feedUrl;
    private readonly string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RoweRC", "updates");
    private readonly SessionLog log;
    private int checking;
    public string ReadyVersion { get; private set; } = "";
    public string Status { get; private set; } = "Automatic updates enabled";
    public AutoUpdater(string gameFolder, string feedUrl, SessionLog log) { this.gameFolder = gameFolder; this.feedUrl = feedUrl; this.log = log; }
    public static UpdateRelease Verify(string signedJson)
    {
        if (signedJson.Length > 65536) throw new InvalidDataException("Update feed exceeds its size limit.");
        var signed = JsonSerializer.Deserialize<SignedUpdate>(signedJson) ?? throw new InvalidDataException("Invalid update feed.");
        var payload = Convert.FromBase64String(signed.Payload); var signature = Convert.FromBase64String(signed.Signature);
        using var key = RSA.Create(); key.ImportFromPem(UpdateTrust.PublicKey);
        if (!key.VerifyData(payload, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pss)) throw new InvalidDataException("Update signature is invalid.");
        var release = JsonSerializer.Deserialize<UpdateRelease>(payload) ?? throw new InvalidDataException("Update release is missing.");
        if (!Version.TryParse(release.Version, out _) || release.GameBuild is not (MapCatalog.SupportedBuild or "3c46a39f90")
            || release.Size is < 1 or > 200 * 1024 * 1024 || release.Sha256.Length != 64
            || !Uri.TryCreate(release.Url, UriKind.Absolute, out var uri) || uri.Scheme != "https") throw new InvalidDataException("Invalid/incompatible update release.");
        return release;
    }
    public async Task CheckAsync()
    {
        if (ReadyVersion.Length > 0 || Interlocked.Exchange(ref checking, 1) != 0) return;
        try
        {
            if (feedUrl.Length == 0) { Status = "Automatic update feed is not configured"; return; }
            if (!Uri.TryCreate(feedUrl, UriKind.Absolute, out var uri) || uri.Scheme != "https") throw new InvalidDataException("Update feed must use HTTPS.");
            Status = "Checking for mod updates…";
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("RoweRC/" + CurrentVersion);
            using var feedResponse = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
            if (feedResponse.StatusCode == HttpStatusCode.NotFound) { Status = "Update feed has not been published yet"; return; }
            feedResponse.EnsureSuccessStatusCode();
            if (feedResponse.Content.Headers.ContentLength > 65536) throw new InvalidDataException("Update feed is too large.");
            var feedBytes = await ReadLimited(await feedResponse.Content.ReadAsStreamAsync().ConfigureAwait(false), 65536).ConfigureAwait(false);
            var feed = Encoding.UTF8.GetString(feedBytes); var release = Verify(feed);
            if (Version.Parse(release.Version) <= Version.Parse(CurrentVersion)) { Status = "Mod is up to date (" + CurrentVersion + ")"; return; }
            Directory.CreateDirectory(root);
            var package = Path.Combine(root, "RoweRC-" + release.Version + ".zip");
            if (!File.Exists(package) || MapCatalog.Hash(package) != release.Sha256.ToLowerInvariant())
            {
                Status = "Downloading mod " + release.Version + "…";
                using var response = await http.GetAsync(release.Url, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                if (response.RequestMessage?.RequestUri?.Scheme != "https") throw new InvalidDataException("Update redirect must use HTTPS.");
                using var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                using (var output = File.Create(package + ".part"))
                {
                    var buffer = new byte[65536]; long total = 0; int n;
                    while ((n = await input.ReadAsync(buffer).ConfigureAwait(false)) > 0)
                    {
                        total += n; if (total > release.Size) throw new InvalidDataException("Update exceeded its declared size.");
                        await output.WriteAsync(buffer.AsMemory(0, n)).ConfigureAwait(false);
                    }
                    if (total != release.Size) throw new InvalidDataException("Update download is incomplete.");
                }
                if (MapCatalog.Hash(package + ".part") != release.Sha256.ToLowerInvariant()) throw new InvalidDataException("Update checksum does not match.");
                File.Move(package + ".part", package, true);
            }
            var feedPath = Path.Combine(root, "verified-feed.json"); File.WriteAllText(feedPath, feed);
            var pendingPath = Path.Combine(root, "pending.json");
            File.WriteAllText(pendingPath, JsonSerializer.Serialize(new PendingUpdate(gameFolder, package, feedPath, Environment.ProcessId)));
            var workerRoot = Path.Combine(root, "worker-" + CurrentVersion); Directory.CreateDirectory(workerRoot);
            var worker = Path.Combine(workerRoot, "RoweRC.exe");
            // The worker runs from a separate path so it can replace the installed helper.
            if (!File.Exists(worker)) File.Copy(Environment.ProcessPath!, worker);
            var start = new ProcessStartInfo(worker) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
            start.ArgumentList.Add("--apply-update"); start.ArgumentList.Add(pendingPath);
            if (Process.Start(start) == null) throw new IOException("Could not start the update installer.");
            ReadyVersion = release.Version;
            Status = "Mod " + release.Version + " downloaded; installs after you close RC Tandems";
            log.Write(Status);
        }
        catch (Exception e) { Status = "Update check: " + e.Message; log.Write(Status); }
        finally { Volatile.Write(ref checking, 0); }
    }
    private static async Task<byte[]> ReadLimited(Stream input, int limit)
    {
        using var output = new MemoryStream(); var buffer = new byte[8192]; int n;
        while ((n = await input.ReadAsync(buffer).ConfigureAwait(false)) > 0)
        { if (output.Length + n > limit) throw new InvalidDataException("Response exceeded size limit."); await output.WriteAsync(buffer.AsMemory(0, n)).ConfigureAwait(false); }
        return output.ToArray();
    }
    internal static readonly string[] ShellPayloadPaths =
        (from id in new[] { "optional_shell", "supra_rc" }
         from extension in new[] { "pak", "utoc", "ucas" }
         select $"RCDriftSim/Content/Paks/~mods/RoweRC/Shells/RoweRC_{id}_P.{extension}").ToArray();
    public static bool AllowedPath(string relative) => relative.StartsWith("RCDriftSim/Binaries/Win64/ue4ss/Mods/RoweRC/Scripts/", StringComparison.Ordinal)
        && relative.EndsWith(".lua", StringComparison.Ordinal) && !relative.Contains("..") && !relative.Contains('\\')
        || new[] { "RoweRC.exe", "bootstrap.dll", "steam_api64.dll" }.Any(name => relative == "RCDriftSim/Binaries/Win64/RoweRC/" + name)
        || relative == "RCDriftSim/Binaries/Win64/RoweRC/Terrain/apex-wangan.rrcterrain"
        || ShellPayloadPaths.Contains(relative, StringComparer.Ordinal);

    public static void Apply(string pendingPath)
    {
        using var workerMutex = new Mutex(true, "Local\\RoweRC.UpdateWorker", out var first);
        if (!first) return;
        var updateRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RoweRC", "updates");
        var log = new SessionLog(updateRoot);
        var backup = new Dictionary<string, string?>();
        try
        {
            if (!Path.GetFullPath(pendingPath).StartsWith(Path.GetFullPath(updateRoot) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Invalid pending update path.");
            var pending = JsonSerializer.Deserialize<PendingUpdate>(File.ReadAllText(pendingPath)) ?? throw new InvalidDataException("Missing pending update.");
            var release = Verify(File.ReadAllText(pending.Feed));
            if (MapCatalog.Hash(pending.Package) != release.Sha256.ToLowerInvariant() || new FileInfo(pending.Package).Length != release.Size) throw new InvalidDataException("Staged update changed after verification.");
            if (Version.Parse(release.Version) <= Version.Parse(CurrentVersion)) return;
            // Wait for both the game's DLLs and the installed helper executable to unload.
            while (IsGameRunning(pending.GameFolder) || ProcessStillRunning(pending.ParentPid)) Thread.Sleep(1000);
            var gameRoot = Path.GetFullPath(pending.GameFolder).TrimEnd(Path.DirectorySeparatorChar);
            var gameExe = Path.Combine(gameRoot, "RCDriftSim/Binaries/Win64/RCDriftSim-Win64-Shipping.exe");
            if (!File.Exists(gameExe) || MapCatalog.Hash(gameExe) != MapCatalog.SupportedExe) throw new InvalidDataException("Game build changed; automatic update was skipped.");
            var backupRoot = Path.Combine(updateRoot, "backup-" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            var targets = InstallPayload(pending.Package, gameRoot, backupRoot, backup, () => IsGameRunning(gameRoot));
            var receiptPath = Path.Combine(gameRoot, "RCDriftSim/Binaries/Win64/RoweRC/install-receipt.json");
            var receipt = File.Exists(receiptPath) ? JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(receiptPath)) : default;
            var files = new Dictionary<string, string>();
            if (receipt.ValueKind == JsonValueKind.Object && receipt.TryGetProperty("Files", out var oldFiles))
                foreach (var file in oldFiles.EnumerateObject()) files[file.Name] = file.Value.GetString()!;
            foreach (var item in targets) files[item.Relative.Replace('/', '\\')] = MapCatalog.Hash(item.Path);
            File.WriteAllText(receiptPath + ".tmp", JsonSerializer.Serialize(new { Version = release.Version, GameBuild = release.GameBuild, Files = files }));
            File.Move(receiptPath + ".tmp", receiptPath, true);
            File.WriteAllText(Path.Combine(updateRoot, "result.json"), JsonSerializer.Serialize(new { Version = release.Version, Success = true }));
            log.Write("Installed mod " + release.Version + "; backup=" + backupRoot);
        }
        catch (Exception e)
        {
            foreach (var error in Rollback(backup)) log.Write(error);
            File.WriteAllText(Path.Combine(updateRoot, "result.json"), JsonSerializer.Serialize(new { Success = false, Error = e.Message }));
            log.Write("Update failed; previous files restored: " + e);
        }
    }
    internal static List<(string Relative, string Path)> InstallPayload(string package, string gameRoot, string backupRoot,
        Dictionary<string, string?> changed, Func<bool>? gameRunning = null)
    {
        gameRoot = Path.GetFullPath(gameRoot).TrimEnd(Path.DirectorySeparatorChar);
        using var zip = ZipFile.OpenRead(package);
        if (zip.Entries.Count is < 1 or > 32 || zip.Entries.Sum(e => e.Length) > 250L * 1024 * 1024)
            throw new InvalidDataException("Update package exceeds limits.");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var targets = new List<(ZipArchiveEntry Entry, string Path, string? Backup)>();
        foreach (var entry in zip.Entries)
        {
            if (!AllowedPath(entry.FullName) || !seen.Add(entry.FullName)) throw new InvalidDataException("Update contains an unexpected path.");
            var target = Path.GetFullPath(Path.Combine(gameRoot, entry.FullName));
            if (!target.StartsWith(gameRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Update path escapes the game folder.");
            for (var parent = Path.GetDirectoryName(target); parent != null && parent.Length > gameRoot.Length; parent = Path.GetDirectoryName(parent))
                if (Directory.Exists(parent) && File.GetAttributes(parent).HasFlag(FileAttributes.ReparsePoint)) throw new InvalidDataException("Update path contains a redirected directory.");
            targets.Add((entry, target, File.Exists(target) ? Path.Combine(backupRoot, entry.FullName) : null));
        }
        if (seen.Any(p => ShellPayloadPaths.Contains(p, StringComparer.Ordinal)) && !ShellPayloadPaths.All(seen.Contains))
            throw new InvalidDataException("Shell update is incomplete: both shells need matching .pak, .utoc and .ucas files.");
        foreach (var item in targets)
            if (item.Backup != null) { Directory.CreateDirectory(Path.GetDirectoryName(item.Backup)!); File.Copy(item.Path, item.Backup); }
        foreach (var item in targets)
        {
            if (gameRunning?.Invoke() == true) throw new IOException("Game restarted before update completed.");
            Directory.CreateDirectory(Path.GetDirectoryName(item.Path)!);
            var temp = item.Path + ".rowerc-update";
            if (File.Exists(temp) && File.GetAttributes(temp).HasFlag(FileAttributes.ReparsePoint)) throw new InvalidDataException("Update staging file is redirected.");
            item.Entry.ExtractToFile(temp, true);
            File.Move(temp, item.Path, true);
            changed[item.Path] = item.Backup;
        }
        return targets.Select(t => (t.Entry.FullName, t.Path)).ToList();
    }
    internal static List<string> Rollback(Dictionary<string, string?> changed)
    {
        var errors = new List<string>();
        foreach (var (target, previous) in changed)
            try { if (previous != null) File.Copy(previous, target, true); else if (File.Exists(target)) File.Delete(target); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { errors.Add("Rollback could not restore " + target + ": " + e.Message); }
        return errors;
    }
    private static bool ProcessStillRunning(int id) { try { using var p = Process.GetProcessById(id); return !p.HasExited; } catch (ArgumentException) { return false; } }
    private static bool IsGameRunning(string root) => Process.GetProcessesByName("RCDriftSim-Win64-Shipping").Any(p => {
        try { return p.MainModule?.FileName?.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) == true; }
        catch { return true; }
    });
}
