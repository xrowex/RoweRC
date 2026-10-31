using System.Runtime.InteropServices;
using Steamworks;

namespace RoweRC;

public static class SteamTransport
{
    public static string Send(ulong peer, byte[] bytes, PeerRoute route, bool reliable)
    {
        if (route == PeerRoute.Legacy)
            return SteamNetworking.SendP2PPacket(new(peer), bytes, (uint)bytes.Length,
                reliable ? EP2PSend.k_EP2PSendReliable : EP2PSend.k_EP2PSendUnreliable, Protocol.Channel) ? "" : "Steam P2P send failed";
        var identity = new SteamNetworkingIdentity(); identity.SetSteamID64(peer);
        var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try
        {
            var result = SteamNetworkingMessages.SendMessageToUser(ref identity, handle.AddrOfPinnedObject(), (uint)bytes.Length,
                (reliable ? Constants.k_nSteamNetworkingSend_ReliableNoNagle : Constants.k_nSteamNetworkingSend_UnreliableNoNagle)
                    | Constants.k_nSteamNetworkingSend_AutoRestartBrokenSession, Protocol.Channel);
            return result == EResult.k_EResultOK ? "" : "Steam Messages: " + result;
        }
        finally { handle.Free(); }
    }
    public static void Receive(Action<ulong, byte[], PeerRoute> receive)
    {
        var pointers = new IntPtr[32];
        var count = SteamNetworkingMessages.ReceiveMessagesOnChannel(Protocol.Channel, pointers, pointers.Length);
        for (var i = 0; i < count; i++)
        {
            try
            {
                var message = SteamNetworkingMessage_t.FromIntPtr(pointers[i]);
                if (message.m_cbSize is < 1 or > Protocol.MaxPacket) continue;
                var bytes = new byte[message.m_cbSize]; Marshal.Copy(message.m_pData, bytes, 0, bytes.Length);
                receive(message.m_identityPeer.GetSteamID64(), bytes, PeerRoute.Messages);
            }
            finally { SteamNetworkingMessage_t.Release(pointers[i]); }
        }
        for (var i = 0; i < 32 && SteamNetworking.IsP2PPacketAvailable(out var size, Protocol.Channel); i++)
        {
            // Steam caps legacy packets at 1 MiB. Drain oversized packets without parsing them.
            if (size > 1048576) break;
            var bytes = new byte[size];
            if (!SteamNetworking.ReadP2PPacket(bytes, size, out var read, out var sender, Protocol.Channel)) break;
            if (read is > 0 and <= Protocol.MaxPacket) receive(sender.m_SteamID, bytes[..(int)read], PeerRoute.Legacy);
        }
    }
    public static void Close(ulong peer)
    {
        var identity = new SteamNetworkingIdentity(); identity.SetSteamID64(peer);
        SteamNetworkingMessages.CloseSessionWithUser(ref identity);
        SteamNetworking.CloseP2PSessionWithUser(new(peer));
    }
}
