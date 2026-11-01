using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace RoweRC;

// Bounded stock-asset snapshot, including the native Workbench shader used by rims.
// No executable data or files are transferred.
public sealed partial record CarAppearance(string Run, string Revision, string Text, int Parts, long Serial)
{
    public const int MaxBytes = 128 * 1024, ChunkBytes = 1800;
    [GeneratedRegex(@"^[A-Za-z0-9_ -]{1,64}$")] private static partial Regex NamePattern();
    [GeneratedRegex(@"^/Game/Vehicle/[A-Za-z0-9_/]+\.[A-Za-z0-9_]+$")] private static partial Regex AssetPattern();
    public static bool Asset(string path) => path.Length <= 240 && !path.Contains("//")
        && (AssetPattern().IsMatch(path) || path == "/Game/Environment/Workbench/M_Workbench.M_Workbench" || path == "/Engine/EngineMaterials/DefaultMaterial.DefaultMaterial")
        && path[(path.LastIndexOf('/') + 1)..path.LastIndexOf('.')] == path[(path.LastIndexOf('.') + 1)..];
    private static bool Numbers(string value, int count, Func<int, double, bool> valid)
    {
        var values = value.Split(',');
        return values.Length == count && values.Select((s, i) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
            && double.IsFinite(v) && valid(i, v)).All(x => x);
    }
    public static CarAppearance? Read(string text)
    {
        if (text.Length > MaxBytes || Encoding.UTF8.GetByteCount(text) > MaxBytes) return null;
        var lines = text.Replace("\r", "").TrimEnd('\n').Split('\n');
        if (lines.Length < 3) return null;
        var header = lines[0].Split('\t'); var end = lines[^1].Split('\t');
        if (header.Length != 3 || header[0] != "RRA1" || header[1].Length is < 1 or > 64
            || !header[1].All(c => char.IsAsciiLetterOrDigit(c) || c == '-') || !long.TryParse(header[2], out var serial) || serial < 0
            || end.Length != 3 || end[0] != "end" || end[2] != header[2] || !int.TryParse(end[1], out var count) || count is < 1 or > 160) return null;
        var parts = new HashSet<int>(); var names = new HashSet<string>(); var materials = new HashSet<string>();
        var parameters = new HashSet<string>(); var parameterCounts = new Dictionary<string, int>();
        foreach (var line in lines[1..^1])
        {
            var p = line.Split('\t');
            if (p.Length < 4 || !int.TryParse(p[1], out var index) || index is < 1 or > 160) return null;
            if (p[0] == "p")
            {
                if (p.Length != 6 || !parts.Add(index) || !NamePattern().IsMatch(p[2]) || !names.Add(p[2]) || !Asset(p[3])
                    || !int.TryParse(p[4], out var wheel) || wheel is < 0 or > 8
                    || !Numbers(p[5], 12, (i, v) => i < 3 ? Math.Abs(v) < 10000 : i < 6 ? Math.Abs(v) <= 360 : Math.Abs(v) is >= .000001 and <= 100)) return null;
            }
            else
            {
                if (!parts.Contains(index) || !int.TryParse(p[2], out var slot) || slot is < 0 or > 7) return null;
                var key = index + ":" + slot;
                if (p[0] == "m") { if (p.Length != 4 || !materials.Add(key) || !Asset(p[3])) return null; }
                else
                {
                    if (p.Length != 5 || !materials.Contains(key) || !NamePattern().IsMatch(p[3]) || !parameters.Add(key + ":" + p[0] + ":" + p[3])) return null;
                    var budget = key + ":" + p[0]; parameterCounts[budget] = parameterCounts.GetValueOrDefault(budget) + 1;
                    if (parameterCounts[budget] > 8) return null;
                    if (p[0] == "s") { if (!Numbers(p[4], 1, (_, v) => Math.Abs(v) <= 10000)) return null; }
                    else if (p[0] == "v") { if (!Numbers(p[4], 4, (_, v) => v is >= 0 and <= 16)) return null; }
                    else return null;
                }
            }
        }
        if (parts.Count != count || !Enumerable.Range(1, count).All(parts.Contains)) return null;
        return new(header[1], Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text))), text, count, serial);
    }
    public AppearanceChunk[] Chunks()
    {
        var bytes = Encoding.UTF8.GetBytes(Text); var total = (bytes.Length + ChunkBytes - 1) / ChunkBytes;
        return Enumerable.Range(0, total).Select(i => new AppearanceChunk(Revision, i, total,
            Convert.ToBase64String(bytes.AsSpan(i * ChunkBytes, Math.Min(ChunkBytes, bytes.Length - i * ChunkBytes))))).ToArray();
    }
}

public sealed record AppearanceChunk(string Revision, int Index, int Total, string Data)
{
    public bool Valid => Revision is { Length: 64 } && Revision.All(char.IsAsciiHexDigit) && Total is > 0 and <= 73
        && Index >= 0 && Index < Total && Data is { Length: > 0 and <= 2400 };
}

public sealed class AppearanceReceiver
{
    private string revision = "";
    private byte[]?[] chunks = [];
    private long started;
    public CarAppearance? Current { get; private set; }
    public bool Accept(AppearanceChunk chunk, long now)
    {
        if (!chunk.Valid || chunk.Revision == Current?.Revision) return false;
        if (revision != chunk.Revision || now - started > 15000)
        { revision = chunk.Revision; chunks = new byte[chunk.Total][]; started = now; }
        if (chunks.Length != chunk.Total) return false;
        byte[] bytes;
        try { bytes = Convert.FromBase64String(chunk.Data); } catch (FormatException) { return false; }
        if (bytes.Length is < 1 or > CarAppearance.ChunkBytes) return false;
        chunks[chunk.Index] = bytes;
        if (chunks.Any(c => c == null)) return false;
        var payload = chunks.SelectMany(c => c!).ToArray();
        if (payload.Length > CarAppearance.MaxBytes || Convert.ToHexStringLower(SHA256.HashData(payload)) != revision) return false;
        var document = CarAppearance.Read(Encoding.UTF8.GetString(payload));
        if (document == null || document.Revision != revision) return false;
        if (Current is { } old && document.Run == old.Run && document.Serial <= old.Serial) return false;
        Current = document; return true;
    }
}
