using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using CosmicCrusade.Core.Model;

namespace CosmicCrusade.Core.Net;

/// <summary>
/// Online play (docs/players/PLAYING-ONLINE.md, docs/design/multiplayer.md).
///
/// Lockstep: both apps run the same engine from the same options and seed. The only thing sent is each player's own
/// decisions, as indices into the legal-action / choice-option lists that both engines compute identically. A small
/// relay (server/cloudflare, or src/CosmicCrusade.Relay locally) pairs the two apps under a room code and forwards text
/// messages; it knows nothing about the game.
/// </summary>
public static class NetProtocol
{
    /// <summary>Relay protocol (the <c>proto=</c> query parameter). Must match server/cloudflare/src/index.js.</summary>
    public const int RelayVersion = 1;

    /// <summary>
    /// Game protocol + rules version. Both players must have the same one. <b>Bump it whenever the engine's rules
    /// change</b> (a different rule on each side would desync the game).
    /// </summary>
    public const int GameVersion = 1;

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Serialize(NetMessage m) => JsonSerializer.Serialize(m, Json);

    /// <summary>Parses a message; null for anything that isn't a JSON object with a type.</summary>
    public static NetMessage? Parse(string text)
    {
        if (text.Length == 0 || text[0] != '{') return null;
        try
        {
            var m = JsonSerializer.Deserialize<NetMessage>(text, Json);
            return string.IsNullOrEmpty(m?.T) ? null : m;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// "wss://name.workers.dev/room/ABCDE?role=host&amp;key=...&amp;proto=1". Accepts what people paste in the Server
    /// box: a bare host name (wss assumed), http(s):// (mapped to ws(s)://) or ws(s)://, with or without a trailing slash.
    /// </summary>
    public static Uri RoomUri(string server, string code, string role, string key)
    {
        string s = server.Trim().TrimEnd('/');
        if (s.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) s = "wss://" + s[8..];
        else if (s.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) s = "ws://" + s[7..];
        else if (!s.StartsWith("ws://", StringComparison.OrdinalIgnoreCase) && !s.StartsWith("wss://", StringComparison.OrdinalIgnoreCase))
            s = "wss://" + s;
        return new Uri($"{s}/room/{Uri.EscapeDataString(code)}?role={role}&key={Uri.EscapeDataString(key)}&proto={RelayVersion}");
    }

    private const string CodeAlphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789"; // no 0/O, 1/I/L

    /// <summary>A 5-character room code that is easy to read out (no 0/O or 1/I/L).</summary>
    public static string NewRoomCode() =>
        new(Enumerable.Range(0, 5).Select(_ => CodeAlphabet[RandomNumberGenerator.GetInt32(CodeAlphabet.Length)]).ToArray());

    /// <summary>A private per-connection secret; lets the same app reconnect into its seat after a drop.</summary>
    public static string NewKey() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(18)).Replace('+', '-').Replace('/', '_').TrimEnd('=');

    /// <summary>Upper-cases a typed code and drops spaces/dashes ("abc-de" → "ABCDE").</summary>
    public static string NormaliseCode(string typed) =>
        new(typed.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    /// <summary>
    /// Fingerprint of the card data (cards.json text, line endings normalised), so both players are known to play
    /// with the same cards.
    /// </summary>
    public static string DataFingerprint(string cardsJsonText)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(cardsJsonText.Replace("\r\n", "\n"));
        return Convert.ToHexString(SHA256.HashData(bytes))[..16];
    }
}

/// <summary>
/// Every message on the wire, flattened into one shape (unused fields are left out of the JSON).
/// <list type="table">
/// <item><term>relay</term><description>From the relay: <c>ev</c> = joined (with <c>role</c>, <c>peer</c>) / peer-joined / peer-left / error (<c>msg</c>).</description></item>
/// <item><term>hello</term><description>Guest → host: <c>ver</c>, <c>name</c>, <c>data</c> (card data fingerprint).</description></item>
/// <item><term>start</term><description>Host → guest: <c>ver</c>, <c>options</c>, <c>hostName</c>, <c>guestName</c>.</description></item>
/// <item><term>reject</term><description>Host → guest: <c>reason</c> (different version or cards).</description></item>
/// <item><term>d</term><description>A decision: <c>n</c> (1, 2, 3... per sender), <c>k</c> (a = turn action, r = React window, c = ability choice), <c>v</c> (indices; -1 = no response), <c>h</c> (state checksum before deciding).</description></item>
/// <item><term>resume</term><description><c>have</c> = decisions received so far; the other side re-sends anything after that.</description></item>
/// <item><term>hb</term><description>Heartbeat, every few seconds: <c>w</c> = waiting for the other player, <c>n</c> = decisions sent, <c>have</c> = decisions received (spots two games stuck waiting for each other).</description></item>
/// <item><term>chat</term><description><c>name</c>, <c>text</c>.</description></item>
/// <item><term>bye</term><description>Left the game (<c>reason</c>).</description></item>
/// <item><term>desync</term><description>The sender's game no longer matches (both games stop).</description></item>
/// </list>
/// </summary>
public sealed class NetMessage
{
    public string T { get; set; } = "";

    // relay
    public string? Ev { get; set; }
    public string? Msg { get; set; }
    public string? Role { get; set; }
    public bool? Peer { get; set; }

    // lobby
    public int? Ver { get; set; }
    public string? Name { get; set; }
    public string? Data { get; set; }
    public NetOptions? Options { get; set; }
    public string? HostName { get; set; }
    public string? GuestName { get; set; }
    public string? Reason { get; set; }

    // decisions
    public int? N { get; set; }
    public string? K { get; set; }
    public int[]? V { get; set; }
    public uint? H { get; set; }
    public int? Have { get; set; }
    public bool? W { get; set; }

    // chat
    public string? Text { get; set; }
}

/// <summary>The game options the host sends (everything that affects the rules or the shuffle).</summary>
public sealed class NetOptions
{
    public int RoundsToWin { get; set; }
    public EmptyDeckRule EmptyDeck { get; set; }
    public bool Warzones { get; set; }
    public bool Archons { get; set; }
    public bool Spacecraft { get; set; }
    public bool Leaders { get; set; }
    public DeckMode DeckMode { get; set; }
    public int Seed { get; set; }
    public int HandUnitTarget { get; set; }
    public int HandOrderTarget { get; set; }

    /// <summary>The host's turn clock (seconds, 0 = off). Null from a host built before the clock: no clock.</summary>
    public int? TurnSeconds { get; set; }
    public Dictionary<string, int> CopyCounts { get; set; } = new();
    public List<string> LeaderRelatedCards { get; set; } = new();

    public static NetOptions From(GameOptions o) => new()
    {
        RoundsToWin = o.RoundsToWin,
        EmptyDeck = o.EmptyDeck,
        Warzones = o.Warzones,
        Archons = o.Archons,
        Spacecraft = o.Spacecraft,
        Leaders = o.Leaders,
        DeckMode = o.DeckMode,
        Seed = o.Seed,
        HandUnitTarget = o.HandUnitTarget,
        HandOrderTarget = o.HandOrderTarget,
        TurnSeconds = o.TurnSeconds,
        CopyCounts = o.CopyCounts.OrderBy(kv => kv.Key, StringComparer.Ordinal).ToDictionary(kv => kv.Key, kv => kv.Value),
        LeaderRelatedCards = o.LeaderRelatedCards.OrderBy(x => x, StringComparer.Ordinal).ToList(),
    };

    public GameOptions ToGameOptions()
    {
        var o = new GameOptions
        {
            RoundsToWin = RoundsToWin,
            EmptyDeck = EmptyDeck,
            Warzones = Warzones,
            Archons = Archons,
            Spacecraft = Spacecraft,
            Leaders = Leaders,
            DeckMode = DeckMode,
            Seed = Seed,
            HandUnitTarget = HandUnitTarget,
            HandOrderTarget = HandOrderTarget,
            TurnSeconds = TurnSeconds ?? 0,
        };
        foreach (var kv in CopyCounts) o.CopyCounts[kv.Key] = kv.Value;
        foreach (var id in LeaderRelatedCards) o.LeaderRelatedCards.Add(id);
        return o;
    }
}

/// <summary>The relay refused the connection (bad code, room full, version...). Not worth retrying.</summary>
public sealed class NetRefusedException(string message) : Exception(message);

/// <summary>Something went wrong setting up or running an online game; the message is for the player.</summary>
public sealed class NetException(string message) : Exception(message);

/// <summary>The opponent left, or the connection was lost for good.</summary>
public sealed class OpponentLeftException(string message) : Exception(message);

/// <summary>The two games no longer match (different rules or data on each side). Both games stop.</summary>
public sealed class DesyncException(string message) : Exception(message);
