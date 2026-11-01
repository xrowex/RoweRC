using System.Globalization;
using System.Text.Json.Serialization;

namespace RoweRC;

public sealed record CarPhysics(double Time, long Generation, double Vx, double Vy, double W,
    double Mass, double Inertia, long Ack, bool Driving, string Shape)
{
    [JsonIgnore] public bool Valid => double.IsFinite(Time) && Time >= 0 && Generation >= 0 && Ack >= 0
        && new[] { Vx, Vy, W, Mass, Inertia }.All(double.IsFinite)
        && Math.Abs(Vx) <= 30 && Math.Abs(Vy) <= 30 && Math.Abs(W) <= 30
        && Mass is >= .1 and <= 10 && Inertia is >= .0001 and <= 2 && ContactGeometry.Parse(Shape) != null;
    [JsonIgnore] public string Tsv => string.Join(',', new[] { Time.ToString("R", CultureInfo.InvariantCulture), Generation.ToString(),
        Vx.ToString("R", CultureInfo.InvariantCulture), Vy.ToString("R", CultureInfo.InvariantCulture), W.ToString("R", CultureInfo.InvariantCulture),
        Mass.ToString("R", CultureInfo.InvariantCulture), Inertia.ToString("R", CultureInfo.InvariantCulture), Ack.ToString(), Driving ? "1" : "0", Shape });
    public static CarPhysics? Read(string text)
    {
        var p = text.Split(',', 10);
        if (p.Length != 10 || !long.TryParse(p[1], out var generation) || !long.TryParse(p[7], out var ack) || p[8] is not ("0" or "1")) return null;
        var values = new double[6]; var indices = new[] { 0, 2, 3, 4, 5, 6 };
        for (var i = 0; i < indices.Length; i++)
            if (!double.TryParse(p[indices[i]], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i])) return null;
        var result = new CarPhysics(values[0], generation, values[1], values[2], values[3], values[4], values[5], ack, p[8] == "1", p[9]);
        return result.Valid ? result : null;
    }
}

public readonly record struct ContactVector(double X, double Y)
{
    public static ContactVector operator +(ContactVector a, ContactVector b) => new(a.X + b.X, a.Y + b.Y);
    public static ContactVector operator -(ContactVector a, ContactVector b) => new(a.X - b.X, a.Y - b.Y);
    public static ContactVector operator *(ContactVector a, double b) => new(a.X * b, a.Y * b);
    public double Dot(ContactVector b) => X * b.X + Y * b.Y;
    public double Cross(ContactVector b) => X * b.Y - Y * b.X;
    public double Length => Math.Sqrt(Dot(this));
}

public sealed record ContactImpulse(long Sequence, string Run, long Generation, double Time,
    double X, double Y, double Dvx, double Dvy, double Dw, double Dpx = 0, double Dpy = 0)
{
    [JsonIgnore] public bool Valid => Sequence > 0 && Run is { Length: > 0 and <= 64 } && Run.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_') && Generation >= 0
        && new[] { Time, X, Y, Dvx, Dvy, Dw, Dpx, Dpy }.All(double.IsFinite) && Time >= 0
        && Math.Abs(X) < 1e8 && Math.Abs(Y) < 1e8 && Dvx * Dvx + Dvy * Dvy <= 16.001 && Math.Abs(Dw) <= 10 && Dpx*Dpx+Dpy*Dpy <= .0004;
    [JsonIgnore] public string Tsv => string.Join('\t', new[] { Sequence.ToString(), Run, Generation.ToString(), Time.ToString("R", CultureInfo.InvariantCulture),
        X.ToString("R", CultureInfo.InvariantCulture), Y.ToString("R", CultureInfo.InvariantCulture),
        Dvx.ToString("R", CultureInfo.InvariantCulture), Dvy.ToString("R", CultureInfo.InvariantCulture), Dw.ToString("R", CultureInfo.InvariantCulture), Dpx.ToString("R", CultureInfo.InvariantCulture), Dpy.ToString("R", CultureInfo.InvariantCulture) });
}

public static class ContactGeometry
{
    // Metres in the car's local XY plane. Body hull plus four tire cylinders'
    // projected outlines; never accepts an arbitrary physics command or asset.
    public static ContactVector[][]? Parse(string? text)
    {
        if (text is null || text.Length is < 12 or > 2300) return null;
        var polygons = text.Split(';'); if (polygons.Length is < 1 or > 5) return null;
        var result = new List<ContactVector[]>(); var total = 0;
        foreach (var polygon in polygons)
        {
            var points = new List<ContactVector>();
            foreach (var pair in polygon.Split('|'))
            {
                var v = pair.Split(':');
                if (v.Length != 2 || !double.TryParse(v[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
                    || !double.TryParse(v[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y)
                    || !double.IsFinite(x) || !double.IsFinite(y) || Math.Abs(x) > 1 || Math.Abs(y) > 1) return null;
                points.Add(new(x, y));
            }
            if (points.Count is < 3 or > 48 || (total += points.Count) > 128) return null;
            double area = 0, sign = 0;
            for (var i = 0; i < points.Count; i++)
            {
                var a = points[i]; var b = points[(i + 1) % points.Count]; var c = points[(i + 2) % points.Count];
                if ((b - a).Length < .00001) return null;
                var cross = (b - a).Cross(c - b);
                if (Math.Abs(cross) > 1e-9) { if (sign * cross < 0) return null; sign = cross; }
                area += a.Cross(b);
            }
            if (Math.Abs(area) < .00001) return null;
            result.Add(points.ToArray());
        }
        return result.ToArray();
    }
    private static ContactVector[] Transform(ContactVector[] polygon, ContactVector position, double yaw)
    {
        var c = Math.Cos(yaw); var s = Math.Sin(yaw);
        return polygon.Select(p => position + new ContactVector(c * p.X - s * p.Y, s * p.X + c * p.Y)).ToArray();
    }
    private static (ContactVector Normal, double Depth)? Intersect(ContactVector[] a, ContactVector[] b, ContactVector direction)
    {
        if (a.Max(p => p.X) <= b.Min(p => p.X) || b.Max(p => p.X) <= a.Min(p => p.X)
            || a.Max(p => p.Y) <= b.Min(p => p.Y) || b.Max(p => p.Y) <= a.Min(p => p.Y)) return null;
        double depth = double.MaxValue; ContactVector normal = default;
        foreach (var polygon in new[] { a, b })
            for (var i = 0; i < polygon.Length; i++)
            {
                var edge = polygon[(i + 1) % polygon.Length] - polygon[i]; var axis = new ContactVector(-edge.Y, edge.X) * (1 / edge.Length);
                var amin = a.Min(p => p.Dot(axis)); var amax = a.Max(p => p.Dot(axis));
                var bmin = b.Min(p => p.Dot(axis)); var bmax = b.Max(p => p.Dot(axis));
                var overlap = Math.Min(amax, bmax) - Math.Max(amin, bmin);
                if (overlap <= 0) return null;
                if (overlap < depth) { depth = overlap; normal = axis.Dot(direction) >= 0 ? axis : axis * -1; }
            }
        return (normal, depth);
    }
    public static bool Overlaps(ContactVector pa, double ya, ContactVector[][] sa, ContactVector pb, double yb, ContactVector[][] sb)
    {
        if ((pb-pa).Length > 2) return false;
        var a=sa.Select(p=>Transform(p,pa,ya)).ToArray(); var b=sb.Select(p=>Transform(p,pb,yb)).ToArray();
        return a.Any(x=>b.Any(y=>Intersect(x,y,pb-pa)!=null));
    }
    // Reference approximation for regression fixtures only. Production calls the game's solver.
    public static (ContactVector A, double Wa, ContactVector B, double Wb)? Solve(
        ContactVector pa, double ya, ContactVector va, double wa, double ma, double ia, ContactVector[][] sa,
        ContactVector pb, double yb, ContactVector vb, double wb, double mb, double ib, ContactVector[][] sb)
    {
        if ((pb - pa).Length > 2) return null;
        (ContactVector Normal, double Depth, ContactVector Point)? best = null;
        var worldA=sa.Select(p => Transform(p, pa, ya)).ToArray(); var worldB=sb.Select(p => Transform(p, pb, yb)).ToArray();
        foreach (var a in worldA) foreach (var b in worldB)
        {
            if (Intersect(a, b, pb - pa) is not { } hit) continue;
            // Average the contact face's support vertices, avoiding spurious
            // corner torque on a centered bumper-to-bumper impact.
            var max = a.Max(p => p.Dot(hit.Normal)); var min = b.Min(p => p.Dot(hit.Normal));
            var fa = a.Where(p => max - p.Dot(hit.Normal) < .002).ToArray();
            var fb = b.Where(p => p.Dot(hit.Normal) - min < .002).ToArray();
            var point = new ContactVector((fa.Average(p => p.X) + fb.Average(p => p.X)) / 2,
                (fa.Average(p => p.Y) + fb.Average(p => p.Y)) / 2);
            if (best == null || hit.Depth > best.Value.Depth) best = (hit.Normal, hit.Depth, point);
        }
        if (best is not { } contact) return null;
        var ra = contact.Point - pa; var rb = contact.Point - pb; var n = contact.Normal;
        var relative = vb + new ContactVector(-wb * rb.Y, wb * rb.X) - va - new ContactVector(-wa * ra.Y, wa * ra.X);
        var closing = relative.Dot(n); if (closing >= -.025) return null;
        var denominator = 1 / ma + 1 / mb + Math.Pow(ra.Cross(n), 2) / ia + Math.Pow(rb.Cross(n), 2) / ib;
        var j = -(1.05 * closing) / denominator; // Low restitution, plastic RC shells.
        var tangent = new ContactVector(-n.Y, n.X);
        var td = 1 / ma + 1 / mb + Math.Pow(ra.Cross(tangent), 2) / ia + Math.Pow(rb.Cross(tangent), 2) / ib;
        var jt = Math.Clamp(-relative.Dot(tangent) / td, -.25 * j, .25 * j);
        var impulse = n * j + tangent * jt;
        var da = impulse * (-1 / ma); var db = impulse * (1 / mb);
        var dwa = -ra.Cross(impulse) / ia; var dwb = rb.Cross(impulse) / ib;
        // Scale the whole pair together so bounds preserve equal/opposite impulse.
        var scale = Math.Min(1, Math.Min(4 / Math.Max(.001, Math.Max(da.Length, db.Length)), 10 / Math.Max(.001, Math.Max(Math.Abs(dwa), Math.Abs(dwb)))));
        return (da * scale, dwa * scale, db * scale, dwb * scale);
    }
}
