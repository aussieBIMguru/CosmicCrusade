namespace CosmicCrusade.App.Board;

/// <summary>
/// Whose eyes the table is drawn through. Against the AI you are always player 0. Online, the host is player 0 and
/// the guest player 1, and each app draws its own player at the bottom: every "is this mine?" check in the UI goes
/// through <see cref="Me"/> / <see cref="Opp"/>, and every mention of the opponent through <see cref="OppName"/> etc.
/// Set once per game by <see cref="Views.GameView"/> before the board is built.
/// </summary>
public static class Perspective
{
    /// <summary>The local player's index in the engine (0 or 1).</summary>
    public static int Me { get; private set; }

    public static int Opp => 1 - Me;

    /// <summary>True during an online game (the opponent is a person).</summary>
    public static bool Online { get; private set; }

    /// <summary>"AI", or the opponent's name online (log lines, banners, labels).</summary>
    public static string OppName { get; private set; } = "AI";

    /// <summary>"the AI" / "Sam" (mid-sentence).</summary>
    public static string TheOpp => Online ? OppName : "the AI";

    /// <summary>"The AI" / "Sam" (start of a sentence).</summary>
    public static string TheOppCap => Online ? OppName : "The AI";

    /// <summary>"the AI's" / "Sam's".</summary>
    public static string OppPossessive => TheOpp + "'s";

    /// <summary>"The AI's" / "Sam's".</summary>
    public static string OppPossessiveCap => TheOppCap + "'s";

    public static bool IsMe(int player) => player == Me;

    /// <summary>Against the AI: you are player 0.</summary>
    public static void SetLocal()
    {
        Me = 0;
        Online = false;
        OppName = "AI";
    }

    /// <summary>Online: <paramref name="mySeat"/> is 0 for the host, 1 for the guest.</summary>
    public static void SetOnline(int mySeat, string opponentName)
    {
        Me = mySeat;
        Online = true;
        OppName = string.IsNullOrWhiteSpace(opponentName) ? "Opponent" : opponentName;
    }
}
