using System.Diagnostics;
using Steamworks;

namespace RoweRC;

public sealed class SteamSession : IDisposable
{
    private readonly GameBridge bridge;
    private readonly SessionLog log;
    private readonly List<IDisposable> callbacks = [];
    private readonly Dictionary<ulong, PeerLink> peers = [];
    private readonly Dictionary<ulong, (long Window, int Count)> rate = [];
    private readonly CallResult<LobbyCreated_t> created;
    private readonly CallResult<LobbyEnter_t> entered;
    private readonly Func<string, MapEntry?> findMap;
    private readonly Func<string, MapEntry?>? findLevel;
    private CSteamID lobby, owner;
    private LobbyState? state;
    private MapEntry? map;
    private bool initialized, pending, cancelPending;
    private long lastMembership, lastSend, lastLog, lastFollow, mapRequestedAt;
    private (string Run, long Seq) lastFrame;
    private string localProblem = "", networkFailure = "";
    private MapEntry? requestedMap;
    public event Action? Changed;
    public string Status { get; private set; } = "Steam disconnected";
    public string Persona { get; private set; } = "";
    public string Code => lobby.IsValid() ? lobby.m_SteamID.ToString() : "";
    public bool Connected => initialized;
    public MapEntry? ActiveMap => map;
    public bool InLobby => lobby.IsValid();
    public bool Busy => pending || InLobby;
    public bool Hosting => InLobby && owner == SteamUser.GetSteamID();
    public string Role => InLobby ? Hosting ? "host" : "guest" : "idle";
    public IEnumerable<string> Players => peers.Values.Select(PeerDescription);
    public Func<MapEntry, bool>? CanLoadMap { get; set; }
    public SteamSession(GameBridge bridge, Func<string, MapEntry?> findMap, Func<string, MapEntry?>? findLevel = null)
    {
        this.bridge = bridge; this.findMap = findMap; this.findLevel = findLevel;
        log = new(bridge.DirectoryPath);
        created = CallResult<LobbyCreated_t>.Create(OnCreated);
        entered = CallResult<LobbyEnter_t>.Create(OnEntered);
    }
    private void SetStatus(string value)
    {
        if (Status == value) return;
        Status = value; log.Write(value); Changed?.Invoke();
    }
    public void Connect(uint appId)
    {
        if (initialized) return;
        if (appId == 0) throw new ArgumentException("Steam App ID is missing.");
        Environment.SetEnvironmentVariable("SteamAppId", appId.ToString());
        Environment.SetEnvironmentVariable("SteamGameId", appId.ToString());
        if (!SteamAPI.Init()) throw new InvalidOperationException("Steam initialization failed. Start Steam and sign in.");
        initialized = true;
        SteamNetworkingUtils.InitRelayNetworkAccess();
        SteamNetworking.AllowP2PPacketRelay(true);
        Persona = SteamFriends.GetPersonaName();
        callbacks.Add(Callback<SteamNetworkingMessagesSessionRequest_t>.Create(request => {
            RefreshMembers(); var identity = request.m_identityRemote; var id = identity.GetSteamID64();
            var allowed = peers.ContainsKey(id);
            log.Write($"Modern session request peer={id} allowed={allowed}");
            if (allowed) SteamNetworkingMessages.AcceptSessionWithUser(ref identity);
            else SteamNetworkingMessages.CloseSessionWithUser(ref identity);
        }));
        callbacks.Add(Callback<SteamNetworkingMessagesSessionFailed_t>.Create(failure => {
            networkFailure = $"Steam peer connection failed ({failure.m_info.m_eEndReason}): {failure.m_info.m_szEndDebug}";
            log.Write(networkFailure);
        }));
        callbacks.Add(Callback<P2PSessionRequest_t>.Create(request => {
            RefreshMembers(); var id = request.m_steamIDRemote.m_SteamID;
            log.Write($"Legacy session request peer={id} allowed={peers.ContainsKey(id)}");
            if (peers.ContainsKey(id)) SteamNetworking.AcceptP2PSessionWithUser(request.m_steamIDRemote);
            else SteamNetworking.CloseP2PSessionWithUser(request.m_steamIDRemote);
        }));
        callbacks.Add(Callback<P2PSessionConnectFail_t>.Create(failure => log.Write($"Legacy session failure peer={failure.m_steamIDRemote} error={failure.m_eP2PSessionError}")));
        callbacks.Add(Callback<GameLobbyJoinRequested_t>.Create(r => { if (!Busy) Join(r.m_steamIDLobby.m_SteamID); }));
        callbacks.Add(Callback<LobbyChatUpdate_t>.Create(_ => { RefreshMembers(); Changed?.Invoke(); }));
        callbacks.Add(Callback<LobbyDataUpdate_t>.Create(r => {
            if (InLobby && r.m_ulSteamIDLobby == lobby.m_SteamID && !Hosting) FollowHostState();
        }));
        SetStatus($"Steam connected as {Persona} — mod 0.1.1, App ID {appId}");
    }
    public void Host(MapEntry selected)
    {
        RequireAvailable(); CheckMap(selected);
        map = selected; pending = true; cancelPending = false;
        created.Set(SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypeFriendsOnly, 8));
        SetStatus("Creating friends-only Steam lobby…");
    }
    public void Join(ulong id)
    {
        RequireAvailable(); var target = new CSteamID(id);
        if (!target.IsLobby()) throw new ArgumentException("Enter a valid Steam lobby code.");
        pending = true; cancelPending = false;
        entered.Set(SteamMatchmaking.JoinLobby(target));
        SetStatus("Joining friend's lobby — the host selects the map…");
    }
    private void RequireAvailable()
    {
        if (!initialized || Busy) throw new InvalidOperationException("Leave the current lobby first.");
    }
    private void CheckMap(MapEntry selected)
    {
        if (CanLoadMap?.Invoke(selected) == false) throw new InvalidOperationException("The selected map is unavailable.");
    }
    private void OnCreated(LobbyCreated_t result, bool failure)
    {
        pending = false;
        if (failure || result.m_eResult != EResult.k_EResultOK) { SetStatus("Lobby creation failed: " + result.m_eResult); return; }
        if (cancelPending) { SteamMatchmaking.LeaveLobby(new(result.m_ulSteamIDLobby)); cancelPending = false; SetStatus("Lobby creation cancelled."); return; }
        lobby = new(result.m_ulSteamIDLobby); owner = SteamUser.GetSteamID();
        state = LobbyState.Create(map!, Guid.NewGuid().ToString("N"), 1);
        if (!PublishState(state)) { Leave(); SetStatus("Could not publish the lobby map."); return; }
        RequestTravel(map!); RefreshMembers();
        SetStatus("Hosting — loading selected map…");
    }
    private bool PublishState(LobbyState next)
    {
        // rrc_state is written last, as one atomic metadata value. Guests never combine
        // a new level path with an old hash/epoch from separate metadata callbacks.
        var metadata = new Dictionary<string, string> {
            ["rrc_mod"] = "rowerc", ["rrc_protocol"] = Protocol.Version.ToString(),
            ["rrc_build"] = MapCatalog.SupportedBuild, ["rrc_version"] = "0.1.1",
            ["name"] = Persona + "'s RC Tandems" };
        foreach (var (key, value) in metadata) if (!SteamMatchmaking.SetLobbyData(lobby, key, value)) return false;
        var result = SteamMatchmaking.SetLobbyData(lobby, "rrc_state", next.ToJson());
        if (result) log.Write($"Published map epoch={next.Epoch} level={next.Level} content={next.Content}");
        return result;
    }
    private void OnEntered(LobbyEnter_t result, bool failure)
    {
        pending = false; var joined = new CSteamID(result.m_ulSteamIDLobby);
        if (failure || result.m_EChatRoomEnterResponse != (uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess)
        { SetStatus("Could not join lobby: " + result.m_EChatRoomEnterResponse); return; }
        if (cancelPending) { SteamMatchmaking.LeaveLobby(joined); cancelPending = false; SetStatus("Lobby join cancelled."); return; }
        var next = LobbyState.Read(SteamMatchmaking.GetLobbyData(joined, "rrc_state"));
        if (next == null)
        {
            SteamMatchmaking.LeaveLobby(joined);
            SetStatus("Host has an incompatible mod. Both players must update to RoweRC 0.1.1 or later."); return;
        }
        var selected = findMap(next.Content);
        if (selected == null || !next.SameMap(selected))
        {
            SteamMatchmaking.LeaveLobby(joined);
            SetStatus($"Host's map '{next.MapId}' is missing or a different version. Install that map, then join again."); return;
        }
        try { CheckMap(selected); }
        catch (Exception e) { SteamMatchmaking.LeaveLobby(joined); SetStatus("Host's map is unavailable: " + e.Message); return; }
        lobby = joined; owner = SteamMatchmaking.GetLobbyOwner(lobby); state = next; map = selected;
        RequestTravel(selected); RefreshMembers();
        SetStatus("Joined — automatically loading host's map: " + selected.Manifest.Name);
    }
    public void ChangeMap(MapEntry selected)
    {
        if (!Hosting || state == null) throw new InvalidOperationException("Only the host can change the lobby map.");
        CheckMap(selected);
        var next = LobbyState.Create(selected, state.Room, state.Epoch + 1);
        if (!PublishState(next)) throw new InvalidOperationException("Steam could not publish the map change.");
        state = next; map = selected; ResetPeersForMap(); RequestTravel(selected);
    }
    private void FollowHostState()
    {
        if (!InLobby || Hosting) return;
        var next = LobbyState.Read(SteamMatchmaking.GetLobbyData(lobby, "rrc_state"));
        if (next == null || next == state) return;
        if (state != null && (next.Room != state.Room || next.Epoch <= state.Epoch)) return;
        var selected = findMap(next.Content);
        if (selected == null || !next.SameMap(selected))
        {
            Leave(); SetStatus($"Host changed to '{next.MapId}'. Install that map version and join again."); return;
        }
        try { CheckMap(selected); }
        catch (Exception e) { Leave(); SetStatus("Host changed maps: " + e.Message); return; }
        state = next; map = selected; ResetPeersForMap(); RequestTravel(selected);
        log.Write($"Following host map epoch={next.Epoch} level={next.Level}");
    }
    private void ResetPeersForMap()
    {
        var ids = peers.Keys.ToArray(); peers.Clear();
        foreach (var id in ids) peers[id] = new(id, Environment.TickCount64);
        bridge.WriteRemotes([]); lastFrame = default; localProblem = "";
    }
    private void RequestTravel(MapEntry selected)
    {
        requestedMap = selected; mapRequestedAt = Environment.TickCount64; lastFollow = mapRequestedAt;
        bridge.OpenMap(selected.Manifest.Level);
    }
    public void Tick()
    {
        if (!initialized) return;
        SteamAPI.RunCallbacks();
        var now = Environment.TickCount64;
        if (!InLobby || state == null) { SteamTransport.Receive((_, _, _) => { }); return; }
        if (SteamMatchmaking.GetLobbyOwner(lobby) != owner)
        { Leave(); SetStatus("Host left. Create or join a new lobby."); return; }
        if (now - lastMembership > 500)
        {
            RefreshMembers(); if (!Hosting) FollowHostState(); lastMembership = now;
            if (!InLobby || state == null) return;
        }
        var local = bridge.Read();
        var ready = local?.Level == state.Level;
        // Establish and receive peer connections even while either game is loading.
        SteamTransport.Receive((id, bytes, route) => Receive(id, bytes, route, now));
        foreach (var peer in peers.Values)
        {
            if (now - peer.LastHello > 750)
            {
                var hello = PeerEnvelope.Hello(state, local).Encode();
                Send(peer, hello, PeerRoute.Messages, true);
                if (peer.TryLegacy(now)) Send(peer, hello, PeerRoute.Legacy, true);
                peer.LastHello = now;
            }
        }
        if (!ready)
        {
            bridge.WriteRemotes([]);
            localProblem = local == null ? "Waiting for live game data" : "Loading host map: " + map!.Manifest.Name;
            if (local != null && now - lastFollow > 3000)
            {
                // A guest returning to a different native track is brought back to the host.
                // A host using the native track picker publishes that choice to the guests.
                var nativeSelection = Hosting && requestedMap == null ? findLevel?.Invoke(local.Level) : null;
                if (nativeSelection != null) ChangeMap(nativeSelection);
                else { bridge.OpenMap(state.Level); lastFollow = now; }
            }
            UpdateStatus(now); LogSnapshot(local, now); return;
        }
        requestedMap = null; localProblem = "";
        if (now - lastSend >= 50 && local != null && lastFrame != (local.Run, local.Sequence))
        {
            var bytes = PeerEnvelope.Pose(state, local).Encode();
            foreach (var peer in peers.Values) Send(peer, bytes, peer.Route(now), false);
            lastSend = now; lastFrame = (local.Run, local.Sequence);
        }
        bridge.WriteRemotes(peers.Values.Where(p => p.HasCar(now)).Select(p => new RemoteCar(p.Id,
            SteamFriends.GetFriendPersonaName(new(p.Id)), p.Car!, p.CarReceivedAt)));
        UpdateStatus(now); LogSnapshot(local, now);
    }
    private void Send(PeerLink peer, byte[] bytes, PeerRoute route, bool reliable)
    {
        var result = SteamTransport.Send(peer.Id, bytes, route, reliable);
        if (result != peer.SendError) log.Write($"peer={peer.Id} route={route} send={(result.Length == 0 ? "OK" : result)}");
        peer.SendError = result;
    }
    private void Receive(ulong id, byte[] bytes, PeerRoute route, long now)
    {
        if (state == null || !peers.TryGetValue(id, out var peer)) return;
        var budget = rate.GetValueOrDefault(id);
        if (now - budget.Window >= 1000) budget = (now, 0);
        rate[id] = (budget.Window, budget.Count + 1);
        if (budget.Count > 80) return;
        var packet = PeerEnvelope.Read(bytes, state);
        if (packet == null) { if (budget.Count == 0) log.Write($"Rejected packet peer={id} route={route} size={bytes.Length}"); return; }
        if (peer.PacketsReceived == 0) log.Write($"First peer packet peer={id} route={route} kind={packet.Kind} ready={packet.Ready} level={packet.Level}");
        peer.Accept(packet, route, now);
    }
    private void UpdateStatus(long now)
    {
        var prefix = Hosting ? "Hosting" : "Joined";
        var count = peers.Values.Count(p => p.HasCar(now));
        var result = $"{prefix} — {peers.Count} friend(s), {count} cars receiving";
        if (localProblem.Length > 0) result += ". " + localProblem;
        else if (peers.Values.FirstOrDefault(p => p.SendError.Length > 0) is { } bad) result += ". " + bad.SendError;
        else if (peers.Count > 0 && count == 0) result += ". " + PeerDescription(peers.Values.First());
        SetStatus(result);
    }
    private string PeerDescription(PeerLink peer)
    {
        var now = Environment.TickCount64;
        var name = SteamFriends.GetFriendPersonaName(new(peer.Id));
        if (peer.HasCar(now)) return $"{name}: car connected ({peer.Route(now)})";
        if (peer.PacketsReceived > 0 && !peer.Ready) return $"{name}: loading host's map";
        if (peer.SendError.Length > 0) return name + ": " + peer.SendError;
        if (peer.PacketsReceived > 0) return name + ": connection ready, waiting for car updates";
        return name + (peer.TryLegacy(now) ? ": retrying Steam P2P connection" : ": connecting Steam P2P");
    }
    private void LogSnapshot(CarFrame? local, long now)
    {
        if (now - lastLog < 5000) return;
        lastLog = now;
        var relay = SteamNetworkingUtils.GetRelayNetworkStatus(out var relayInfo);
        log.Write($"lobby={Code} role={Role} wanted={state?.Level} local={local?.Level ?? "none"} bridgeError={bridge.Error} relay={relay} networkError={networkFailure}");
        foreach (var peer in peers.Values)
        {
            var identity = new SteamNetworkingIdentity(); identity.SetSteamID64(peer.Id);
            var connection = SteamNetworkingMessages.GetSessionConnectionInfo(ref identity, out var info, out _);
            log.Write($"peer={peer.Id} packets={peer.PacketsReceived} poses={peer.PosesReceived} ready={peer.Ready} level={peer.Level} route={peer.Route(now)} modernState={connection} end={info.m_eEndReason} {info.m_szEndDebug}");
        }
    }
    private void RefreshMembers()
    {
        var ids = new HashSet<ulong>();
        if (InLobby)
            for (var i = 0; i < Math.Min(8, SteamMatchmaking.GetNumLobbyMembers(lobby)); i++)
            {
                var id = SteamMatchmaking.GetLobbyMemberByIndex(lobby, i);
                if (id.IsValid() && id != SteamUser.GetSteamID()) ids.Add(id.m_SteamID);
            }
        foreach (var id in peers.Keys.Except(ids).ToArray()) { SteamTransport.Close(id); peers.Remove(id); rate.Remove(id); log.Write("Peer left: " + id); }
        foreach (var id in ids.Except(peers.Keys).ToArray()) { peers[id] = new(id, Environment.TickCount64); log.Write("Peer joined: " + id); }
    }
    public void Leave()
    {
        if (pending) { cancelPending = true; SetStatus("Cancelling lobby request…"); return; }
        if (InLobby) SteamMatchmaking.LeaveLobby(lobby);
        lobby = CSteamID.Nil; RefreshMembers(); peers.Clear(); rate.Clear(); map = null; state = null; requestedMap = null;
        localProblem = networkFailure = ""; bridge.WriteRemotes([]);
        SetStatus(initialized ? "Steam connected — lobby left" : "Steam disconnected");
    }
    public void Dispose()
    {
        if (!initialized) return;
        Leave(); foreach (var c in callbacks) c.Dispose(); created.Dispose(); entered.Dispose();
        SteamAPI.Shutdown(); initialized = false;
    }
}
