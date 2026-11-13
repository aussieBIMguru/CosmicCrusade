using CosmicCrusade.App.Controls;
using CosmicCrusade.Core.Engine;
using CosmicCrusade.Core.Model;

namespace CosmicCrusade.App.Board;

/// <summary>
/// The human seat. The engine awaits <see cref="ChooseActionAsync"/>; the UI raises <see cref="DecisionRequested"/>,
/// lets the player click, and calls <see cref="Submit"/> with one of the legal actions.
/// </summary>
public sealed class HumanController : IPlayerController
{
    private TaskCompletionSource<GameAction>? _pending;

    public event Action<DecisionContext>? DecisionRequested;
    public event Action? DecisionCompleted;

    public DecisionContext? Current { get; private set; }

    /// <summary>
    /// Auto-allow toggle (spec §7.2). True: stop and ask whenever one of my Reacts can be issued.
    /// False: always answer "OK, no response" without asking.
    /// </summary>
    public bool PauseForReacts { get; set; } = true;

    /// <summary>Skip windows where every available React would only answer one of my own items.</summary>
    public bool IgnoreOwnItems { get; set; } = true;

    public Task<GameAction> ChooseActionAsync(DecisionContext context, CancellationToken ct) => AskAsync(context, ct);

    /// <summary>Priority window. Only called when a React is available; returns null for "OK, no response".</summary>
    public async Task<GameAction?> ChooseResponseAsync(DecisionContext context, CancellationToken ct)
    {
        if (!PauseForReacts) return null;
        // Don't stop to ask about answering your own plays (rarely useful; you can still React to the AI's response).
        if (IgnoreOwnItems && context.Legal.OfType<ReactAction>().All(r => r.RespondingTo.Controller == context.Player))
            return null;
        var action = await AskAsync(context, ct);
        return action is PassAction ? null : action;
    }

    private Task<GameAction> AskAsync(DecisionContext context, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<GameAction>(TaskCreationOptions.RunContinuationsAsynchronously);
        ct.Register(() => tcs.TrySetCanceled(ct));
        _pending = tcs;
        Current = context;
        DecisionRequested?.Invoke(context);
        return tcs.Task;
    }

    public bool IsWaiting => _pending is not null;

    /// <summary>
    /// The turn clock ran out: answers whatever is being asked with the safe default. A turn passes ("END TURN" during an
    /// Open Source extra action), a React window answers "no response", an ability choice takes its default pick.
    /// Online this goes through <c>LocalSeat</c> like any other decision, so both games stay in step.
    /// Returns what was answered, or null if nothing was being asked.
    /// </summary>
    public TimeoutAnswer? AnswerOnTimeout()
    {
        if (_pendingChoice is not null && CurrentChoice is { } req)
        {
            var pick = req.DefaultPick();
            if (!req.IsValid(pick)) return null;
            SubmitChoice(pick);
            return TimeoutAnswer.Choice;
        }
        if (_pending is not null && Current is { } ctx && ctx.Legal.OfType<PassAction>().FirstOrDefault() is { } pass)
        {
            var kind = ctx.IsResponse ? TimeoutAnswer.NoResponse : ctx.ExtraAction ? TimeoutAnswer.EndTurn : TimeoutAnswer.Pass;
            Submit(pass);
            return kind;
        }
        return null;
    }

    // ------------------------------------------------------------------ ability choices

    private TaskCompletionSource<IReadOnlyList<int>>? _pendingChoice;

    /// <summary>Raised when an ability needs a choice from the human (targets, nodes, yes/no...).</summary>
    public event Action<ChoiceRequest>? ChoiceRequested;
    public event Action? ChoiceCompleted;

    public ChoiceRequest? CurrentChoice { get; private set; }

    public Task<IReadOnlyList<int>> ChooseAsync(ChoiceRequest request, GameState state, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<IReadOnlyList<int>>(TaskCreationOptions.RunContinuationsAsynchronously);
        ct.Register(() => tcs.TrySetCanceled(ct));
        _pendingChoice = tcs;
        CurrentChoice = request;
        ChoiceRequested?.Invoke(request);
        return tcs.Task;
    }

    /// <summary>Answers the current choice. Ignored unless the answer is legal.</summary>
    public void SubmitChoice(IReadOnlyList<int> picks)
    {
        var tcs = _pendingChoice;
        var req = CurrentChoice;
        if (tcs is null || req is null || !req.IsValid(picks)) return;
        _pendingChoice = null;
        CurrentChoice = null;
        ChoiceCompleted?.Invoke();
        tcs.TrySetResult(picks);
    }

    public void Submit(GameAction action)
    {
        var tcs = _pending;
        if (tcs is null || Current is null || !Current.Legal.Contains(action)) return;
        _pending = null;
        Current = null;
        DecisionCompleted?.Invoke();
        tcs.TrySetResult(action);
    }
}

/// <summary>What <see cref="HumanController.AnswerOnTimeout"/> did.</summary>
public enum TimeoutAnswer
{
    Pass,
    EndTurn,
    NoResponse,
    Choice,
}

/// <summary>Wraps an AI so its moves are paced for a human to follow.</summary>
public sealed class PacedController : IPlayerController
{
    private readonly IPlayerController _inner;
    private readonly int _thinkMs;

    public PacedController(IPlayerController inner, int thinkMs = 650)
    {
        _inner = inner;
        _thinkMs = thinkMs;
    }

    /// <summary>True while the AI is "thinking" about a turn or a choice (the UI's holographic eye pulses).</summary>
    public event Action<bool>? ThinkingChanged;

    public async Task<GameAction> ChooseActionAsync(DecisionContext context, CancellationToken ct)
    {
        ThinkingChanged?.Invoke(true);
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(_thinkMs * Anim.SpeedFactor), ct);
            return await _inner.ChooseActionAsync(context, ct);
        }
        finally
        {
            ThinkingChanged?.Invoke(false);
        }
    }

    public Task<GameAction?> ChooseResponseAsync(DecisionContext context, CancellationToken ct) =>
        _inner.ChooseResponseAsync(context, ct);

    public async Task<IReadOnlyList<int>> ChooseAsync(ChoiceRequest request, GameState state, CancellationToken ct)
    {
        ThinkingChanged?.Invoke(true);
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(_thinkMs * 0.6 * Anim.SpeedFactor), ct);
            return await _inner.ChooseAsync(request, state, ct);
        }
        finally
        {
            ThinkingChanged?.Invoke(false);
        }
    }
}
