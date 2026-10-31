using System.Text.Json;

namespace RoweRC;

// One Steam metadata value is the commit point for a host's map change.
public sealed record LobbyState(int Protocol, string Build, string Room, long Epoch,
    string MapId, string Content, string Level)
{
    public static LobbyState Create(MapEntry map, string room, long epoch) =>
        new(RoweRC.Protocol.Version, MapCatalog.SupportedBuild, room, epoch, map.Manifest.Id, map.Fingerprint, map.Manifest.Level);
    public string ToJson() => JsonSerializer.Serialize(this);
    public static LobbyState? Read(string json)
    {
        if (json.Length > 4096) return null;
        try
        {
            var state = JsonSerializer.Deserialize<LobbyState>(json);
            return state is { Protocol: RoweRC.Protocol.Version, Epoch: > 0, Room.Length: 32, MapId.Length: > 0 and < 65, Content.Length: > 0 and < 256 }
                && state.Build == MapCatalog.SupportedBuild && RoweRC.Protocol.ValidLevel(state.Level) ? state : null;
        }
        catch (Exception e) when (e is JsonException or NullReferenceException) { return null; }
    }
    public bool SameMap(MapEntry map) => map.Manifest.Id == MapId && map.Fingerprint == Content && map.Manifest.Level == Level;
}

public sealed record PeerEnvelope(int Protocol, string Room, long Epoch, string Content, string Kind,
    string Level, bool Ready, CarFrame? Car, AppearanceChunk? Appearance = null, string Ack = "")
{
    public static PeerEnvelope Hello(LobbyState state, CarFrame? local) =>
        new(RoweRC.Protocol.Version, state.Room, state.Epoch, state.Content, "hello", local?.Level ?? "", local?.Level == state.Level, null);
    public static PeerEnvelope Pose(LobbyState state, CarFrame local) =>
        new(RoweRC.Protocol.Version, state.Room, state.Epoch, state.Content, "pose", local.Level, local.Level == state.Level, local);
    public byte[] Encode() => JsonSerializer.SerializeToUtf8Bytes(this);
    public static PeerEnvelope? Read(byte[] bytes, LobbyState state)
    {
        if (bytes.Length is < 1 or > RoweRC.Protocol.MaxPacket) return null;
        try
        {
            var packet = JsonSerializer.Deserialize<PeerEnvelope>(bytes);
            if (packet == null || packet.Protocol != RoweRC.Protocol.Version || packet.Room != state.Room || packet.Epoch != state.Epoch
                || packet.Content != state.Content || packet.Level is null || packet.Level.Length > 240) return null;
            if (packet.Ack is null || packet.Ack.Length != 0 && (packet.Ack.Length != 64 || !packet.Ack.All(char.IsAsciiHexDigit))) return null;
            if (packet.Kind == "appearance") return packet.Car == null && packet.Appearance is { Valid: true } ? packet : null;
            if (packet.Appearance != null) return null;
            if (packet.Kind == "hello") return packet.Car == null && (packet.Level == "" || RoweRC.Protocol.ValidLevel(packet.Level))
                ? packet with { Ready = packet.Ready && packet.Level == state.Level } : null;
            return packet.Kind == "pose" && packet.Ready && packet.Level == state.Level
                && packet.Car is { Sequence: >= 0, Run.Length: > 0 and <= 64, Valid: true } && packet.Car.Level == state.Level ? packet : null;
        }
        catch (Exception e) when (e is JsonException or NullReferenceException) { return null; }
    }
}

public enum PeerRoute { Messages, Legacy }

public sealed class PeerLink(ulong id, long now)
{
    public ulong Id { get; } = id;
    public long JoinedAt { get; } = now;
    public long ModernReceived { get; private set; } = long.MinValue / 2;
    public long LegacyReceived { get; private set; } = long.MinValue / 2;
    public long LastHello { get; set; } = long.MinValue / 2;
    public long LastAttempt { get; set; } = long.MinValue / 2;
    public string Level { get; private set; } = "";
    public bool Ready { get; private set; }
    public long PacketsReceived { get; private set; }
    public long PosesReceived { get; private set; }
    public string SendError { get; set; } = "";
    public AppearanceReceiver Appearance { get; } = new();
    public string AppearanceAck { get; private set; } = "";
    public string OutgoingAppearance { get; set; } = "";
    public int NextAppearanceChunk { get; set; }
    public long LastAppearanceChunk { get; set; }
    public long AppearanceSentAt { get; set; }
    public CarFrame? Car { get; private set; }
    public long CarReceivedAt { get; private set; }
    public PeerRoute Route(long clock) => clock - ModernReceived < 4000 || clock - LegacyReceived >= 4000 ? PeerRoute.Messages : PeerRoute.Legacy;
    public bool TryLegacy(long clock) => clock - JoinedAt > 2000 && clock - ModernReceived > 4000;
    public void Accept(PeerEnvelope packet, PeerRoute route, long clock)
    {
        if (route == PeerRoute.Messages) ModernReceived = clock; else LegacyReceived = clock;
        PacketsReceived++; Level = packet.Level; Ready = packet.Ready;
        if (packet.Kind == "hello") AppearanceAck = packet.Ack;
        if (packet.Appearance != null) Appearance.Accept(packet.Appearance, clock);
        if (packet.Car is not { } car) return;
        if (Car != null && Car.Run == car.Run && Car.Sequence >= car.Sequence) return;
        Car = car; CarReceivedAt = clock; PosesReceived++;
    }
    public bool HasCar(long clock) => Car != null && clock - CarReceivedAt < 3000;
}
