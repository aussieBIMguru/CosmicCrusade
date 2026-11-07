using System.Text.Json;
using CosmicCrusade.Core.Effects;
using CosmicCrusade.Core.Model;
using CosmicCrusade.Core.Rules;

namespace CosmicCrusade.Core.Engine;

/// <summary>
/// M3.3: the scripted handlers (Ace, Terraform, Mr Morph / Forced Mitosis, TR-1993.r) and the stack-control
/// primitives behind Recolonize, Commandeer and One of Us.
/// </summary>
public sealed partial class GameEngine
{
    /// <summary>Safety cap on Ace's switches (each one is a separate choice).</summary>
    internal const int MaxRearrangeSteps = 12;

    // ================================================================== Ace, Council Tactician

    /// <summary>
    /// "When you play this Unit, you may rearrange all Nodes (the new Nodes must be legal)." The controller makes one
    /// switch at a time: pick a node, then the node on the same side to switch it with; NONE ends the rearrangement.
    /// See <see cref="Rearrange"/> for what may move.
    /// </summary>
    private async Task RearrangeAllNodesAsync(EffectContext ctx, CancellationToken ct)
    {
        int player = ctx.Controller;
        var source = ctx.Source;
        for (int step = 0; step < MaxRearrangeSteps; step++)
        {
            var movable = Rearrange.Movable(State);
            if (movable.Count == 0)
            {
                if (step == 0) await EmitAsync(new LogEvent($"  {source.Name}: nothing can be rearranged."), ct);
                return;
            }

            var first = await ChooseAsync(new ChoiceRequest(player, source,
                $"{source.Name}: choose a node to move{(step == 0 ? "" : " next")} (NONE when you're done)",
                ChoiceKind.Nodes, movable.Select(n => new ChoiceOption(NodeLabel(n, player), Node: n)).ToList(),
                0, 1, "RearrangeFrom"), ct);
            if (first.Count == 0) return;
            var a = movable[first[0]];

            var partners = Rearrange.Partners(State, a);
            var second = await ChooseAsync(new ChoiceRequest(player, source,
                $"{source.Name}: switch {NodeLabel(a, player)} with which node?",
                ChoiceKind.Nodes, partners.Select(n => new ChoiceOption(NodeLabel(n, player), Node: n)).ToList(),
                1, 1, $"RearrangeTo:{a.Id}"), ct);
            var b = partners[second.Count > 0 ? second[0] : 0];

            await RepositionSwapAsync(a, b, ct);
        }
    }

    /// <summary>
    /// Switch two nodes on the same side (Ace). Units stay in play when they move between Unit/Leader nodes; a Unit
    /// going into Standby leaves play ("When this Unit leaves play" fires, Rank changes reset; Black Hole Nullifies it
    /// instead), and one coming out of Standby enters play without being Played.
    /// </summary>
    internal async Task RepositionSwapAsync(Node a, Node b, CancellationToken ct)
    {
        // Black Hole: "If a Unit would be put into a Standby Zone, Nullify it instead" (prevented → it moves anyway).
        foreach (var (from, to) in new[] { (a, b), (b, a) })
            if (from.IsScoring && to.Kind == NodeKind.Standby)
                foreach (var c in from.Occupants.ToList())
                    await BlackHoleReplacesAsync(c, ct);

        var ca = a.Card;
        var cb = b.Card;
        if (ca is null && cb is null) return;
        if (!Rearrange.ContentsFit(a, b) || !Rearrange.ContentsFit(b, a)) return; // something changed during a Nullify window

        // Everything that crosses between play (Unit/Leader nodes) and Standby. A spacecraft travels with its crew.
        var leaving = new List<(CardInstance Card, Node From, Dictionary<string, object> Memory, bool Suppressed)>();
        foreach (var (from, to) in new[] { (a, b), (b, a) })
            if (from.IsScoring && !to.IsScoring)
                foreach (var c in from.Occupants)
                    leaving.Add((c, from, LeaveMemory(c), StaticLayer.IsSuppressed(State, c)));

        State.SwapContents(a, b);
        foreach (var l in leaving) l.Card.ResetInPlayState();

        await EmitAsync(new NodesSwappedEvent(a, b, cb, ca), ct);
        foreach (var l in leaving) await LeftPlayAsync(l.Card, l.From, l.Memory, l.Suppressed, nullified: false, ct);
    }

    // ================================================================== Terraform

    /// <summary>
    /// "Lock a Node, and its opposing Node. The Unit's controllers may put those Units into their Standby Zone."
    /// Ruling #19: each Unit's controller may move it to their Standby; if they don't or can't (no room, a Leader:
    /// ruling #36) it is Nullified – before the Lock. Immune Units are unaffected, but their nodes are still Locked.
    /// </summary>
    private async Task TerraformAsync(EffectContext ctx, CancellationToken ct)
    {
        int player = ctx.Controller;
        var source = ctx.Source;
        var nodes = State.AllNodes.Where(n => n.IsScoring && State.OpposingNode(n) is not null).ToList();
        if (nodes.Count == 0) return;

        var pick = await ChooseAsync(new ChoiceRequest(player, source,
            $"{source.Name}: choose a Node to Lock (its opposing Node is Locked too)",
            ChoiceKind.Nodes, nodes.Select(n => new ChoiceOption(NodeLabel(n, player), Node: n)).ToList(),
            1, 1, "Terraform"), ct);
        var chosen = nodes[pick.Count > 0 ? pick[0] : 0];
        var pair = new[] { chosen, State.OpposingNode(chosen)! };

        // Spacecraft: a spacecraft and its crew are both Units on the node; the crew is dealt with first.
        foreach (var (node, unit) in pair.SelectMany(n => n.Occupants.Reverse().Select(u => (n, u))).ToList())
        {
            if (!node.Holds(unit) || !unit.Definition.IsUnit) continue;
            if (StaticLayer.IsImmune(State, unit))
            {
                await EmitAsync(new LogEvent($"  {unit.Name} is Immune: {source.Name} doesn't move it."), ct);
                continue;
            }

            int owner = node.Side;
            bool moved = false;
            bool room = State.Players[owner].StandbyNodes.Any(n => n.IsEmpty);
            if (Placement.CanEnterStandby(unit) && room)
            {
                bool keep = await AskYesNoAsync(owner, source,
                    $"{source.Name}: put {unit.Name} into your Standby zone? (If not, it is Nullified.)", "TerraformKeep", ct);
                if (keep) moved = await MoveToStandbyAsync(unit, owner, retireIfNoRoom: false, ct);
            }
            else
            {
                string why = Placement.CanEnterStandby(unit) ? "there is no room in Standby" : "a Leader can't go into Standby";
                await EmitAsync(new LogEvent($"  {unit.Name} can't go to Standby ({why})."), ct);
            }

            if (!moved && node.Holds(unit)) await NullifyAsync(unit, source, ct);
        }

        foreach (var node in pair) await SetLockAsync(node, true, source, ct);
    }

    // ================================================================== Mr Morph / Forced Mitosis

    /// <summary>
    /// "Enters (play) as a copy of another Unit, except it is a Blaarg." Ruling #24: the copied Unit is one in play.
    ///   • Mr Morph (AsEnters, optional): it has just landed; its controller picks the Unit and it becomes the copy at
    ///     once, before "When you play" abilities (Deployed as written, Played as the chosen Unit).
    ///   • Forced Mitosis (React to a Deploy): the React's issuer picks the Unit now; the deploying Unit becomes the copy
    ///     when it enters play (<see cref="StackItem.EntersAsCopy"/>).
    /// A copy takes the copied card's printed values (Rank, class, abilities), not its Rank changes.
    /// </summary>
    private async Task EnterAsCopyAsync(JsonElement effect, EffectContext ctx, CancellationToken ct)
    {
        effect.TryGetProperty("params", out var p);
        var races = new List<Race>();
        if (p.ValueKind == JsonValueKind.Object && p.TryGetProperty("setRaces", out var sr) && sr.ValueKind == JsonValueKind.Array)
            foreach (var r in sr.EnumerateArray())
                races.Add(StaticLayer.TextRace(State, CardDatabase.ParseEnum<Race>(r.GetString()!), ctx.Source));
        races = races.Distinct().ToList();

        bool forDeploy = Str(p, "subject") == "Event.Unit";
        StackItem? item = null;
        CardInstance subject;
        int side;
        if (forDeploy)
        {
            item = ctx.EventItem;
            if (item is not { Kind: StackItemKind.DeployUnit, Cancelled: false, TargetNode: not null }
                || item.Card.Location.Kind != LocationKind.Stack)
            {
                await EmitAsync(new LogEvent($"  {ctx.Source.Name}: the Unit is no longer being Deployed."), ct);
                return;
            }
            subject = item.Card;
            side = item.TargetNode.Side;
        }
        else
        {
            subject = ctx.Source;
            side = subject.Location.Node?.Side ?? ctx.Controller;
        }

        // Spacecraft: a spacecraft only copies spacecraft (Morphership), a Unit only copies Units (Mr Morph, Forced Mitosis).
        bool ship = subject.PrintedDefinition.IsSpacecraft;
        var candidates = State.AllUnitsInPlay
            .Where(u => !ReferenceEquals(u, subject) && u.Definition.IsSpacecraft == ship).ToList();
        if (candidates.Count == 0)
        {
            await EmitAsync(new LogEvent($"  {ctx.Source.Name}: there is no other {(ship ? "spacecraft" : "Unit")} in play to copy."), ct);
            return;
        }

        var pick = await ChooseAsync(new ChoiceRequest(ctx.Controller, ctx.Source,
            $"{ctx.Source.Name}: choose the Unit {subject.Name} {(forDeploy ? "enters play" : "enters")} as a copy of",
            ChoiceKind.Cards,
            candidates.Select(c => new ChoiceOption($"{c.Definition.Name} (Base Rank {c.Definition.BaseRank ?? 0}) – {c.Location.Node!.Id}", c)).ToList(),
            1, 1, $"CopyFor:{side}"), ct);
        var copied = candidates[pick.Count > 0 ? pick[0] : 0].Definition;

        if (item is not null)
        {
            item.EntersAsCopy = new CopyInstruction(copied, races, ctx.Source);
            await EmitAsync(new LogEvent($"  {subject.Name} will enter play as a copy of {copied.Name}{RacesText(races)}."), ct);
        }
        else
        {
            await ApplyCopyAsync(subject, copied, races, ctx.Source, ct);
        }
    }

    private async Task ApplyCopyAsync(CardInstance card, CardDefinition copied, IReadOnlyList<Race> races, CardInstance? source,
        CancellationToken ct)
    {
        card.BecomeCopy(copied, races);
        await EmitAsync(new CardCopiedEvent(card, copied, source), ct);
        await EmitAsync(new LogEvent($"  {card.PrintedDefinition.Name} becomes a copy of {copied.Name}{RacesText(races)}."), ct);
    }

    private static string RacesText(IReadOnlyList<Race> races) =>
        races.Count == 0 ? "" : $", except it is a {string.Join("/", races)}";

    /// <summary>
    /// After the Unit lands (ResolveDeployAsync): Forced Mitosis' pending copy, otherwise the card's own
    /// "may enter as a copy" (Mr Morph), unless its abilities are Suppressed.
    /// </summary>
    private async Task ApplyEntryAsync(StackItem item, CardInstance card, Node target, CancellationToken ct)
    {
        if (item.EntersAsCopy is { } copy)
        {
            await ApplyCopyAsync(card, copy.Of, copy.Races, copy.Source, ct);
            return;
        }
        if (!EffectsWith(card.Definition, "AsEnters").Any()) return;
        if (StaticLayer.IsTriggerSuppressed(State, card, "AsEnters"))
        {
            await EmitAsync(new LogEvent($"  {card.Name}'s abilities are Suppressed."), ct);
            return;
        }
        await FireTriggersAsync(card, "AsEnters", target.Side, target, card.Memory, null, ct);
    }

    // ================================================================== TR-1993.r

    /// <summary>
    /// "When you play this Unit, you may Trigger any number of Units' Triggered Abilities." Ruling #20: re-run the
    /// "When you play" abilities of any chosen Units in play, each as its controller, in board order. Not itself,
    /// not other re-triggering Units (no loops), not Immune or Suppressed ones.
    /// </summary>
    private async Task RetriggerAbilitiesAsync(EffectContext ctx, CancellationToken ct)
    {
        var candidates = TriggerSources()
            .Where(src => src.Node is { IsScoring: true } && src.Card.Definition.IsUnit && !ReferenceEquals(src.Card, ctx.Source))
            .Where(src => EffectsWith(src.Card.Definition, "OnPlay").Any(EffectSupport.IsEffectLive))
            .Where(src => !EffectsWith(src.Card.Definition, "OnPlay").Any(e => Str(e, "script") == "RetriggerAbilities"))
            .Where(src => !StaticLayer.IsImmune(State, src.Card) && !StaticLayer.IsTriggerSuppressed(State, src.Card, "OnPlay"))
            .ToList();
        if (candidates.Count == 0)
        {
            await EmitAsync(new LogEvent($"  {ctx.Source.Name}: no other Unit in play has a \"When you play\" ability to Trigger."), ct);
            return;
        }

        var picks = await ChooseAsync(new ChoiceRequest(ctx.Controller, ctx.Source,
            $"{ctx.Source.Name}: choose any number of Units to Trigger their \"When you play\" abilities",
            ChoiceKind.Cards,
            candidates.Select(src => new ChoiceOption($"{src.Card.Name} – {src.Node!.Id}", src.Card)).ToList(),
            0, candidates.Count, "Retrigger"), ct);

        foreach (var src in picks.OrderBy(i => i).Select(i => candidates[i]))
        {
            if (!StillThere(src) || StaticLayer.IsTriggerSuppressed(State, src.Card, "OnPlay")) continue;
            await EmitAsync(new LogEvent($"  {ctx.Source.Name} Triggers {src.Card.Name}."), ct);
            await FireTriggersAsync(src.Card, "OnPlay", src.Node!.Side, src.Node, src.Card.Memory, null, ct);
        }
    }

    // ================================================================== Recolonize, Commandeer, One of Us

    /// <summary>Recolonize: "That Unit is Deployed to that Node instead."</summary>
    internal async Task RedirectDeployAsync(StackItem item, Node to, CardInstance? source, CancellationToken ct)
    {
        var from = item.TargetNode!;
        item.TargetNode = to;
        item.Description = $"Deploy {item.Card.Name} to {to.Label}";
        await EmitAsync(new DeployRedirectedEvent(item, from, to, source), ct);
    }

    /// <summary>Commandeer: "Gain control of a React or Action as it is being Issued."</summary>
    internal async Task GainControlOfStackItemAsync(StackItem item, int player, CardInstance? source, CancellationToken ct)
    {
        int old = item.Controller;
        if (old == player)
        {
            await EmitAsync(new LogEvent($"  {State.Players[player].Name} already controls {item.Card.Name}."), ct);
            return;
        }
        item.Controller = player;
        State.MoveToStack(item.Card, player);
        await EmitAsync(new StackItemControlChangedEvent(item, old, player, source), ct);
    }

    /// <summary>
    /// Commandeer: "You may choose new Targets for it." Targets are chosen as a card resolves, so a commandeered
    /// Action's choices are simply its new controller's. A commandeered React can be pointed at another item on the
    /// stack whose condition it meets.
    /// </summary>
    internal async Task RetargetAsync(StackItem item, int player, CardInstance? source, CancellationToken ct)
    {
        if (!item.IsReact || item.RespondingTo is null)
        {
            await EmitAsync(new LogEvent($"  {item.Card.Name}: {State.Players[player].Name} chooses its targets as it resolves."), ct);
            return;
        }

        var others = _stack.Where(x => !ReferenceEquals(x, item) && !ReferenceEquals(x, item.RespondingTo)
                                       && ReactConditions.IsMet(State, item.Card, x)).ToList();
        if (others.Count == 0) return;

        var options = new List<ChoiceOption> { new($"Keep: {item.RespondingTo.Description}") };
        options.AddRange(others.Select(x => new ChoiceOption(x.Description, x.Card)));
        var pick = await ChooseAsync(new ChoiceRequest(player, source, $"{source?.Name ?? "Retarget"}: choose a new target for {item.Card.Name}?",
            ChoiceKind.Options, options, 1, 1, "Retarget"), ct);
        if (pick.Count == 0 || pick[0] == 0) return;

        var target = others[pick[0] - 1];
        item.RespondingTo = target;
        item.Description = $"{item.Card.Name} → {target.Card.Name}";
        await EmitAsync(new StackItemControlChangedEvent(item, player, player, source), ct);
        await EmitAsync(new LogEvent($"  {item.Card.Name} now answers {target.Description}."), ct);
    }

    /// <summary>One of Us: the Unit moves to its new controller's node. It remains in play (it isn't Played again).</summary>
    internal async Task GainControlOfUnitAsync(CardInstance unit, Node to, int player, CardInstance? source, CancellationToken ct)
    {
        if (unit.Location.Node is not { IsScoring: true } from || !Placement.CanMoveInPlayTo(unit, to)) return;
        State.MoveToNode(unit, to);
        await EmitAsync(new UnitControlGainedEvent(unit, from, to, player, source), ct);
    }

    // ================================================================== labels

    private static string NodeLabel(Node n, int viewer) =>
        $"{(n.Side == viewer ? "Your" : "Opponent's")} {n.Label}{(n.Card is null ? "" : $" ({n.Card.Name})")}{(n.IsLocked ? " [locked]" : "")}";

    // ================================================================== Archons

    /// <summary>Ceasefire (P5): if both players control a Leader, the Main Phase ends at once; the round is scored normally.</summary>
    private async Task CeasefireAsync(EffectContext ctx, CancellationToken ct)
    {
        bool both = State.Players.All(p => p.UnitsInPlay.Any(u => u.Definition.IsLeader));
        if (!both)
        {
            await EmitAsync(new LogEvent($"  {ctx.Source.Name}: both players must control a Leader – no effect."), ct);
            return;
        }
        _mainEndsNow = true;
        await EmitAsync(new LogEvent($"  {ctx.Source.Name}: the guns fall silent – the round ends now."), ct);
    }

    /// <summary>Insurrection (P6): if you control at least 3 Units that share a Race, you win the round at once.</summary>
    private async Task InsurrectionAsync(EffectContext ctx, CancellationToken ct)
    {
        var units = State.Players[ctx.Controller].UnitsInPlay.ToList();
        var race = units.SelectMany(u => StaticLayer.Races(State, u)).GroupBy(r => r)
            .Where(g => g.Count() >= 3).Select(g => (Race?)g.Key).FirstOrDefault();
        if (race is null)
        {
            await EmitAsync(new LogEvent($"  {ctx.Source.Name}: you don't control 3 Units that share a Race – no effect."), ct);
            return;
        }
        await WinRoundAsync(ctx.Controller, ctx.Source, ct);
        _roundEndsNow = true;
    }

    /// <summary>
    /// Quantum Leap (P10): draw up to 3 Orders, then issue any of them as additional actions (each still opens a
    /// priority window; Reacts can't be issued this way), then Retire the rest of them (to the Issued pile).
    /// </summary>
    private async Task QuantumLeapAsync(EffectContext ctx, CancellationToken ct)
    {
        int player = ctx.Controller;
        var source = ctx.Source;
        var counts = Enumerable.Range(0, 4).Select(i => new ChoiceOption(i == 0 ? "None" : $"{i} Order{(i == 1 ? "" : "s")}", Value: i)).ToList();
        var n = await ChooseAsync(new ChoiceRequest(player, source, $"{source.Name}: draw how many Orders?",
            ChoiceKind.Number, counts, 1, 1, "QuantumLeapDraw"), ct);
        int count = n.Count > 0 ? counts[n[0]].Value : 0;

        var drawn = new List<CardInstance>();
        for (int i = 0; i < count; i++)
        {
            if (await DrawAsync(player, PileKind.OrderDeck, ct, source) is not { } card) break;
            drawn.Add(card);
        }

        while (true)
        {
            var issues = LegalActions.ForTurn(State, player).OfType<IssueOrderAction>()
                .Where(a => drawn.Contains(a.Card) && a.Card.Location.Kind == LocationKind.Hand).ToList();
            if (issues.Count == 0) break;
            var pick = await ChooseAsync(new ChoiceRequest(player, source, $"{source.Name}: issue one of the drawn Orders as an additional action?",
                ChoiceKind.Options, issues.Select(a => new ChoiceOption(a.Describe(), a.Card, a.OrderNode)).ToList(), 0, 1, "QuantumLeapIssue"), ct);
            if (pick.Count == 0) break;
            var action = issues[pick[0]];
            var item = await AnnounceOrderAsync(player, action.Card, action.OrderNode, respondingTo: null, ct);
            await RunPriorityAsync(item, player, ct);
            await CheckStateAsync(ct);
            if (_roundEndsNow || _mainEndsNow) break;
        }

        foreach (var card in drawn.Where(c => c.Location.Kind == LocationKind.Hand && c.Location.Player == player).ToList())
        {
            State.MoveToPile(card, PileKind.IssuedOrders);
            await EmitAsync(new CardRetiredFromHandEvent(card, player), ct);
        }
    }
}
