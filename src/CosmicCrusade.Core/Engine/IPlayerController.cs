using CosmicCrusade.Core.Model;

namespace CosmicCrusade.Core.Engine;

/// <summary>
/// A seat at the table: human UI, AI, or (later) a network player. The engine only asks controllers to pick
/// from legal options, so a controller can never make an illegal move.
/// </summary>
public interface IPlayerController
{
    /// <summary>Main Phase turn: pick one of <see cref="DecisionContext.Legal"/> (always contains a Pass).</summary>
    Task<GameAction> ChooseActionAsync(DecisionContext context, CancellationToken ct);

    /// <summary>Priority window: return a React from <see cref="DecisionContext.Legal"/>, or null for "OK, no response".</summary>
    Task<GameAction?> ChooseResponseAsync(DecisionContext context, CancellationToken ct);

    /// <summary>
    /// A choice made while an ability resolves (targets, nodes, yes/no, how many, +N or −N...). Return the indices
    /// of the chosen <see cref="ChoiceRequest.Options"/>: distinct, between Min and Max of them.
    /// The default picks the first options, which keeps simple controllers (tests) working.
    /// </summary>
    Task<IReadOnlyList<int>> ChooseAsync(ChoiceRequest request, GameState state, CancellationToken ct) =>
        Task.FromResult(request.DefaultPick());
}
