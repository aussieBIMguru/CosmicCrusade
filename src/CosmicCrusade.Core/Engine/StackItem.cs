using CosmicCrusade.Core.Model;

namespace CosmicCrusade.Core.Engine;

public enum StackItemKind
{
    /// <summary>A Unit being Deployed (from hand or advancing from Standby).</summary>
    DeployUnit,

    /// <summary>An Order being Issued: an Action or Ongoing on your turn, or a React in a priority window.</summary>
    IssueOrder,

    /// <summary>A Unit from hand being Assembled onto an allied Unit (ruling #18).</summary>
    Assemble,
}

/// <summary>
/// Something waiting on the priority stack (spec §7.2). Reacts read its public properties to decide whether
/// their condition is met ("as a Unit is being Deployed", "as a React or Action is being Issued").
/// </summary>
public sealed class StackItem
{
    private static int _nextId;

    public StackItem(int controller, StackItemKind kind, CardInstance card, string description,
        Func<StackItem, CancellationToken, Task> resolve)
    {
        Id = Interlocked.Increment(ref _nextId);
        Controller = controller;
        Kind = kind;
        Card = card;
        Description = description;
        Resolve = resolve;
    }

    /// <summary>Unique per item (UI bookkeeping).</summary>
    public int Id { get; }

    /// <summary>Who controls the item. Mutable for Commandeer (M3).</summary>
    public int Controller { get; internal set; }

    public StackItemKind Kind { get; }

    /// <summary>The card on the stack: the Unit being deployed / assembled, or the Order being issued.</summary>
    public CardInstance Card { get; }

    public string Description { get; }

    /// <summary>DeployUnit: the node the Unit is going to.</summary>
    public Node? TargetNode { get; init; }

    /// <summary>DeployUnit from Standby: the Standby node it came from.</summary>
    public Node? FromNode { get; init; }

    /// <summary>IssueOrder (Ongoing): the Order Node it will occupy.</summary>
    public Node? OrderNode { get; init; }

    /// <summary>Assemble: the allied Unit receiving the Rank.</summary>
    public CardInstance? AssembleTarget { get; init; }

    /// <summary>React: the stack item it responds to.</summary>
    public StackItem? RespondingTo { get; init; }

    public bool IsReact => Kind == StackItemKind.IssueOrder && Card.Definition.OrderType == OrderType.React;

    /// <summary>
    /// Set by counter-effects (Shadow Strike, Second Thought...). A cancelled item is not Played / does not resolve.
    /// The counter-effect has already moved the card to its destination pile.
    /// </summary>
    public bool Cancelled { get; set; }

    internal Func<StackItem, CancellationToken, Task> Resolve { get; }

    public override string ToString() => Description;
}
