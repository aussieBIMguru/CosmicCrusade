using System.Collections.ObjectModel;

namespace CosmicCrusade.App.ViewModels;

/// <summary>Everything the side panel shows. The GameView updates it as events arrive.</summary>
public sealed class GameHudViewModel : ObservableObject
{
    private string _roundText = "";
    private string _status = "";
    private int _myTotal;
    private int _aiTotal;
    private int _myRounds;
    private int _aiRounds;
    private int _roundsToWin = 3;
    private bool _canPass;
    private string _hint = "";
    private bool _isMyTurn;
    private string _passText = "PASS";
    private string _handRevealNote = "";
    private string _netStatus = "";
    private bool _netOk = true;

    public string PlayerName { get; init; } = "You";
    public string AiName { get; init; } = "AI";

    public string RoundText { get => _roundText; set => Set(ref _roundText, value); }
    public string Status { get => _status; set => Set(ref _status, value); }
    public string Hint { get => _hint; set => Set(ref _hint, value); }
    public bool IsMyTurn { get => _isMyTurn; set => Set(ref _isMyTurn, value); }

    /// <summary>"Your hand is revealed to the AI" while The All-knowing / Transparency / Oracle's Arch shows it.</summary>
    public string HandRevealNote { get => _handRevealNote; set => Set(ref _handRevealNote, value); }

    public int MyTotal { get => _myTotal; set { if (Set(ref _myTotal, value)) Raise(nameof(Leader)); } }
    public int AiTotal { get => _aiTotal; set { if (Set(ref _aiTotal, value)) Raise(nameof(Leader)); } }

    /// <summary>-1 AI ahead, 0 level, 1 you ahead (drives the score colours).</summary>
    public int Leader => Math.Sign(MyTotal - AiTotal);

    public int MyRounds { get => _myRounds; set { if (Set(ref _myRounds, value)) Raise(nameof(MyPips)); } }
    public int AiRounds { get => _aiRounds; set { if (Set(ref _aiRounds, value)) Raise(nameof(AiPips)); } }
    public int RoundsToWin
    {
        get => _roundsToWin;
        set { if (Set(ref _roundsToWin, value)) { Raise(nameof(MyPips)); Raise(nameof(AiPips)); } }
    }

    public string MyPips => Pips(MyRounds);
    public string AiPips => Pips(AiRounds);

    public bool CanPass { get => _canPass; set => Set(ref _canPass, value); }

    /// <summary>"PASS" on a turn, "OK – NO RESPONSE" in a priority window.</summary>
    public string PassText { get => _passText; set => Set(ref _passText, value); }

    public ObservableCollection<string> Log { get; } = new();

    // ------------------------------------------------------------------ online play

    /// <summary>True in an online game: shows the chat box and the connection line.</summary>
    public bool IsOnline { get; init; }

    /// <summary>"Connected to Sam" / "Sam's connection lost – waiting…" / "Reconnecting to the server…".</summary>
    public string NetStatus { get => _netStatus; set => Set(ref _netStatus, value); }

    /// <summary>False while something is wrong with the connection (the status line turns amber).</summary>
    public bool NetOk { get => _netOk; set => Set(ref _netOk, value); }

    /// <summary>Chat lines, oldest first ("Sam: gg").</summary>
    public ObservableCollection<ChatLine> Chat { get; } = new();

    public void AddChat(string name, string text, bool mine)
    {
        Chat.Add(new ChatLine(name, text, mine));
        while (Chat.Count > 100) Chat.RemoveAt(0);
    }

    public void AddLog(string line)
    {
        Log.Insert(0, line);
        while (Log.Count > 200) Log.RemoveAt(Log.Count - 1);
    }

    private string Pips(int won) =>
        new string('★', Math.Min(won, RoundsToWin)) + new string('☆', Math.Max(0, RoundsToWin - won));
}

/// <summary>One chat line. <see cref="Mine"/> picks the colour (blue for you, red for the opponent, grey for notices).</summary>
public sealed record ChatLine(string Name, string Text, bool Mine, bool Notice = false)
{
    public string Display => Notice ? Text : $"{Name}: {Text}";
}
