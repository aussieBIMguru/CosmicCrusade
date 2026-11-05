using System.Text.Json;
using CosmicCrusade.Core.Engine;
using CosmicCrusade.Core.Model;
using CosmicCrusade.Core.Rules;

namespace CosmicCrusade.Core.Effects;

/// <summary>
/// Interprets the <c>effects[]</c> entries of cards.json (see design/card-catalogue-notes.md). Only effects that
/// <see cref="EffectSupport.IsEffectLive"/> accepts are run. Moves go through the engine's primitives so every
/// change is published as a <see cref="GameEvent"/> and "leaves play" triggers fire.
/// </summary>
internal sealed class EffectRunner
{
    private readonly GameEngine _engine;

    public EffectRunner(GameEngine engine) => _engine = engine;

    private GameState State => _engine.State;

    // ================================================================== effect entry

    public async Task RunAsync(JsonElement effect, EffectContext ctx, CancellationToken ct)
    {
        await _engine.EmitAsync(new AbilityTriggeredEvent(ctx.Source, ctx.Controller, ctx.Trigger), ct);

        if (effect.TryGetProperty("condition", out var cond) && !EvalCondition(cond, ctx))
        {
            await _engine.EmitAsync(new LogEvent($"  {ctx.Source.Name}: its condition isn't met."), ct);
            return;
        }

        if (Bool(effect, "optional"))
        {
            bool yes = await _engine.AskYesNoAsync(ctx.Controller, ctx.Source,
                $"{ctx.Source.Name}: use this ability?", "Optional", ct);
            if (!yes)
            {
                await _engine.EmitAsync(new LogEvent($"  {ctx.Source.Name}: ability declined."), ct);
                return;
            }
        }

        if (effect.TryGetProperty("choose", out var choose) && choose.ValueKind == JsonValueKind.Object)
            ctx.Chosen[Str(choose, "name") ?? "chosen"] = await SelectAsync(choose, ctx, "Lock", ct);

        if (!effect.TryGetProperty("do", out var ops)) return;
        foreach (var op in ops.EnumerateArray())
        {
            ct.ThrowIfCancellationRequested();
            await RunOpAsync(op, ctx, ct);
        }
    }

    // ================================================================== ops

    private async Task RunOpAsync(JsonElement op, EffectContext ctx, CancellationToken ct)
    {
        string name = Str(op, "op") ?? "";
        switch (name)
        {
            case "Nullify":
            {
                var targets = await SelectAsync(op.GetProperty("targets"), ctx, "Nullify", ct);
                if (Str(op, "bind") is { } bind) ctx.Bound[bind] = targets;
                foreach (var card in targets.OfType<CardInstance>().ToList())
                    await _engine.NullifyAsync(card, ctx.Source, ct);
                break;
            }

            case "ModifyRank":
            {
                var amountEl = op.GetProperty("amount");
                bool buff = amountEl.ValueKind != JsonValueKind.Number || amountEl.GetInt32() >= 0;
                var targets = await SelectAsync(op.GetProperty("targets"), ctx, buff ? "Buff" : "Debuff", ct);
                foreach (var card in targets.OfType<CardInstance>().Where(InPlay))
                {
                    int amount = EvalAmount(amountEl, card);
                    card.PersistentModifiers.Add(new RankModifier(ctx.Source.Name, amount));
                    await _engine.EmitAsync(new RankChangedEvent(card, $"{amount:+0;-0;0} Rank", ctx.Source), ct);
                }
                break;
            }

            case "SetBaseRank":
            {
                int value = op.GetProperty("value").GetInt32();
                var targets = await SelectAsync(op.GetProperty("targets"), ctx, $"SetBaseRank:{value}", ct);
                foreach (var card in targets.OfType<CardInstance>().Where(InPlay))
                {
                    card.BaseRankOverride = value;
                    await _engine.EmitAsync(new RankChangedEvent(card, $"Base Rank {value}", ctx.Source), ct);
                }
                break;
            }

            case "Lock":
            {
                var nodes = (await SelectAsync(op.GetProperty("targets"), ctx, "Lock", ct)).OfType<Node>().ToList();
                foreach (var node in nodes)
                    if (!node.IsLocked) await _engine.SetLockAsync(node, true, ctx.Source, ct);
                if (Str(op, "remember") is { } key && nodes.Count > 0) ctx.Memory[key] = nodes[0];
                break;
            }

            case "Unlock":
            {
                var nodes = (await SelectAsync(op.GetProperty("targets"), ctx, "Unlock", ct)).OfType<Node>().ToList();
                int n = 0;
                foreach (var node in nodes)
                {
                    if (!node.IsLocked) continue;
                    await _engine.SetLockAsync(node, false, ctx.Source, ct);
                    n++;
                }
                if (Str(op, "countAs") is { } c) ctx.Counts[c] = n;
                break;
            }

            case "MoveToStandby":
            {
                var targets = await SelectAsync(op.GetProperty("targets"), ctx, "MoveToStandby", ct);
                bool toController = Str(op, "standbyOf") == "Controller";
                bool retire = Str(op, "ifNoRoom") == "Retire";
                bool assembleToHand = op.TryGetProperty("ifHasKeyword", out _);
                int n = 0;
                foreach (var card in targets.OfType<CardInstance>().ToList())
                {
                    int side = toController && ControllerOf(card) is >= 0 and var c ? c : ctx.Controller;
                    if (assembleToHand && AssembleRules.HasAssemble(State, card))
                    {
                        await _engine.ReturnToHandAsync(card, side, ct);
                        n++;
                        continue;
                    }
                    if (await _engine.MoveToStandbyAsync(card, side, retire, ct)) n++;
                }
                if (Str(op, "countAs") is { } key) ctx.Counts[key] = n;
                break;
            }

            case "Draw":
                await DrawAsync(op, ctx, ct);
                break;

            case "Duel":
                await DuelAsync(op, ctx, ct);
                break;

            case "Manipulate":
            {
                int n = op.GetProperty("amount").GetInt32();
                var candidates = op.TryGetProperty("filter", out var f)
                    ? Cards(f, ctx, unitOnly: true)
                    : Cards(default, ctx, unitOnly: true);
                var picked = await AskCardsAsync(ctx, candidates, 1, 1, "Manipulate", $"{ctx.Source.Name}: choose a Unit to Manipulate {n}", ct);
                var target = picked.FirstOrDefault();
                if (target is null) break;

                var sign = await _engine.ChooseAsync(new ChoiceRequest(ctx.Controller, ctx.Source,
                    $"{ctx.Source.Name}: Manipulate {target.Name} by +{n} or −{n}?", ChoiceKind.Options,
                    new[] { new ChoiceOption($"+{n}", target, Value: n), new ChoiceOption($"−{n}", target, Value: -n) },
                    1, 1, "ManipulateSign"), ct);
                int delta = sign.Count > 0 && sign[0] == 1 ? -n : n;
                target.PersistentModifiers.Add(new RankModifier("Manipulate", delta));
                await _engine.EmitAsync(new RankChangedEvent(target, $"{delta:+0;-0;0} Rank (Manipulate)", ctx.Source), ct);
                break;
            }

            case "ReturnToHand":
            {
                var targets = await SelectAsync(op.GetProperty("targets"), ctx, "ReturnToHand", ct);
                bool toController = Str(op, "hand") == "Controller";
                foreach (var card in targets.OfType<CardInstance>().ToList())
                {
                    int player = toController && ControllerOf(card) is >= 0 and var c ? c : ctx.Controller;
                    await _engine.ReturnToHandAsync(card, player, ct);
                }
                break;
            }

            case "RetireFromHand":
            {
                var hand = State.Players[ctx.Controller].Hand.ToList();
                if (op.TryGetProperty("filter", out var f)) hand = hand.Where(c => MatchCard(c, f, ctx)).ToList();
                int min = IntOr(op, "min", 1, ctx), max = IntOr(op, "max", 1, ctx);
                var picked = await AskCardsAsync(ctx, hand, min, max, "RetireFromHand",
                    $"{ctx.Source.Name}: choose cards to Retire from your hand", ct);
                foreach (var card in picked) await _engine.RetireFromHandAsync(card, ct);
                if (Str(op, "countAs") is { } key) ctx.Counts[key] = picked.Count;
                break;
            }

            case "RetireHand":
            {
                var hand = State.Players[ctx.Controller].Hand.ToList();
                string key = Str(op, "countAs") ?? "hand";
                ctx.Counts[key] = hand.Count;
                ctx.Counts[key + ".units"] = hand.Count(c => c.Definition.IsUnit);
                ctx.Counts[key + ".orders"] = hand.Count(c => c.Definition.IsOrder);
                foreach (var card in hand) await _engine.RetireFromHandAsync(card, ct);
                break;
            }

            case "SwapNodeContents":
            {
                var nodes = (await SelectAsync(op.GetProperty("nodes"), ctx, "Swap", ct)).OfType<Node>().ToList();
                if (nodes.Count == 2) await _engine.SwapAsync(nodes[0], nodes[1], ct);
                break;
            }

            case "Suppress":
            {
                var targets = await SelectAsync(op.GetProperty("targets"), ctx, "Suppress", ct);
                foreach (var card in targets.OfType<CardInstance>())
                {
                    card.AbilitiesSuppressed = true;
                    await _engine.EmitAsync(new AbilitiesSuppressedEvent(card, ctx.Source), ct);
                }
                break;
            }

            case "Assemble":
            {
                var source = (await SelectAsync(op.GetProperty("source"), ctx, "AssembleSource", ct)).OfType<CardInstance>().FirstOrDefault();
                if (source is null) break;
                var onto = (await SelectAsync(op.GetProperty("onto"), ctx, "Buff", ct)).OfType<CardInstance>().FirstOrDefault();
                if (onto is null) break;
                await _engine.AssembleFromAsync(source, onto, ctx.Controller, ct);
                break;
            }

            default:
                await _engine.EmitAsync(new LogEvent($"  {ctx.Source.Name}: '{name}' is not implemented yet."), ct);
                break;
        }
    }

    private async Task DrawAsync(JsonElement op, EffectContext ctx, CancellationToken ct)
    {
        int player = ctx.Controller;

        // "Draw up to 2 Unit Cards": a named deck with min/max.
        if (Str(op, "deck") is { } deckName)
        {
            var deck = deckName == "Units" ? PileKind.UnitDeck : PileKind.OrderDeck;
            int min = IntOr(op, "min", 1, ctx), max = IntOr(op, "max", 1, ctx);
            int n = max;
            if (min != max)
            {
                var options = Enumerable.Range(min, max - min + 1).Select(i => new ChoiceOption(i.ToString(), Value: i)).ToList();
                var pick = await _engine.ChooseAsync(new ChoiceRequest(player, ctx.Source,
                    $"{ctx.Source.Name}: how many {(deck == PileKind.UnitDeck ? "Unit" : "Order")} cards do you draw?",
                    ChoiceKind.Number, options, 1, 1, "Draw"), ct);
                n = pick.Count > 0 ? options[pick[0]].Value : max;
            }
            for (int i = 0; i < n; i++)
                if (await _engine.DrawAsync(player, deck, ct) is null) break;
            return;
        }

        int count = EvalCount(op.GetProperty("count"), ctx);
        if (count <= 0) return;

        int units;
        if (Str(op, "distribution") == "SameAsRetired")
        {
            // "Retire your Hand, then Draw that many": keep the Unit/Order split (spec §5.1).
            string key = (Str(op, "count") ?? "Count.hand").Replace("Count.", "");
            units = ctx.Counts.GetValueOrDefault(key + ".units");
        }
        else
        {
            // "Draw N cards": you choose the deck for each card. Asked once as a split.
            var options = Enumerable.Range(0, count + 1)
                .Select(i => new ChoiceOption($"{i} Unit / {count - i} Order", Value: i)).Reverse().ToList();
            var pick = await _engine.ChooseAsync(new ChoiceRequest(player, ctx.Source,
                $"{ctx.Source.Name}: draw {count} card{(count == 1 ? "" : "s")} – how many from the Unit deck?",
                ChoiceKind.Options, options, 1, 1, "DeckSplit"), ct);
            units = pick.Count > 0 ? options[pick[0]].Value : count;
        }

        for (int i = 0; i < units; i++)
            if (await _engine.DrawAsync(player, PileKind.UnitDeck, ct) is null) break;
        for (int i = 0; i < count - units; i++)
            if (await _engine.DrawAsync(player, PileKind.OrderDeck, ct) is null) break;
    }

    private async Task DuelAsync(JsonElement op, EffectContext ctx, CancellationToken ct)
    {
        CardInstance? a, b;
        if (op.TryGetProperty("node", out var nodeSel))
        {
            var node = (await SelectAsync(nodeSel, ctx, "Duel", ct)).OfType<Node>().FirstOrDefault();
            a = node?.Card;
            b = node is null ? null : State.OpposingNode(node)?.Card;
        }
        else
        {
            a = (await SelectAsync(op.GetProperty("a"), ctx, "Duel", ct)).OfType<CardInstance>().FirstOrDefault();
            b = (await SelectAsync(op.GetProperty("b"), ctx, "Duel", ct)).OfType<CardInstance>().FirstOrDefault();
        }
        if (a is null || b is null || !InPlay(a) || !InPlay(b))
        {
            await _engine.EmitAsync(new LogEvent($"  {ctx.Source.Name}: no Duel (a Unit is missing)."), ct);
            return;
        }

        var protect = op.TryGetProperty("protect", out var p) && p.ValueKind == JsonValueKind.Array
            ? p.EnumerateArray().Select(x => x.GetString()).ToHashSet()
            : new HashSet<string?>();
        var loser = Duel.Loser(State, a, b);
        bool isProtected = (loser == a && protect.Contains("a")) || (loser == b && protect.Contains("b"));
        await _engine.EmitAsync(new DuelEvent(a, b, isProtected ? null : loser, isProtected), ct);
        if (loser is not null && !isProtected) await _engine.NullifyAsync(loser, ctx.Source, ct);
    }

    // ================================================================== selectors

    /// <summary>Resolves a selector to cards and/or nodes. Choices are asked of the effect's controller.</summary>
    private async Task<List<object>> SelectAsync(JsonElement sel, EffectContext ctx, string purpose, CancellationToken ct)
    {
        if (sel.ValueKind == JsonValueKind.String) return Named(sel.GetString()!, ctx);
        if (sel.ValueKind != JsonValueKind.Object) return new List<object>();
        if (!sel.TryGetProperty("choose", out var what)) return Cards(sel, ctx, unitOnly: false).Cast<object>().ToList();

        string kind = what.GetString()!;
        sel.TryGetProperty("filter", out var filter);
        int min, max;
        if (sel.TryGetProperty("count", out var countEl))
            min = max = EvalCount(countEl, ctx);
        else
        {
            min = IntOr(sel, "min", 1, ctx);
            max = IntOr(sel, "max", 1, ctx);
        }

        if (kind == "Node")
        {
            var nodes = Nodes(filter, ctx);
            if (Bool(sel, "perPlayer"))
            {
                var result = new List<object>();
                foreach (int side in new[] { ctx.Controller, ctx.Opponent })
                    result.AddRange(await AskNodesAsync(ctx, nodes.Where(n => n.Side == side).ToList(), 1, 1, purpose, ct));
                return result;
            }
            return (await AskNodesAsync(ctx, nodes, min, max, purpose, ct)).Cast<object>().ToList();
        }

        var cards = Cards(filter, ctx, unitOnly: kind == "Unit");
        return (await AskCardsAsync(ctx, cards, min, max, purpose, PromptFor(ctx, purpose, kind, min, max), ct))
            .Cast<object>().ToList();
    }

    private List<object> Named(string name, EffectContext ctx)
    {
        var list = new List<object>();
        switch (name)
        {
            case "Self":
                list.Add(ctx.Source);
                break;
            case "OpposingUnit":
                if (ctx.SelfNode is not null && State.OpposingNode(ctx.SelfNode)?.Card is { } opp) list.Add(opp);
                break;
            case "OpposingLeader":
                if (State.Players[ctx.Opponent].LeaderNode?.Card is { } leader) list.Add(leader);
                break;
            case "Event.Unit":
                if (ctx.EventItem is { Kind: StackItemKind.DeployUnit, Cancelled: false } du && du.Card.Location.Kind == LocationKind.Stack)
                    list.Add(du.Card);
                break;
            case "Event.Order":
                if (ctx.EventItem is { Kind: StackItemKind.IssueOrder, Cancelled: false } io && io.Card.Location.Kind == LocationKind.Stack)
                    list.Add(io.Card);
                break;
            case "Event.TargetNode":
                if (ctx.EventItem?.TargetNode is { } tn) list.Add(tn);
                break;
            default:
                if (name.StartsWith("Bound.") && ctx.Bound.TryGetValue(name[6..], out var bound)) list.AddRange(bound);
                else if (name.StartsWith("Remembered.") && ctx.Memory.TryGetValue(name[11..], out var mem)) list.Add(mem);
                else if (name.StartsWith("Chosen."))
                {
                    var parts = name.Split('.');
                    if (ctx.Chosen.TryGetValue(parts[1], out var chosen))
                    {
                        if (parts.Length > 2 && parts[2] == "Unit")
                            list.AddRange(chosen.OfType<Node>().Where(n => n.Card is not null).Select(n => (object)n.Card!));
                        else
                            list.AddRange(chosen);
                    }
                }
                break;
        }
        return list;
    }

    private async Task<List<CardInstance>> AskCardsAsync(EffectContext ctx, List<CardInstance> candidates, int min, int max,
        string purpose, string prompt, CancellationToken ct)
    {
        if (candidates.Count == 0 || max <= 0) return new List<CardInstance>();
        var options = candidates.Select(c => new ChoiceOption(CardLabel(c), c)).ToList();
        var picks = await _engine.ChooseAsync(new ChoiceRequest(ctx.Controller, ctx.Source, prompt, ChoiceKind.Cards,
            options, Math.Min(min, options.Count), Math.Min(max, options.Count), purpose), ct);
        return picks.Select(i => candidates[i]).ToList();
    }

    private async Task<List<Node>> AskNodesAsync(EffectContext ctx, List<Node> candidates, int min, int max, string purpose, CancellationToken ct)
    {
        if (candidates.Count == 0 || max <= 0) return new List<Node>();
        var options = candidates.Select(n => new ChoiceOption(NodeLabel(n, ctx.Controller), Node: n)).ToList();
        var picks = await _engine.ChooseAsync(new ChoiceRequest(ctx.Controller, ctx.Source,
            PromptFor(ctx, purpose, "Node", Math.Min(min, options.Count), Math.Min(max, options.Count)), ChoiceKind.Nodes,
            options, Math.Min(min, options.Count), Math.Min(max, options.Count), purpose), ct);
        return picks.Select(i => candidates[i]).ToList();
    }

    private static string PromptFor(EffectContext ctx, string purpose, string kind, int min, int max)
    {
        string what = kind == "Node" ? "Node" : "Unit";
        if (kind == "Card") what = "card";
        string count = max >= 99 ? $"any number of {what}s"
            : min == max ? (max == 1 ? $"a {what}" : $"{max} {what}s")
            : min == 0 ? (max == 1 ? $"up to 1 {what}" : $"up to {max} {what}s")
            : $"{min}–{max} {what}s";
        string verb = purpose switch
        {
            "Nullify" => "to Nullify",
            "Lock" => "to Lock",
            "Unlock" => "to Unlock",
            "Buff" => "to gain Rank",
            "Debuff" => "to lose Rank",
            "MoveToStandby" => "to place into Standby",
            "ReturnToHand" => "to return to its hand",
            "Duel" => "to Duel",
            "Swap" => "to switch",
            "AssembleSource" => "to Assemble",
            _ when purpose.StartsWith("SetBaseRank:") => $"to set to Base Rank {purpose[12..]}",
            _ => "",
        };
        return $"{ctx.Source.Name}: choose {count} {verb}".TrimEnd();
    }

    private string CardLabel(CardInstance c)
    {
        string where = c.Location.Kind switch
        {
            LocationKind.Node => c.Location.Node!.Id,
            LocationKind.Pile => c.Location.Pile.ToString()!,
            LocationKind.Hand => "hand",
            _ => "",
        };
        string rank = c.Definition.IsUnit ? $" (Rank {(InPlay(c) ? RankCalculator.GetRank(State, c) : RankCalculator.BaseRank(c))})" : "";
        return $"{c.Name}{rank} – {where}";
    }

    private static string NodeLabel(Node n, int you) =>
        $"{(n.Side == you ? "Your" : "Opponent's")} {n.Label}{(n.Card is null ? "" : $" ({n.Card.Name})")}{(n.IsLocked ? " [locked]" : "")}";

    // ================================================================== filters

    /// <summary>Cards matching a filter. With no zone, Units in play (and Orders on Order Nodes unless unitOnly).</summary>
    private List<CardInstance> Cards(JsonElement filter, EffectContext ctx, bool unitOnly)
    {
        var zones = new List<string>();
        if (filter.ValueKind == JsonValueKind.Object && filter.TryGetProperty("zone", out var z))
        {
            if (z.ValueKind == JsonValueKind.String) zones.Add(z.GetString()!);
            else if (z.ValueKind == JsonValueKind.Array) zones.AddRange(z.EnumerateArray().Select(x => x.GetString()!));
        }
        if (zones.Count == 0) zones.Add("InPlay");

        var result = new List<CardInstance>();
        foreach (var zone in zones)
        {
            switch (zone)
            {
                case "InPlay":
                    result.AddRange(State.Players.SelectMany(p => p.ScoringNodes).Where(n => n.Card is not null).Select(n => n.Card!));
                    result.AddRange(State.Players.SelectMany(p => p.OrderNodes).Where(n => n.Card is not null).Select(n => n.Card!));
                    break;
                case "OrderNode":
                    result.AddRange(State.Players.SelectMany(p => p.OrderNodes).Where(n => n.Card is not null).Select(n => n.Card!));
                    break;
                case "Standby":
                    result.AddRange(State.Players.SelectMany(p => p.StandbyNodes).Where(n => n.Card is not null).Select(n => n.Card!));
                    break;
                case "RetiredPile": result.AddRange(State.RetiredUnits); break;
                case "NullifiedPile": result.AddRange(State.NullifiedUnits); break;
                case "IssuedPile": result.AddRange(State.IssuedOrders); break;
            }
        }

        // Piles list bottom → top; offer the top cards first.
        result.Reverse();
        return result
            .Distinct()
            .Where(c => !unitOnly || c.Definition.IsUnit)
            .Where(c => filter.ValueKind != JsonValueKind.Object || MatchCard(c, filter, ctx))
            .ToList();
    }

    private bool MatchCard(CardInstance card, JsonElement filter, EffectContext ctx)
    {
        foreach (var p in filter.EnumerateObject())
        {
            var v = p.Value;
            switch (p.Name)
            {
                case "kind":
                    if (card.Definition.Kind != CardDatabase.ParseEnum<CardKind>(v.GetString()!)) return false;
                    break;
                case "controller":
                    int who = v.GetString() == "You" ? ctx.Controller : ctx.Opponent;
                    if (ControllerOf(card) != who) return false;
                    break;
                case "race":
                    if (!card.Races.Contains(CardDatabase.ParseEnum<Race>(v.GetString()!))) return false;
                    break;
                case "notRace":
                    if (card.Races.Contains(CardDatabase.ParseEnum<Race>(v.GetString()!))) return false;
                    break;
                case "unitClass":
                    if (card.Definition.UnitClass != CardDatabase.ParseEnum<UnitClass>(v.GetString()!)) return false;
                    break;
                case "orderType":
                    if (card.Definition.OrderType != CardDatabase.ParseEnum<OrderType>(v.GetString()!)) return false;
                    break;
                case "excludeSelf":
                    if (v.ValueKind == JsonValueKind.True && ReferenceEquals(card, ctx.Source)) return false;
                    break;
                case "opposed":
                    if ((OpposingCard(card) is not null) != (v.ValueKind == JsonValueKind.True)) return false;
                    break;
                case "opposingUnitRace":
                    if (OpposingCard(card) is not { } oc || !oc.Races.Contains(CardDatabase.ParseEnum<Race>(v.GetString()!))) return false;
                    break;
                case "sharesRaceWith":
                    var others = Named(v.GetString()!, ctx).OfType<CardInstance>().ToList();
                    if (!others.Any(o => o.Races.Overlaps(card.Races) || o.Definition.Races.Any(card.Races.Contains))) return false;
                    break;
                case "excludeBound":
                    if (v.ValueKind == JsonValueKind.True && ctx.Bound.Values.Any(l => l.Contains(card))) return false;
                    break;
                case "baseRankLessThan":
                    int count = Cards(v.GetProperty("countOf"), ctx, unitOnly: false).Count;
                    if (RankCalculator.BaseRank(card) >= count) return false;
                    break;
                // zone was applied when gathering; node-only keys don't apply to cards.
            }
        }
        return true;
    }

    private List<Node> Nodes(JsonElement filter, EffectContext ctx)
    {
        var nodes = State.AllNodes.ToList();
        if (filter.ValueKind != JsonValueKind.Object) return nodes;
        return nodes.Where(n => MatchNode(n, filter, ctx)).ToList();
    }

    private bool MatchNode(Node node, JsonElement filter, EffectContext ctx)
    {
        foreach (var p in filter.EnumerateObject())
        {
            var v = p.Value;
            switch (p.Name)
            {
                case "empty":
                    if (node.IsEmpty != (v.ValueKind == JsonValueKind.True)) return false;
                    break;
                case "locked":
                    if (node.IsLocked != (v.ValueKind == JsonValueKind.True)) return false;
                    break;
                case "nodeKind":
                    var kinds = v.ValueKind == JsonValueKind.Array
                        ? v.EnumerateArray().Select(x => CardDatabase.ParseEnum<NodeKind>(x.GetString()!)).ToList()
                        : new List<NodeKind> { CardDatabase.ParseEnum<NodeKind>(v.GetString()!) };
                    if (!kinds.Contains(node.Kind)) return false;
                    break;
                case "side":
                case "nodeOf":
                    string s = v.GetString() ?? "Any";
                    if (s == "You" && node.Side != ctx.Controller) return false;
                    if (s == "Opponent" && node.Side != ctx.Opponent) return false;
                    break;
                case "opposed":
                    bool opposed = node.Card is not null && State.OpposingNode(node)?.Card is not null;
                    if (opposed != (v.ValueKind == JsonValueKind.True)) return false;
                    break;
            }
        }
        return true;
    }

    // ================================================================== conditions and values

    private bool EvalCondition(JsonElement cond, EffectContext ctx)
    {
        if (cond.TryGetProperty("unit", out var unitSel))
        {
            var card = Named(unitSel.GetString()!, ctx).OfType<CardInstance>().FirstOrDefault();
            if (card is null) return false;
            if (Str(cond, "race") is { } race && !card.Races.Contains(CardDatabase.ParseEnum<Race>(race))) return false;
            if (Str(cond, "notRace") is { } notRace && card.Races.Contains(CardDatabase.ParseEnum<Race>(notRace))) return false;
        }
        if (cond.TryGetProperty("exists", out var exists))
        {
            bool nodeFilter = exists.TryGetProperty("nodeOf", out _) || exists.TryGetProperty("empty", out _);
            bool any = nodeFilter ? Nodes(exists, ctx).Count > 0 : Cards(exists, ctx, unitOnly: false).Count > 0;
            if (!any) return false;
        }
        if (cond.TryGetProperty("selfOpposed", out var so) && so.ValueKind == JsonValueKind.True)
        {
            if (ctx.SelfNode is null || State.OpposingNode(ctx.SelfNode)?.Card is null) return false;
        }
        return true;
    }

    private static int EvalAmount(JsonElement amount, CardInstance card)
    {
        if (amount.ValueKind == JsonValueKind.Number) return amount.GetInt32();
        var race = CardDatabase.ParseEnum<Race>(amount.GetProperty("ifRace").GetString()!);
        return card.Races.Contains(race) ? amount.GetProperty("then").GetInt32() : amount.GetProperty("else").GetInt32();
    }

    private static int EvalCount(JsonElement v, EffectContext ctx) => v.ValueKind switch
    {
        JsonValueKind.Number => v.GetInt32(),
        JsonValueKind.String when v.GetString() == "Any" => 99,
        JsonValueKind.String when v.GetString()!.StartsWith("Count.") => ctx.Counts.GetValueOrDefault(v.GetString()![6..]),
        _ => 0,
    };

    private static int IntOr(JsonElement e, string name, int fallback, EffectContext ctx) =>
        e.TryGetProperty(name, out var v) ? EvalCount(v, ctx) : fallback;

    // ================================================================== small helpers

    private int ControllerOf(CardInstance card) => card.Location.Kind switch
    {
        LocationKind.Node => card.Location.Node!.Side,
        LocationKind.Stack => card.Location.Player,
        LocationKind.Hand => card.Location.Player,
        _ => -1,
    };

    private CardInstance? OpposingCard(CardInstance card) =>
        card.Location.Kind == LocationKind.Node && card.Location.Node is { IsScoring: true } n ? State.OpposingNode(n)?.Card : null;

    private static bool InPlay(CardInstance c) =>
        c.Location.Kind == LocationKind.Node && c.Location.Node is { IsScoring: true } && c.Definition.IsUnit;

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool Bool(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
}
