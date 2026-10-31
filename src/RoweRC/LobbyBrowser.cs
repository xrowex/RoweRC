using Steamworks;

namespace RoweRC;

public sealed record LobbyListing(ulong Id, string Name, string Map, int Players, int Capacity, string Access, string Problem)
{
    public bool CanJoin => Problem.Length == 0;
}

public sealed class LobbyBrowser : IDisposable
{
    private readonly CallResult<LobbyMatchList_t> listed;
    private readonly Callback<LobbyDataUpdate_t> updated;
    private readonly Dictionary<ulong, string> candidates = [];
    private readonly Dictionary<ulong, LobbyListing> rows = [];
    private readonly Func<string, MapEntry?> findMap;
    private long requested;
    private bool searching, publicDone;
    public string Status { get; private set; } = "Refresh to find Steam lobbies";
    public IEnumerable<LobbyListing> Listings => rows.Values.OrderBy(r => r.Access != "Friends").ThenBy(r => r.Name).Take(50);
    public LobbyBrowser(Func<string, MapEntry?> findMap)
    {
        this.findMap = findMap;
        listed = CallResult<LobbyMatchList_t>.Create((result, failed) => {
            searching = false; publicDone = true;
            if (!failed)
                for (var i = 0; i < Math.Min(result.m_nLobbiesMatching, 50); i++)
                {
                    var id = SteamMatchmaking.GetLobbyByIndex(i);
                    candidates.TryAdd(id.m_SteamID, "Public"); Read(id);
                }
            Status = failed ? "Steam search failed; refresh to retry" : $"{rows.Count} RoweRC lobby(s) found";
        });
        updated = Callback<LobbyDataUpdate_t>.Create(r => {
            if (!candidates.ContainsKey(r.m_ulSteamIDLobby)) return;
            if (r.m_bSuccess != 0) Read(new(r.m_ulSteamIDLobby)); else rows.Remove(r.m_ulSteamIDLobby);
            if (publicDone) Status = $"{rows.Count} RoweRC lobby(s) found";
        });
    }
    public void Refresh(uint appId)
    {
        var now = Environment.TickCount64;
        if (searching && now - requested < 22000 || now - requested < 3000) return;
        requested = now; searching = true; publicDone = false; candidates.Clear(); rows.Clear();
        Status = "Searching Steam public and friends' lobbies…";
        // Friends-only rooms never appear in RequestLobbyList.
        for (var i = 0; i < Math.Min(SteamFriends.GetFriendCount(EFriendFlags.k_EFriendFlagImmediate), 2000); i++)
        {
            var friend = SteamFriends.GetFriendByIndex(i, EFriendFlags.k_EFriendFlagImmediate);
            if (!SteamFriends.GetFriendGamePlayed(friend, out var game) || game.m_gameID.AppID().m_AppId != appId || !game.m_steamIDLobby.IsLobby()) continue;
            candidates[game.m_steamIDLobby.m_SteamID] = "Friends";
            SteamMatchmaking.RequestLobbyData(game.m_steamIDLobby);
        }
        SteamMatchmaking.AddRequestLobbyListStringFilter("rrc_mod", "rowerc", ELobbyComparison.k_ELobbyComparisonEqual);
        SteamMatchmaking.AddRequestLobbyListStringFilter("rrc_protocol", Protocol.Version.ToString(), ELobbyComparison.k_ELobbyComparisonEqual);
        SteamMatchmaking.AddRequestLobbyListStringFilter("rrc_build", MapCatalog.SupportedBuild, ELobbyComparison.k_ELobbyComparisonEqual);
        SteamMatchmaking.AddRequestLobbyListDistanceFilter(ELobbyDistanceFilter.k_ELobbyDistanceFilterWorldwide);
        SteamMatchmaking.AddRequestLobbyListResultCountFilter(50);
        listed.Set(SteamMatchmaking.RequestLobbyList());
    }
    private void Read(CSteamID id)
    {
        if (SteamMatchmaking.GetLobbyData(id, "rrc_mod") != "rowerc") return;
        var state = LobbyState.Read(SteamMatchmaking.GetLobbyData(id, "rrc_state"));
        if (state == null) return;
        var map = findMap(state.Content);
        var count = SteamMatchmaking.GetNumLobbyMembers(id); var capacity = SteamMatchmaking.GetLobbyMemberLimit(id);
        var problem = count >= capacity ? "Full" : map == null || !state.SameMap(map) ? "Map missing / different version" : "";
        rows[id.m_SteamID] = new(id.m_SteamID, SteamMatchmaking.GetLobbyData(id, "name"), map?.Manifest.Name ?? state.MapId,
            count, capacity, candidates[id.m_SteamID], problem);
    }
    public void Dispose() { listed.Dispose(); updated.Dispose(); }
}
