using System.Text.Json;

namespace RoweRC;

internal static class ContactSmoke
{
    // Explicit offline developer test: one bounded contact on the stationary local
    // car. Requires an empty host lobby and the host's contact mode enabled.
    public static void Run()
    {
        var bridge=new GameBridge(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"RoweRC","bridge"));
        var state=File.ReadAllText(Path.Combine(bridge.DirectoryPath,"ui-state.tsv"));
        if (!state.Contains("\nrole\thost\n") || !state.Contains("\ncontact\t1\n") || !state.Contains("0 friend(s), 0 cars"))
            throw new InvalidOperationException("Contact smoke test requires an empty host lobby with contact enabled.");
        var key=state.Split('\n').First(line=>line.StartsWith("contact-key\t"))[12..].TrimEnd('\r');
        var car=bridge.Read() ?? throw new InvalidOperationException("No live car.");
        var physics=car.Physics ?? throw new InvalidOperationException("No native dynamics/collider frame.");
        if (!physics.Driving || Math.Abs(physics.Vx)+Math.Abs(physics.Vy)+Math.Abs(physics.W)>.02)
            throw new InvalidOperationException("Contact smoke test requires a stationary car in Drive.");
        var wire=PeerEnvelope.Pose(new(Protocol.Version,MapCatalog.SupportedBuild,new string('a',32),1,"test","test",car.Level,true),car).Encode();
        if (wire.Length>Protocol.MaxPacket) throw new InvalidOperationException("Native collider packet exceeds network limit.");
        var shape=ContactGeometry.Parse(physics.Shape)!;
        var p=new ContactVector(car.Pose.X*.01,car.Pose.Y*.01); var yaw=car.Pose.Yaw*Math.PI/180;
        var forward=new ContactVector(Math.Cos(yaw),Math.Sin(yaw));
        var native = new NativePairMailbox(bridge.DirectoryPath);
        var at=Environment.TickCount64;
        var a=new NativePairMailbox.Body(p,yaw,new(0,0),0,physics.Mass,physics.Inertia,physics.Shape);
        var b=new NativePairMailbox.Body(p+forward*.4,yaw,forward*-1,0,physics.Mass,physics.Inertia,physics.Shape);
        native.Solve("smoke",a,b,at); native.Flush(at);
        NativePairMailbox.Result? result=null;
        while (Environment.TickCount64-at<140)
        {
            Thread.Sleep(5);native.Poll(Environment.TickCount64);
            result=native.Solve("smoke",a,b,Environment.TickCount64);
            if(result!=null)break;
        }
        if(result==null)throw new InvalidOperationException("Native pair fixture did not return a contact; inspect native-error.txt.");
        var hit=new ContactImpulse(Math.Max(physics.Ack+1,DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()),car.Run,physics.Generation,physics.Time,
            car.Pose.X,car.Pose.Y,result.A.X,result.A.Y,result.Wa,result.Pa.X,result.Pa.Y);
        bridge.Contact(key,hit);
        File.WriteAllText(Path.Combine(bridge.DirectoryPath,"contact-smoke.json"),JsonSerializer.Serialize(new { Before=car,Hit=hit,PacketBytes=wire.Length,Shapes=shape.Length,Vertices=shape.Sum(s=>s.Length) },new JsonSerializerOptions{WriteIndented=true}));
    }
}
