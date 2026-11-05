using CosmicCrusade.Core.Engine;
using CosmicCrusade.Core.Model;

namespace CosmicCrusade.Core.Effects;

/// <summary>Everything one resolving ability can see: who it belongs to, where it is, and what it has chosen so far.</summary>
internal sealed class EffectContext
{
    public EffectContext(GameEngine engine, CardInstance source, int controller, Node? selfNode, string trigger)
    {
        Engine = engine;
        Source = source;
        Controller = controller;
        SelfNode = selfNode;
        Trigger = trigger;
    }

    public GameEngine Engine { get; }
    public GameState State => Engine.State;

    /// <summary>The card whose ability this is ("Self", "this Unit").</summary>
    public CardInstance Source { get; }

    /// <summary>"You": the controller of the source (the side it sits on, or the player who issued the Order).</summary>
    public int Controller { get; }
    public int Opponent => GameState.Other(Controller);

    /// <summary>The source's node (its last known node for "when this leaves play" / "if this is Nullified").</summary>
    public Node? SelfNode { get; }

    public string Trigger { get; }

    /// <summary>React effects: the stack item being responded to (Event.Unit, Event.Order, Event.TargetNode).</summary>
    public StackItem? EventItem { get; init; }

    /// <summary>"remember" / "Remembered.x": the source card's memory (a snapshot once it has left play).</summary>
    public Dictionary<string, object> Memory { get; init; } = new();

    /// <summary>"bind" / "Bound.x".</summary>
    public Dictionary<string, List<object>> Bound { get; } = new();

    /// <summary>Effect-level "choose" with a name / "Chosen.x".</summary>
    public Dictionary<string, List<object>> Chosen { get; } = new();

    /// <summary>"countAs" / "Count.x". RetireHand also records x.units and x.orders.</summary>
    public Dictionary<string, int> Counts { get; } = new();
}
