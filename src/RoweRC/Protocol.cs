using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace RoweRC;

public sealed record Pose(double X, double Y, double Z, double Pitch, double Yaw, double Roll)
{
    [JsonIgnore] public bool Valid => new[] { X, Y, Z, Pitch, Yaw, Roll }.All(double.IsFinite)
        && Math.Abs(X) < 1e8 && Math.Abs(Y) < 1e8 && Math.Abs(Z) < 1e8
        && Math.Abs(Pitch) <= 360 && Math.Abs(Yaw) <= 360 && Math.Abs(Roll) <= 360;
    [JsonIgnore] public string Tsv => string.Join('\t', new[] { X, Y, Z, Pitch, Yaw, Roll }.Select(v => v.ToString("R", CultureInfo.InvariantCulture)));
}

public sealed record CarMotion(double Time, double[] Wheels, double[] Audio)
{
    [JsonIgnore] public bool Valid => double.IsFinite(Time) && Time >= 0 && Time < 1e10
        && Wheels is { Length: 48 } && Wheels.Select((v, i) => double.IsFinite(v) && Math.Abs(v) <= (i % 6 < 3 ? 10000 : 360)).All(v => v)
        && Audio is { Length: 18 } && Audio.Select((v, i) => double.IsFinite(v) && (i % 3 == 2 ? v is 0 or 1 : i % 3 == 0 ? v >= 0 && v <= 4 : v >= 0.01 && v <= 4)).All(v => v);
    [JsonIgnore] public string Tsv => string.Join(',', new[] { Time }.Concat(Wheels).Concat(Audio).Select(v => v.ToString("R", CultureInfo.InvariantCulture)));
    public static CarMotion? Read(string text)
    {
        var parts = text.Split(',');
        if (parts.Length != 67) return null;
        var v = new double[67];
        for (var i = 0; i < v.Length; i++)
            if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out v[i])) return null;
        var motion = new CarMotion(v[0], v[1..49], v[49..67]);
        return motion.Valid ? motion : null;
    }
}
public sealed record CarFrame(string Run, long Sequence, string Level, Pose Pose, CarMotion? Motion = null, CarPhysics? Physics = null)
{
    [JsonIgnore] public bool Valid => Pose is { Valid: true } && (Motion == null || Motion.Valid) && (Physics == null || Physics.Valid);
}
public sealed record WireFrame(int Protocol, string Room, string Content, CarFrame Car);
public sealed record RemoteCar(ulong Id, string Name, CarFrame Frame, long ReceivedAt, CarAppearance? Appearance = null, CarDamage? Damage = null);

public static partial class Protocol
{
    public const int Version = 2;
    public const int Channel = 27131;
    public const int MaxPacket = 4096;
    [GeneratedRegex(@"^/Game/[A-Za-z0-9_/]+$")] private static partial Regex LevelPattern();
    public static bool ValidLevel(string level) => level.Length < 240 && LevelPattern().IsMatch(level) && !level.Contains("//");
    public static CarFrame? ReadLocal(string line)
    {
        var p = line.Trim().Split('\t');
        if (p.Length is not (12 or 13) || p[0] != "RRC1" || p[1].Length is < 1 or > 64
            || !long.TryParse(p[2], out var seq) || seq < 0 || p[^1] != p[2] || !ValidLevel(p[3])) return null;
        var values = new double[6];
        for (var i = 0; i < 6; i++)
            if (!double.TryParse(p[i + 4], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i])) return null;
        var pose = new Pose(values[0], values[1], values[2], values[3], values[4], values[5]);
        var motion = p[10] == "0" ? null : CarMotion.Read(p[10]);
        var physics = p.Length == 13 && p[11] != "0" ? CarPhysics.Read(p[11]) : null;
        return pose.Valid && (p[10] == "0" || motion != null) && (p.Length == 12 || p[11] == "0" || physics != null)
            ? new(p[1], seq, p[3], pose, motion, physics) : null;
    }
    public static byte[] Encode(WireFrame frame) => JsonSerializer.SerializeToUtf8Bytes(frame);
    public static WireFrame? Decode(byte[] data, string room, string content, string level)
    {
        if (data.Length > MaxPacket) return null;
        try
        {
            var f = JsonSerializer.Deserialize<WireFrame>(data);
            return f is { Protocol: Version, Car.Valid: true } && f.Room == room && f.Content == content
                && f.Car.Level == level && ValidLevel(f.Car.Level) && f.Car.Run.Length is > 0 and <= 64
                && f.Car.Sequence >= 0 ? f : null;
        }
        catch (Exception e) when (e is JsonException or NullReferenceException) { return null; }
    }
}

public sealed class GameBridge
{
    public string DirectoryPath { get; }
    public string? Error { get; private set; }
    private long commandSequence = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    private string? pendingCommand;
    private DateTime appearanceWrite;
    private CarAppearance? appearance;
    private readonly Dictionary<ulong,string> writtenAppearances = [];
    private readonly List<ContactImpulse> contacts = [];
    public GameBridge(string path) { DirectoryPath = path; Directory.CreateDirectory(path); }
    public CarFrame? Read()
    {
        CarFrame? newest = null;
        DateTime newestWrite = DateTime.MinValue;
        foreach (var name in new[] { "local.0.tsv", "local.1.tsv" })
        {
            var path = Path.Combine(DirectoryPath, name);
            try
            {
                if (!File.Exists(path)) continue;
                var written = File.GetLastWriteTimeUtc(path);
                if (DateTime.UtcNow - written > TimeSpan.FromSeconds(2)) continue;
                var f = Protocol.ReadLocal(File.ReadAllText(path));
                if (f != null && (newest == null || (f.Run == newest.Run ? f.Sequence > newest.Sequence : written > newestWrite)))
                { newest = f; newestWrite = written; }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        return newest;
    }
    public CarAppearance? ReadAppearance()
    {
        var path = Path.Combine(DirectoryPath, "appearance.tsv");
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length > CarAppearance.MaxBytes) return null;
            var stamp = File.GetLastWriteTimeUtc(path);
            if (stamp != appearanceWrite)
            {
                var next = CarAppearance.Read(File.ReadAllText(path));
                if (next != null) { appearance = next; appearanceWrite = stamp; }
            }
            return appearance;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }
    public void WriteRemotes(IEnumerable<RemoteCar> cars)
    {
        var text = new StringBuilder($"RRC1\n# {Environment.TickCount64}\n");
        foreach (var c in cars.Take(7))
        {
            var revision = "";
            if (c.Appearance is { } appearance && appearance.Run == c.Frame.Run)
            {
                if (writtenAppearances.GetValueOrDefault(c.Id) != appearance.Revision
                    && Atomic("appearance." + c.Id + ".tsv", appearance.Text)) writtenAppearances[c.Id] = appearance.Revision;
                if (writtenAppearances.GetValueOrDefault(c.Id) == appearance.Revision) revision = appearance.Revision;
            }
            var name=string.Concat(c.Name.EnumerateRunes().Where(r=>!System.Text.Rune.IsControl(r)).Take(48));
            text.Append(c.Id).Append('\t').Append(c.Frame.Pose.Tsv).Append('\t').Append(c.Frame.Run).Append('\t').Append(c.Frame.Sequence)
                .Append('\t').Append(c.Frame.Motion?.Tsv ?? "0").Append('\t').Append(revision).Append('\t').Append(name).Append('\t').Append(c.Damage?.Run==c.Frame.Run ? c.Damage.Revision : 0)
                .Append('\t').Append(c.Damage?.Run==c.Frame.Run ? c.Damage.Mesh : "").Append('\t').Append(c.Damage?.Run==c.Frame.Run ? c.Damage.Dents : "").Append('\n');
        }
        Atomic("remote.tsv", text.ToString());
    }
    public void OpenMap(string level)
    {
        if (!Protocol.ValidLevel(level)) throw new InvalidDataException("Invalid Unreal map path.");
        pendingCommand = $"{++commandSequence}\topen\t{level}\n"; Flush();
    }
    public void Probe() { pendingCommand = $"{++commandSequence}\tdump\t\n"; Flush(); }
    public void Contact(string key, ContactImpulse impulse)
    {
        contacts.Add(impulse); if (contacts.Count > 24) contacts.RemoveAt(0);
        Atomic("contact-events.tsv", key + "\n" + string.Join('\n', contacts.Select(c => c.Tsv)) + "\n");
    }
    public void ClearContacts() { contacts.Clear(); Atomic("contact-events.tsv", ""); }
    public void Flush() { if (pendingCommand != null && Atomic("command.tsv", pendingCommand)) pendingCommand = null; }
    private bool Atomic(string name, string text)
    {
        try
        {
            var path = Path.Combine(DirectoryPath, name);
            File.WriteAllText(path + ".tmp", text, new UTF8Encoding(false));
            File.Move(path + ".tmp", path, true);
            Error = null;
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Error = e.Message; return false; }
    }
}
