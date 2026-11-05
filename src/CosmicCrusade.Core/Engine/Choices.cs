using CosmicCrusade.Core.Model;

namespace CosmicCrusade.Core.Engine;

public enum ChoiceKind
{
    /// <summary>Options are "Yes" (index 0) and "No" (index 1).</summary>
    YesNo,

    /// <summary>Options carry a <see cref="ChoiceOption.Card"/> (units in play, cards in piles or in hand).</summary>
    Cards,

    /// <summary>Options carry a <see cref="ChoiceOption.Node"/>.</summary>
    Nodes,

    /// <summary>Options are numbers (<see cref="ChoiceOption.Value"/>), e.g. "draw up to 2".</summary>
    Number,

    /// <summary>Labelled options, e.g. "+2 / −2" or "Unit deck / Order deck".</summary>
    Options,
}

public sealed record ChoiceOption(string Label, CardInstance? Card = null, Node? Node = null, int Value = 0);

/// <summary>
/// A decision an ability needs from a player while it resolves.
/// <paramref name="Purpose"/> tells an AI what the choice is for ("Nullify", "Lock", "ManipulateSign", ...).
/// </summary>
public sealed record ChoiceRequest(
    int Player,
    CardInstance? Source,
    string Prompt,
    ChoiceKind Kind,
    IReadOnlyList<ChoiceOption> Options,
    int Min,
    int Max,
    string Purpose)
{
    public static ChoiceRequest YesNo(int player, CardInstance? source, string prompt, string purpose) =>
        new(player, source, prompt, ChoiceKind.YesNo,
            new[] { new ChoiceOption("Yes", Value: 1), new ChoiceOption("No", Value: 0) }, 1, 1, purpose);

    /// <summary>The first options, as many as needed (at least Min, and 1 when allowed).</summary>
    public IReadOnlyList<int> DefaultPick() =>
        Enumerable.Range(0, Math.Min(Options.Count, Math.Max(Min, Math.Min(Max, 1)))).ToList();

    /// <summary>True when <paramref name="picks"/> is a legal answer.</summary>
    public bool IsValid(IReadOnlyList<int> picks) =>
        picks.Count >= Min && picks.Count <= Max
        && picks.All(i => i >= 0 && i < Options.Count)
        && picks.Distinct().Count() == picks.Count;
}
