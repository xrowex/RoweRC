using System.Globalization;
using System.Text;

namespace RoweRC;

// The helper supplies state copies; the game-thread DLL calls the pinned native
// pair solver. No network packet can select an address or invoke a function.
public sealed class NativePairMailbox(string directory)
{
    public sealed record Body(ContactVector P, double Yaw, ContactVector V, double W, double Mass, double Inertia, string Shape);
    public sealed record Result(ContactVector A, double Wa, ContactVector Pa, ContactVector B, double Wb, ContactVector Pb);
    private sealed record Job(long Id, string Key, Body A, Body B, long At);
    private readonly List<Job> queued = [];
    private readonly Dictionary<long, Job> flight = [];
    private readonly Dictionary<string, (Result Value, Job Job)> ready = [];
    private long serial = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), batch, sentAt;
    public void Clear() { queued.Clear(); flight.Clear(); ready.Clear(); batch = 0; }
    public void Poll(long now)
    {
        if (batch == 0) return;
        if (now - sentAt > 150) { flight.Clear(); batch = 0; return; }
        try
        {
            var path = Path.Combine(directory, "native-response.tsv");
            if (!File.Exists(path) || new FileInfo(path).Length > 32768) return;
            var lines = File.ReadAllLines(path); var h = lines[0].Split('\t');
            if (h.Length != 3 || h[0] != "RRCN1" || h[1] != batch.ToString() || h[2] != flight.Count.ToString() || lines.Length != flight.Count + 1) return;
            foreach (var line in lines.Skip(1))
            {
                var p = line.Split('\t');
                if (p.Length != 13 || !long.TryParse(p[0], out var id) || !flight.TryGetValue(id, out var job) || p[1] != "1") continue;
                var v = p.Skip(2).Select(s => double.Parse(s, CultureInfo.InvariantCulture)).ToArray();
                if (v.Any(n => !double.IsFinite(n)) || double.Hypot(v[0],v[1]) > 4.001 || double.Hypot(v[5],v[6]) > 4.001
                    || Math.Abs(v[2]) > 10 || Math.Abs(v[7]) > 10 || double.Hypot(v[3],v[4]) > .02 || double.Hypot(v[8],v[9]) > .02) continue;
                ready[job.Key] = (new(new(v[0],v[1]),v[2],new(v[3],v[4]),new(v[5],v[6]),v[7],new(v[8],v[9])),job);
            }
            flight.Clear(); batch = 0;
        }
        catch (Exception e) when (e is IOException or FormatException or OverflowException or IndexOutOfRangeException) { }
    }
    public Result? Solve(string key, Body a, Body b, long now)
    {
        if (ready.Remove(key, out var result))
            return now - result.Job.At <= 150 && (a.P-result.Job.A.P).Length < .5 && (b.P-result.Job.B.P).Length < .5 ? result.Value : null;
        if (batch == 0 && queued.Count < 28 && !queued.Any(j => j.Key == key)) queued.Add(new(++serial,key,a,b,now));
        return null;
    }
    public void Flush(long now)
    {
        if (batch != 0 || queued.Count == 0) return;
        static string Encode(Body b) => FormattableString.Invariant($"{b.P.X:R}\t{b.P.Y:R}\t{b.Yaw:R}\t{b.V.X:R}\t{b.V.Y:R}\t{b.W:R}\t{b.Mass:R}\t{b.Inertia:R}\t{b.Shape}");
        var next = ++serial; var text = new StringBuilder($"RRCN1\t{next}\t{queued.Count}\n");
        foreach (var j in queued) text.Append(j.Id).Append('\t').Append(Encode(j.A)).Append('\t').Append(Encode(j.B)).Append('\n');
        try
        {
            var path = Path.Combine(directory,"native-request.tsv"); File.WriteAllText(path+".tmp",text.ToString(),new UTF8Encoding(false)); File.Move(path+".tmp",path,true);
            foreach (var j in queued) flight[j.Id]=j; queued.Clear(); batch=next; sentAt=now;
        }
        catch (IOException) { queued.Clear(); }
        foreach (var key in ready.Where(p => now-p.Value.Job.At>150).Select(p=>p.Key).ToArray()) ready.Remove(key);
    }
}
