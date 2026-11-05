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

    public string PlayerName { get; init; } = "You";
    public string AiName { get; init; } = "AI";

    public string RoundText { get => _roundText; set => Set(ref _roundText, value); }
    public string Status { get => _status; set => Set(ref _status, value); }
    public string Hint { get => _hint; set => Set(ref _hint, value); }
    public bool IsMyTurn { get => _isMyTurn; set => Set(ref _isMyTurn, value); }

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

    public void AddLog(string line)
    {
        Log.Insert(0, line);
        while (Log.Count > 200) Log.RemoveAt(Log.Count - 1);
    }

    private string Pips(int won) =>
        new string('★', Math.Min(won, RoundsToWin)) + new string('☆', Math.Max(0, RoundsToWin - won));
}
