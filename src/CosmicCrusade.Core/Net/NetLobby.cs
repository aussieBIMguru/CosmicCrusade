using CosmicCrusade.Core.Model;

namespace CosmicCrusade.Core.Net;

/// <summary>What both players agreed on before the first card is dealt.</summary>
/// <param name="MySeat">0 for the host, 1 for the guest.</param>
public sealed record NetGameSetup(GameOptions Options, int MySeat, string HostName, string GuestName)
{
    public string MyName => MySeat == 0 ? HostName : GuestName;
    public string OpponentName => MySeat == 0 ? GuestName : HostName;
}

/// <summary>
/// The handshake after both players are connected to a room:
/// guest → hello (version, name, card data) · host → start (options incl. the seed, both names).
/// The guest repeats its hello every few seconds until it gets the start, and the host's connection answers every
/// later hello with the same start (<see cref="NetConnection.HelloReply"/>), so a drop during the handshake can't
/// leave one side waiting.
/// </summary>
public static class NetLobby
{
    public static readonly TimeSpan HelloRepeat = TimeSpan.FromSeconds(3);

    /// <summary>Host: waits for a guest, checks it matches and sends the options.</summary>
    public static async Task<NetGameSetup> HostAsync(NetConnection net, GameOptions options, string myName, string dataFingerprint, CancellationToken ct)
    {
        var start = new NetMessage
        {
            T = "start",
            Ver = NetProtocol.GameVersion,
            Options = NetOptions.From(options),
            HostName = myName,
        };
        while (true)
        {
            var m = await net.NextLobbyAsync(ct);
            switch (m.T)
            {
                case "hello":
                {
                    string? problem =
                        m.Ver != NetProtocol.GameVersion
                            ? $"{Display(m.Name)} has a different version of Cosmic Crusade (game version {m.Ver?.ToString() ?? "?"}, yours is {NetProtocol.GameVersion}). You both need the same build."
                        : m.Data != dataFingerprint
                            ? $"{Display(m.Name)} has different card data (cards.json). You both need the same data folder."
                        : null;
                    if (problem is not null)
                    {
                        net.SendLobby(new NetMessage { T = "reject", Reason = problem });
                        throw new NetException(problem);
                    }
                    string guestName = UniqueName(Display(m.Name), myName);
                    start.GuestName = guestName;
                    net.HelloReply = start;
                    net.SendLobby(start);
                    return new NetGameSetup(options, 0, myName, guestName);
                }
            }
        }
    }

    /// <summary>Guest: introduces itself and waits for the host's options.</summary>
    public static async Task<NetGameSetup> JoinAsync(NetConnection net, string myName, string dataFingerprint, CancellationToken ct)
    {
        var hello = new NetMessage { T = "hello", Ver = NetProtocol.GameVersion, Name = myName, Data = dataFingerprint };
        net.SendLobby(hello);

        while (true)
        {
            NetMessage m;
            using (var wait = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                wait.CancelAfter(HelloRepeat);
                try
                {
                    m = await net.NextLobbyAsync(wait.Token);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    net.SendLobby(hello); // no answer yet (host reconnecting?): say it again
                    continue;
                }
            }

            switch (m.T)
            {
                case "reject":
                    throw new NetException(m.Reason ?? "The host turned the game down.");
                case "start":
                    if (m.Ver != NetProtocol.GameVersion || m.Options is null)
                        throw new NetException($"The host has a different version of Cosmic Crusade (game version {m.Ver?.ToString() ?? "?"}, yours is {NetProtocol.GameVersion}).");
                    return new NetGameSetup(m.Options.ToGameOptions(), 1, Display(m.HostName), m.GuestName ?? myName);
            }
        }
    }

    /// <summary>Trims a player name to something sensible for banners and the log.</summary>
    public static string Display(string? name)
    {
        string n = new string((name ?? "").Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (n.Length > 20) n = n[..20].Trim();
        return n.Length == 0 ? "Player" : n;
    }

    private static string UniqueName(string guest, string host) =>
        string.Equals(guest, host, StringComparison.OrdinalIgnoreCase) ? guest + " 2" : guest;
}
