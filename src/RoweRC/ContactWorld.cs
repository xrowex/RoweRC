namespace RoweRC;

// One host resolves contacts. Owners keep their native driving physics and apply
// bounded velocity/angular-velocity deltas exactly once, then acknowledge them.
public sealed class ContactWorld
{
    public sealed record Input(ulong Id, CarFrame Frame, double Age);
    private sealed record Pending(ContactImpulse Hit, long At);
    private sealed record Body(Input Input, ContactVector P, double Yaw, ContactVector V, double W, CarPhysics Physics, ContactVector[][] Shape);
    private readonly Dictionary<ulong, List<Pending>> pending = [];
    private readonly Dictionary<(ulong, ulong), long> lastContact = [];
    private readonly Dictionary<string, ContactVector[][]> shapes = [];
    private Dictionary<ulong, Body> previous = [];
    private long previousAt;
    private long sequence = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    public void Clear() { pending.Clear(); lastContact.Clear(); previous.Clear(); }
    public IEnumerable<(ulong Id, ContactImpulse Hit)> Step(IEnumerable<Input> inputs, long now)
    {
        var bodies = new List<Body>(); var results = new List<(ulong, ContactImpulse)>();
        foreach (var input in inputs)
        {
            if (input.Frame.Physics is not { Driving: true } physics || input.Age is < 0 or > .25) continue;
            if (!shapes.TryGetValue(physics.Shape, out var shape))
            {
                shape = ContactGeometry.Parse(physics.Shape); if (shape == null) continue;
                if (shapes.Count >= 128) shapes.Clear(); shapes[physics.Shape] = shape;
            }
            var p = new ContactVector(input.Frame.Pose.X * .01, input.Frame.Pose.Y * .01);
            var v = new ContactVector(physics.Vx, physics.Vy); var yaw = input.Frame.Pose.Yaw * Math.PI / 180; var w = physics.W;
            p += v * input.Age; yaw += w * input.Age;
            if (!pending.TryGetValue(input.Id, out var queue)) pending[input.Id] = queue = [];
            queue.RemoveAll(item => item.Hit.Sequence <= physics.Ack || item.Hit.Run != input.Frame.Run
                || item.Hit.Generation != physics.Generation || now - item.At > 650);
            foreach (var item in queue)
            {
                var dv = new ContactVector(item.Hit.Dvx, item.Hit.Dvy); var age = (now - item.At) / 1000.0;
                v += dv; w += item.Hit.Dw; p += dv * age; yaw += item.Hit.Dw * age;
            }
            bodies.Add(new(input, p, yaw, v, w, physics, shape));
        }
        for (var i = 0; i < bodies.Count; i++) for (var j = i + 1; j < bodies.Count; j++)
        {
            var a = bodies[i]; var b = bodies[j];
            var pair = a.Input.Id < b.Input.Id ? (a.Input.Id, b.Input.Id) : (b.Input.Id, a.Input.Id);
            if (now - lastContact.GetValueOrDefault(pair, long.MinValue / 2) < 100) continue;
            previous.TryGetValue(a.Input.Id, out var oldA); previous.TryGetValue(b.Input.Id, out var oldB);
            var elapsed = now - previousAt;
            var sweep = elapsed is > 0 and < 50 && oldA?.Input.Frame.Run == a.Input.Frame.Run && oldB?.Input.Frame.Run == b.Input.Frame.Run
                && oldA.Physics.Generation == a.Physics.Generation && oldB.Physics.Generation == b.Physics.Generation
                && (oldA.P - a.P).Length < 1 && (oldB.P - b.P).Length < 1;
            var steps = sweep ? Math.Max(1, (int)Math.Ceiling(elapsed / 3.0)) : 1;
            for (var step = 1; step <= steps; step++)
            {
                var t = step / (double)steps;
                var pa = sweep ? oldA!.P * (1 - t) + a.P * t : a.P;
                var pb = sweep ? oldB!.P * (1 - t) + b.P * t : b.P;
                var hit = ContactGeometry.Solve(pa, a.Yaw, a.V, a.W, a.Physics.Mass, a.Physics.Inertia, a.Shape,
                    pb, b.Yaw, b.V, b.W, b.Physics.Mass, b.Physics.Inertia, b.Shape);
                if (hit is not { } impulse) continue;
                lastContact[pair] = now; var id = ++sequence;
                ContactImpulse Make(Body body, ContactVector dv, double dw) => new(id, body.Input.Frame.Run, body.Physics.Generation,
                    body.Physics.Time, body.Input.Frame.Pose.X, body.Input.Frame.Pose.Y, dv.X, dv.Y, dw);
                var ha = Make(a, impulse.A, impulse.Wa); var hb = Make(b, impulse.B, impulse.Wb);
                pending[a.Input.Id].Add(new(ha, now)); pending[b.Input.Id].Add(new(hb, now));
                results.Add((a.Input.Id, ha)); results.Add((b.Input.Id, hb));
                bodies[i] = a with { V = a.V + impulse.A, W = a.W + impulse.Wa };
                bodies[j] = b with { V = b.V + impulse.B, W = b.W + impulse.Wb };
                break;
            }
        }
        previous = bodies.ToDictionary(b => b.Input.Id); previousAt = now;
        return results;
    }
}
