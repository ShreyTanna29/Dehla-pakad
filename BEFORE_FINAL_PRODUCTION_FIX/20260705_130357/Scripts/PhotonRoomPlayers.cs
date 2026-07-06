using System.Collections.Generic;
using Photon.Pun;
using Photon.Realtime;

/// <summary>
/// Safe room player iteration — avoids PhotonNetwork.PlayerList LINQ when room.Players is null.
/// </summary>
public static class PhotonRoomPlayers
{
    public static Player[] GetSorted()
    {
        if (!PhotonNetwork.InRoom) return System.Array.Empty<Player>();

        Room room = PhotonNetwork.CurrentRoom;
        if (room == null || room.Players == null || room.Players.Count == 0)
            return System.Array.Empty<Player>();

        var players = new List<Player>(room.Players.Count);
        foreach (KeyValuePair<int, Player> kvp in room.Players)
        {
            if (kvp.Value != null)
                players.Add(kvp.Value);
        }

        if (players.Count == 0) return System.Array.Empty<Player>();

        players.Sort((a, b) => a.ActorNumber.CompareTo(b.ActorNumber));
        return players.ToArray();
    }

    public static int CountActiveHumans()
    {
        if (!PhotonNetwork.InRoom)
            return PhotonNetwork.OfflineMode ? 1 : 0;

        int count = 0;
        foreach (Player p in GetSorted())
        {
            if (p != null && !p.IsInactive)
                count++;
        }
        return count;
    }
}
