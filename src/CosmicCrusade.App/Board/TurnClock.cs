using System.Diagnostics;

namespace CosmicCrusade.App.Board;

/// <summary>
/// One player's turn clock (Hearthstone-style "rope").
/// <list type="bullet">
/// <item>Time only runs while that player is actually being asked something (a turn, a React window, an ability
/// choice), so animations, the AI thinking and the other player's decisions never use it up.</item>
/// <item>On your own turn, every question shares one budget: the turn action, its targets, and answering a React to
/// it, plus any Open Source extra actions. It refills when your next turn starts.</item>
/// <item>Off your turn (a React window, a choice the opponent's card gives you, Start-up prompts) each question gets
/// the full limit on its own.</item>
/// </list>
/// The clock knows nothing about the game; <c>GameView.TurnClock.cs</c> drives it from engine events and the seats.
/// </summary>
public sealed class TurnClock
{
    private readonly Stopwatch _run = new(); // the question being asked right now (stopped while paused)
    private TimeSpan _banked;                // time already used by earlier questions this turn
    private bool _ownTurn;

    public TurnClock(TimeSpan limit) => Limit = limit < TimeSpan.Zero ? TimeSpan.Zero : limit;

    public TimeSpan Limit { get; }

    /// <summary>False when the clock is off (limit 0).</summary>
    public bool Enabled => Limit > TimeSpan.Zero;

    /// <summary>True while this player is being asked to decide.</summary>
    public bool Waiting { get; private set; }

    public bool Paused { get; private set; }

    public TimeSpan Used => _banked + _run.Elapsed;

    public TimeSpan Remaining
    {
        get
        {
            var left = Limit - Used;
            return left < TimeSpan.Zero ? TimeSpan.Zero : left;
        }
    }

    /// <summary>The clock is on, a question is open and the time is gone.</summary>
    public bool Expired => Enabled && Waiting && Remaining <= TimeSpan.Zero;

    /// <summary>This player's turn starts: a full budget. Extra actions in the same turn keep the one already running.</summary>
    public void StartTurn()
    {
        if (_ownTurn) return;
        _ownTurn = true;
        _banked = TimeSpan.Zero;
        if (Waiting) Restart();
    }

    /// <summary>The turn is over (the other player's turn started, someone passed, or the round ended).</summary>
    public void EndTurn() => _ownTurn = false;

    /// <summary>A question is put to this player.</summary>
    public void BeginWait()
    {
        if (!_ownTurn) _banked = TimeSpan.Zero; // off-turn questions each get the full limit
        Waiting = true;
        Restart();
    }

    /// <summary>The player answered (or the clock answered for them).</summary>
    public void EndWait()
    {
        if (!Waiting) return;
        _banked += _run.Elapsed;
        _run.Reset();
        Waiting = false;
    }

    /// <summary>Stops the time while the table can't be played (reconnecting, the opponent away, game over).</summary>
    public void SetPaused(bool paused)
    {
        if (Paused == paused) return;
        Paused = paused;
        if (paused) _run.Stop();
        else if (Waiting) _run.Start();
    }

    private void Restart()
    {
        _run.Reset();
        if (!Paused) _run.Start();
    }
}
