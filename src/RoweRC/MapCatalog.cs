using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RoweRC;

public sealed record MapManifest(int Format, string Id, string Name, string Author, string Version,
    string Engine, string Level, Dictionary<string, string> Files);
public sealed record MapEntry(MapManifest Manifest, string Folder, string Fingerprint, bool BuiltIn)
{
    public override string ToString() => $"{Manifest.Name}  ({(BuiltIn ? "built-in" : Manifest.Version)})";
}

public static partial class MapCatalog
{
    public const string SupportedBuild = "1801ffbdfe";
    public const string SupportedExe = "08da7cc9ca54d8f11dc05f70de7f33ed38014b33a82d2ccb4beb7f033592b4cf";
    public const long MaxMapBytes = 4L * 1024 * 1024 * 1024;
    [GeneratedRegex(@"^[a-z0-9][a-z0-9-]{0,63}$")] private static partial Regex IdPattern();
    [GeneratedRegex(@"^[A-Za-z0-9_-]+\.(pak|utoc|ucas)$")] private static partial Regex PayloadPattern();
    [GeneratedRegex(@"^[a-fA-F0-9]{64}$")] private static partial Regex HashPattern();
    public static string Hash(string path)
    {
        using var s = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(s)).ToLowerInvariant();
    }
    public static MapEntry Validate(string folder)
    {
        var manifestPath = Path.Combine(folder, "map.json");
        if (new FileInfo(manifestPath).Length > 65536) throw new InvalidDataException("Map manifest is too large.");
        var m = JsonSerializer.Deserialize<MapManifest>(File.ReadAllText(manifestPath),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new InvalidDataException("Missing manifest.");
        if (m.Format != 1 || m.Id is null || !IdPattern().IsMatch(m.Id) || m.Name is null || m.Name.Length is < 1 or > 100
            || m.Version is null || m.Version.Length is < 1 or > 40 || m.Engine != "5.7" || !Protocol.ValidLevel(m.Level)
            || !m.Level.StartsWith($"/Game/RoweRCMaps/{m.Id.Replace('-', '_')}/", StringComparison.Ordinal)
            || m.Files is null || m.Files.Count is < 1 or > 32) throw new InvalidDataException("Invalid map manifest or namespace. Maps must be cooked for UE 5.7.");
        var canonical = new StringBuilder($"RRCMAP1\n{m.Id}\n{m.Version}\n{m.Engine}\n{m.Level}\n");
        long total = 0;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, expected) in m.Files.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            if (!PayloadPattern().IsMatch(name) || !names.Add(name) || expected is null || !HashPattern().IsMatch(expected))
                throw new InvalidDataException("Invalid or duplicate payload filename/hash.");
            var path = Path.Combine(folder, name);
            if (!File.Exists(path) || new FileInfo(path).Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new InvalidDataException($"Missing payload: {name}");
            total += new FileInfo(path).Length;
            if (total > MaxMapBytes || !Hash(path).Equals(expected, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException($"Map checksum/size failure: {name}");
            canonical.Append(name).Append(':').Append(expected.ToLowerInvariant()).Append('\n');
        }
        if (m.Files.Keys.Any(n => n.EndsWith(".utoc", StringComparison.OrdinalIgnoreCase)
            && !names.Contains(Path.ChangeExtension(n, ".ucas")))) throw new InvalidDataException("An IoStore .utoc needs its matching .ucas.");
        if (!m.Files.Keys.Any(n => n.EndsWith(".pak", StringComparison.OrdinalIgnoreCase))) throw new InvalidDataException("Map package needs a .pak.");
        return new(m, Path.GetFullPath(folder), Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString()))).ToLowerInvariant(), false);
    }
    public static List<MapEntry> Load(string root, out List<string> errors)
    {
        errors = [];
        var result = new List<MapEntry>();
        foreach (var (id, name, level) in new[] {
            ("tandyz", "Tandyz", "/Game/Maps/Tracks/RCTrack_TandyzV2"),
            ("hairpins", "Oops All Hairpins", "/Game/Maps/Tracks/RCTrack_OopsAllHairpins"),
            ("twin-loop", "Twin Loop 50", "/Game/Maps/Tracks/RCTrack_TwinLoop50"),
            ("switchback", "P-Tile Switchback 60", "/Game/Maps/Tracks/RCTrack_PTileSwitchback60") })
            result.Add(new(new(1, id, name, "Chelllabs", SupportedBuild, "5.7", level, []), "", "builtin:" + SupportedBuild + ":" + id, true));
        Directory.CreateDirectory(root);
        foreach (var dir in Directory.GetDirectories(root))
        {
            if (Path.GetFileName(dir) == "Inbox" || Path.GetFileName(dir).StartsWith('.')) continue;
            try { result.Add(Validate(dir)); }
            catch (Exception e) when (e is IOException or JsonException or ArgumentException or NullReferenceException) { errors.Add($"{Path.GetFileName(dir)}: {e.Message}"); }
        }
        return result;
    }
    public static MapEntry ImportZip(string zipPath, string root)
    {
        Directory.CreateDirectory(root);
        var staging = Path.Combine(root, ".import-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            using (var zip = ZipFile.OpenRead(zipPath))
            {
                if (zip.Entries.Count is < 2 or > 33 || zip.Entries.Sum(e => e.Length) > MaxMapBytes) throw new InvalidDataException("Map archive exceeds limits.");
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var e in zip.Entries)
                {
                    if (!(e.FullName == "map.json" || PayloadPattern().IsMatch(e.FullName)) || !seen.Add(e.FullName))
                        throw new InvalidDataException("Archive must contain map.json and payload files at its root, without paths or duplicate names.");
                    // Extract manually with a byte budget: do not trust a ZIP's advertised length alone.
                    using var input = e.Open();
                    using var output = File.Create(Path.Combine(staging, e.FullName));
                    var buffer = new byte[65536]; long count = 0; int read;
                    while ((read = input.Read(buffer)) > 0)
                    {
                        count += read;
                        if (count > e.Length || count > MaxMapBytes) throw new InvalidDataException("Archive expanded beyond its size limit.");
                        output.Write(buffer, 0, read);
                    }
                }
            }
            var entry = Validate(staging);
            var target = Path.Combine(root, entry.Manifest.Id);
            if (Directory.Exists(target)) throw new InvalidDataException("That map ID is already installed. Remove the old map folder before replacing it.");
            Directory.Move(staging, target);
            return entry with { Folder = target };
        }
        finally { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
    }
}
