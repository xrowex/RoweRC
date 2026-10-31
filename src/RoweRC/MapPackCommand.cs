using System.IO.Compression;
using System.Text.Json;

namespace RoweRC;

internal static class MapPackCommand
{
    // --pack-map <cooked-payload-folder> <id> <name> <author> <version> <level> <output.zip>
    public static void Run(string[] args)
    {
        var outputLog = Path.Combine(AppContext.BaseDirectory, "map-pack-result.txt");
        try
        {
            if (args.Length != 8) throw new ArgumentException("Usage: --pack-map <cooked-payload-folder> <id> <name> <author> <version> <level> <output.zip>");
            var source = Path.GetFullPath(args[1]); var output = Path.GetFullPath(args[7]);
            if (File.Exists(output)) throw new IOException("Output already exists: " + output);
            var names = Directory.GetFiles(source).Where(p => new[] { ".pak", ".utoc", ".ucas" }.Contains(Path.GetExtension(p).ToLowerInvariant())).ToArray();
            var hashes = names.ToDictionary(p => Path.GetFileName(p), p => MapCatalog.Hash(p), StringComparer.OrdinalIgnoreCase);
            var manifest = new MapManifest(1, args[2], args[3], args[4], args[5], "5.7", args[6], hashes);
            var staging = Path.Combine(Path.GetTempPath(), "RoweRC-pack-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staging);
            try
            {
                File.WriteAllText(Path.Combine(staging, "map.json"), JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
                foreach (var path in names) File.Copy(path, Path.Combine(staging, Path.GetFileName(path)));
                var map = MapCatalog.Validate(staging);
                Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                ZipFile.CreateFromDirectory(staging, output, CompressionLevel.Fastest, false);
                File.WriteAllText(outputLog, $"Created: {output}\nMap fingerprint: {map.Fingerprint}\nPayload hash validation passed. Cooking/mount paths/gameplay still require in-game testing.\n");
            }
            finally
            {
                var resolved = Path.GetFullPath(staging);
                if (Path.GetDirectoryName(resolved) == Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar)
                    && Path.GetFileName(resolved).StartsWith("RoweRC-pack-", StringComparison.Ordinal)) Directory.Delete(resolved, true);
            }
        }
        catch (Exception e) { File.WriteAllText(outputLog, e.ToString()); Environment.ExitCode = 1; }
    }
}
