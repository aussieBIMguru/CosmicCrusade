using System.Text.Json;
using CosmicCrusade.Core.Effects;
using CosmicCrusade.Core.Model;
using CosmicCrusade.Core.Rules;

namespace CosmicCrusade.Core.Engine;

/// <summary>
/// Spacecraft module: the scripted card abilities (design/spacecraft.md). The data-driven ones (Requisitions Unit,
/// Hull Rot, Greased Engines...) run through the effect interpreter; these need choices or moves it doesn't have.
/// </summary>
public sealed partial class GameEngine
{
    /// <summary>The Mothership's token (cards.json "0ST1").</summary>
    internal const string FighterTokenId = "0ST1";

    private async Task RunSpacecraftScriptAsync(string name, JsonElement effect, EffectContext ctx, CancellationToken ct)
    {
        switch (name)
        {
            case "Mothership": await MothershipAsync(ctx, ct); break;
            case "CrewFromPile": await CrewFromPileAsync(effect, ctx, ct); break;
            case "SporePod": await SporePodAsync(ctx, ct); break;
            case "Abduct": await AbductAsync(ctx, ct); break;
            case "SwapCrews": await SwapCrewsAsync(ctx, ct); break;
            case "BeamUp": await BeamUpAsync(ctx, ct); break;
            case "Hijack": await HijackAsync(ctx, ct); break;
            case "GelatinousHull": await GelatinousHullAsync(ctx, ct); break;
            case "Engulf": await EngulfAsync(ctx, ct); break;
            case "ExoFrame": await ExoFrameAsync(ctx, ct); break;
            case "Refit": await RefitAsync(ctx, ct); break;
            case "SelfDestruct": await SelfDestructAsync(ctx, ct); break;
        }
    }

    private Task LogAsync(string message, CancellationToken ct) => EmitAsync(new LogEvent(message), ct);

    private static bool UnitInPlay(CardInstance c) =>
        c.Definition.IsUnit && c.Location is { Kind: LocationKind.Node, Node.IsScoring: true };

    private bool Affectable(CardInstance c, CardInstance source) =>
        ReferenceEquals(c, source) || !StaticLayer.IsImmune(State, c);

    private string CardLabel(CardInstance c) =>
        $"{c.Name} (Rank {(UnitInPlay(c) ? RankCalculator.GetRank(State, c) : RankCalculator.BaseRank(c))})"
        + (c.Location.Node is { } n ? $" – {State.Players[n.Side].Name}'s {n.Label}{(Spacecraft.IsCrew(c) ? " (crew)" : "")}" : "");

    private async Task<CardInstance?> PickCardAsync(int player, CardInstance source, IReadOnlyList<CardInstance> cards,
        string prompt, string purpose, CancellationToken ct, int min = 1)
    {
        if (cards.Count == 0) return null;
        var pick = await ChooseAsync(new ChoiceRequest(player, source, prompt, ChoiceKind.Cards,
            cards.Select(c => new ChoiceOption(CardLabel(c), c)).ToList(), min, 1, purpose), ct);
        return pick.Count > 0 ? cards[pick[0]] : null;
    }

    // ================================================================== Mothership

    /// <summary>
    /// "When you play this spacecraft, put each ground Unit on your side without a spacecraft into a Rank 1 Galactic
    /// Council spacecraft called Fighter." Fighters are tokens (S13): each is laid under the Unit on its node.
    /// Immune Units and Locked nodes are skipped; Viscous Fog stops it.
    /// </summary>
    private async Task MothershipAsync(EffectContext ctx, CancellationToken ct)
    {
        if (StaticLayer.BoardingRestricted(State))
        {
            await LogAsync($"  {ctx.Source.Name}: Units can't board spacecraft right now.", ct);
            return;
        }
        if (!State.Database.TryGet(FighterTokenId, out var fighter))
        {
            await LogAsync($"  {ctx.Source.Name}: the Fighter token is missing from cards.json.", ct);
            return;
        }

        int made = 0;
        foreach (var node in State.Players[ctx.Controller].ScoringNodes.ToList())
        {
            if (node.Card is not { } unit || node.Crew is not null || node.IsLocked) continue;
            if (Spacecraft.IsShip(unit) || !Affectable(unit, ctx.Source)) continue;
            var token = State.CreateInstance(fighter);
            State.MoveToNode(token, node); // laid under the Unit, which becomes its crew
            made++;
            await EmitAsync(new TokenCreatedEvent(token, node, ctx.Source), ct);
            await CrewArrivedAsync(node, ct);
        }
        await LogAsync(made == 0
            ? $"  {ctx.Source.Name}: there are no ground Units to launch."
            : $"  {ctx.Source.Name}: {made} Fighter{(made == 1 ? "" : "s")} launched.", ct);
    }

    // ================================================================== Chrome Steed, Sarcophagar

    /// <summary>
    /// "When you play this spacecraft, you may move a Retired Unit to crew it" (Chrome Steed) / "crew it with a Unit
    /// from the Nullified pile. That Unit becomes a Necrolite" (Sarcophagar). The Unit enters play but isn't Played.
    /// </summary>
    private async Task CrewFromPileAsync(JsonElement effect, EffectContext ctx, CancellationToken ct)
    {
        var ship = ctx.Source;
        if (ship.Location.Node is not { } node || !ReferenceEquals(node.Card, ship) || node.Crew is not null)
        {
            await LogAsync($"  {ship.Name}: it already has a crew.", ct);
            return;
        }
        effect.TryGetProperty("params", out var p);
        var pile = Str(p, "zone") == "NullifiedPile" ? State.NullifiedUnits : State.RetiredUnits;
        var candidates = Enumerable.Reverse(pile)
            .Where(c => c.Definition.IsUnit && !c.Definition.IsSpacecraft && Spacecraft.CanCrew(State, c, ship, node))
            .ToList();
        var unit = await PickCardAsync(ctx.Controller, ship, candidates, $"{ship.Name}: choose a Unit to crew it", "CrewFromPile", ct);
        if (unit is null)
        {
            await LogAsync($"  {ship.Name}: there is no Unit that can crew it.", ct);
            return;
        }
        if (!await BoardAsync(unit, ship, ship, ct)) return;

        if (p.ValueKind == JsonValueKind.Object && p.TryGetProperty("setRaces", out var sr) && sr.ValueKind == JsonValueKind.Array)
        {
            var races = sr.EnumerateArray()
                .Select(r => StaticLayer.TextRace(State, CardDatabase.ParseEnum<Race>(r.GetString()!), ship)).Distinct().ToList();
            unit.Races.Clear();
            foreach (var r in races) unit.Races.Add(r);
            await EmitAsync(new RacesChangedEvent(unit, races, ship), ct);
            await LogAsync($"  {unit.Name} becomes {string.Join("/", races)}.", ct);
        }
    }

    // ================================================================== Spore Pod

    /// <summary>
    /// "When this spacecraft leaves play, Lock its Node and the opposing Node. Units on those Nodes are placed in
    /// their controllers' Standby Zones." Units move before the Lock (Retired with no room); a Leader stays (#36);
    /// Immune Units are unaffected. Both nodes are Locked either way.
    /// </summary>
    private async Task SporePodAsync(EffectContext ctx, CancellationToken ct)
    {
        if (ctx.SelfNode is not { } node) return;
        var nodes = new List<Node> { node };
        if (State.OpposingNode(node) is { } opp) nodes.Add(opp);
        foreach (var n in nodes)
            foreach (var unit in n.Occupants.Reverse().ToList()) // crew first: the spacecraft would only drop it
            {
                if (!n.Holds(unit) || !Affectable(unit, ctx.Source)) continue;
                await MoveToStandbyAsync(unit, n.Side, retireIfNoRoom: true, ct);
            }
        foreach (var n in nodes) await SetLockAsync(n, true, ctx.Source, ct);
    }

    // ================================================================== Abductor

    /// <summary>
    /// "When you play this spacecraft, if it is uncrewed and the Opposing Unit is a ground Unit, move that Unit onto
    /// this spacecraft." It crosses the control line (control follows the side) and stays in play.
    /// </summary>
    private async Task AbductAsync(EffectContext ctx, CancellationToken ct)
    {
        var ship = ctx.Source;
        if (ship.Location.Node is not { } node || !ReferenceEquals(node.Card, ship) || node.Crew is not null)
        {
            await LogAsync($"  {ship.Name}: it already has a crew.", ct);
            return;
        }
        if (State.OpposingNode(node)?.Card is not { } target || Spacecraft.IsShip(target))
        {
            await LogAsync($"  {ship.Name}: there is no ground Unit opposite.", ct);
            return;
        }
        if (!Affectable(target, ship) || !Spacecraft.CanCrew(State, target, ship, node))
        {
            await LogAsync($"  {ship.Name}: {target.Name} can't be taken aboard.", ct);
            return;
        }
        await BoardAsync(target, ship, ship, ct);
    }

    // ================================================================== Mindscrambler

    /// <summary>
    /// "When you play this spacecraft, you may switch the crews of any 2 spacecraft." One of the two may be uncrewed
    /// (its crew just moves). Each crew must be able to crew its new spacecraft (S11); Immune crews don't move.
    /// </summary>
    private async Task SwapCrewsAsync(EffectContext ctx, CancellationToken ct)
    {
        if (StaticLayer.BoardingRestricted(State))
        {
            await LogAsync($"  {ctx.Source.Name}: Units can't board or leave spacecraft right now.", ct);
            return;
        }
        var ships = State.AllUnitsInPlay.Where(Spacecraft.IsShip).ToList();
        bool CanSwap(CardInstance a, CardInstance b)
        {
            var na = a.Location.Node!;
            var nb = b.Location.Node!;
            var ca = na.Crew;
            var cb = nb.Crew;
            if (ReferenceEquals(a, b) || (ca is null && cb is null)) return false;
            if (ca is not null && (!Affectable(ca, ctx.Source) || !Spacecraft.CanCrew(State, ca, b, nb))) return false;
            if (cb is not null && (!Affectable(cb, ctx.Source) || !Spacecraft.CanCrew(State, cb, a, na))) return false;
            return true;
        }

        var firsts = ships.Where(a => ships.Any(b => CanSwap(a, b))).ToList();
        var first = await PickCardAsync(ctx.Controller, ctx.Source, firsts,
            $"{ctx.Source.Name}: choose a spacecraft whose crew switches", "SwapCrewsFrom", ct);
        if (first is null)
        {
            await LogAsync($"  {ctx.Source.Name}: no crews can be switched.", ct);
            return;
        }
        var partners = ships.Where(b => CanSwap(first, b)).ToList();
        var second = await PickCardAsync(ctx.Controller, ctx.Source, partners,
            $"{ctx.Source.Name}: switch crews with which spacecraft?", "SwapCrewsTo", ct);
        if (second is null) return;

        var nodeA = first.Location.Node!;
        var nodeB = second.Location.Node!;
        var crewA = nodeA.Crew;
        var crewB = nodeB.Crew;
        if (crewA is not null) State.RemoveFromCurrentLocation(crewA);
        if (crewB is not null) State.RemoveFromCurrentLocation(crewB);
        if (crewA is not null) State.MoveToNode(crewA, nodeB);
        if (crewB is not null) State.MoveToNode(crewB, nodeA);
        if (crewA is not null) await EmitAsync(new CrewBoardedEvent(crewA, second, nodeB, nodeA, null, ctx.Source), ct);
        if (crewB is not null) await EmitAsync(new CrewBoardedEvent(crewB, first, nodeA, nodeB, null, ctx.Source), ct);
        await LogAsync($"  {ctx.Source.Name}: the crews of {first.Name} and {second.Name} switch.", ct);
        if (crewB is not null) await CrewArrivedAsync(nodeA, ct);
        if (crewA is not null) await CrewArrivedAsync(nodeB, ct);
    }

    // ================================================================== Beam Up

    /// <summary>"Move one of your ground Units onto an uncrewed spacecraft you control. It isn't Played again."</summary>
    private async Task BeamUpAsync(EffectContext ctx, CancellationToken ct)
    {
        int me = ctx.Controller;
        var units = State.Players[me].UnitsInPlay
            .Where(u => !Spacecraft.IsShip(u) && !Spacecraft.IsCrew(u) && Affectable(u, ctx.Source)
                        && Spacecraft.ShipsToBoard(State, u, me).Count > 0)
            .ToList();
        var unit = await PickCardAsync(me, ctx.Source, units, $"{ctx.Source.Name}: choose a ground Unit to beam up", "BeamUpUnit", ct);
        if (unit is null)
        {
            await LogAsync($"  {ctx.Source.Name}: no ground Unit can board one of your spacecraft.", ct);
            return;
        }
        var ships = Spacecraft.ShipsToBoard(State, unit, me).Select(n => n.Card!).ToList();
        var ship = await PickCardAsync(me, ctx.Source, ships, $"{ctx.Source.Name}: beam {unit.Name} up to which spacecraft?", "BeamUpShip", ct);
        if (ship is not null) await BoardAsync(unit, ship, ctx.Source, ct);
    }

    // ================================================================== Hijack

    /// <summary>
    /// "Gain control of a spacecraft as it is being Deployed. It is Deployed to one of your Empty Nodes instead."
    /// The issuer picks the node (one its class allows). The spacecraft is then Played on your side, for you.
    /// </summary>
    private async Task HijackAsync(EffectContext ctx, CancellationToken ct)
    {
        if (ctx.EventItem is not { Kind: StackItemKind.DeployUnit, Cancelled: false, TargetNode: not null } item
            || item.Card.Location.Kind != LocationKind.Stack || !item.Card.Definition.IsSpacecraft)
        {
            await LogAsync($"  {ctx.Source.Name}: the spacecraft is no longer being Deployed.", ct);
            return;
        }
        int me = ctx.Controller;
        var nodes = State.Players[me].ScoringNodes.Where(n => n.IsEmpty && Placement.ClassAllows(item.Card.Definition, n.Kind)).ToList();
        if (nodes.Count == 0)
        {
            await LogAsync($"  {ctx.Source.Name}: you have no Empty node for {item.Card.Name}.", ct);
            return;
        }
        var pick = await ChooseAsync(new ChoiceRequest(me, ctx.Source, $"{ctx.Source.Name}: Deploy {item.Card.Name} to which of your nodes?",
            ChoiceKind.Nodes, nodes.Select(n => new ChoiceOption(NodeLabel(n, me), Node: n)).ToList(), 1, 1, "HijackNode"), ct);
        var to = nodes[pick.Count > 0 ? pick[0] : 0];

        int old = item.Controller;
        var from = item.TargetNode!;
        item.Controller = me;
        item.TargetNode = to;
        item.RidingCrew = null; // a crew waiting in Standby stays there
        item.Description = $"Deploy {item.Card.Name} to {to.Label}";
        State.MoveToStack(item.Card, me);
        await EmitAsync(new StackItemControlChangedEvent(item, old, me, ctx.Source), ct);
        await EmitAsync(new DeployRedirectedEvent(item, from, to, ctx.Source), ct);
    }

    // ================================================================== Gelatinous Hull

    /// <summary>"When this spacecraft becomes crewed, Manipulate 1. If its crew is a Blaarg, Manipulate 2 instead."</summary>
    private async Task GelatinousHullAsync(EffectContext ctx, CancellationToken ct)
    {
        var crew = Spacecraft.CrewOf(ctx.Source);
        int n = crew is not null && StaticLayer.HasRace(State, crew, StaticLayer.TextRace(State, Race.Blaarg, ctx.Source)) ? 2 : 1;
        using var doc = JsonDocument.Parse($"{{\"op\":\"Manipulate\",\"amount\":{n}}}");
        await Runner.RunSingleOpAsync(doc.RootElement, ctx, ct);
    }

    // ================================================================== Engulf

    /// <summary>"Nullify a spacecraft. Its crew stays on the Node and becomes a Blaarg" (only if it was Nullified).</summary>
    private async Task EngulfAsync(EffectContext ctx, CancellationToken ct)
    {
        var ships = State.AllUnitsInPlay.Where(u => Spacecraft.IsShip(u) && Affectable(u, ctx.Source)).ToList();
        var ship = await PickCardAsync(ctx.Controller, ctx.Source, ships, $"{ctx.Source.Name}: choose a spacecraft to Nullify", "Nullify", ct);
        if (ship is null)
        {
            await LogAsync($"  {ctx.Source.Name}: there is no spacecraft to Nullify.", ct);
            return;
        }
        var crew = Spacecraft.CrewOf(ship);
        bool gone = await NullifyAsync(ship, ctx.Source, ct);
        if (!gone || crew is null || !UnitInPlay(crew) || !Affectable(crew, ctx.Source)) return;
        var blaarg = StaticLayer.TextRace(State, Race.Blaarg, ctx.Source);
        crew.Races.Clear();
        crew.Races.Add(blaarg);
        await EmitAsync(new RacesChangedEvent(crew, new[] { blaarg }, ctx.Source), ct);
        await LogAsync($"  {crew.Name} drops to the ground and becomes a {blaarg}.", ct);
    }

    // ================================================================== Exo-Frame

    /// <summary>"If this spacecraft leaves play, Assemble its Rank onto its crew." Uses the Rank it had as it left.</summary>
    private async Task ExoFrameAsync(EffectContext ctx, CancellationToken ct)
    {
        if (!ctx.Memory.TryGetValue("$crew", out var c) || c is not CardInstance crew || !UnitInPlay(crew))
        {
            await LogAsync($"  {ctx.Source.Name}: it had no crew to pass its Rank to.", ct);
            return;
        }
        if (!Affectable(crew, ctx.Source)) return;
        int rank = ctx.Memory.TryGetValue("$rank", out var r) && r is int i ? i : RankCalculator.BaseRank(ctx.Source);
        int amount = rank * StaticLayer.AssembleMultiplier(State, ctx.Controller);
        crew.PersistentModifiers.Add(new RankModifier($"Assemble: {ctx.Source.Name}", amount));
        await EmitAsync(new RankChangedEvent(crew, $"{amount:+0;-0;0} Rank (Assembled from {ctx.Source.Name})", ctx.Source,
            RankChangeKind.Assembled), ct);
        await RaiseAsync(new TriggerEvent("Assembled", ctx.Controller, ctx.Source, AssembledRank: amount, AssembleTarget: crew), ct);
    }

    // ================================================================== Refit

    /// <summary>
    /// "Retire a spacecraft card from your Hand. A spacecraft in play gets +X Rank, where X is that card's Base Rank."
    /// </summary>
    private async Task RefitAsync(EffectContext ctx, CancellationToken ct)
    {
        int me = ctx.Controller;
        var hand = State.Players[me].Hand.Where(c => c.Definition.IsUnit && c.Definition.IsSpacecraft).ToList();
        var part = await PickCardAsync(me, ctx.Source, hand, $"{ctx.Source.Name}: choose a spacecraft card to Retire from your hand", "RetireFromHand", ct);
        if (part is null)
        {
            await LogAsync($"  {ctx.Source.Name}: you have no spacecraft card in hand.", ct);
            return;
        }
        int x = RankCalculator.BaseRank(part);
        await RetireFromHandAsync(part, ct);

        var ships = State.AllUnitsInPlay.Where(u => Spacecraft.IsShip(u) && Affectable(u, ctx.Source)).ToList();
        var ship = await PickCardAsync(me, ctx.Source, ships, $"{ctx.Source.Name}: which spacecraft gets +{x} Rank?",
            x >= 0 ? "Buff" : "Debuff", ct);
        if (ship is null) return;
        ship.PersistentModifiers.Add(new RankModifier(ctx.Source.Name, x));
        await EmitAsync(new RankChangedEvent(ship, $"{x:+0;-0;0} Rank", ctx.Source), ct);
    }

    // ================================================================== Self-Destruct

    /// <summary>
    /// "Issue this Order as a spacecraft is Nullified to Nullify the opposing spacecraft and its crew as well."
    /// Nothing happens when no spacecraft is opposite.
    /// </summary>
    private async Task SelfDestructAsync(EffectContext ctx, CancellationToken ct)
    {
        var item = ctx.EventItem;
        var node = item?.TargetNode ?? item?.Card.Location.Node;
        if (node is null || State.OpposingNode(node)?.Card is not { } opp || !Spacecraft.IsShip(opp))
        {
            await LogAsync($"  {ctx.Source.Name}: there is no spacecraft opposite.", ct);
            return;
        }
        var crew = Spacecraft.CrewOf(opp);
        await LogAsync($"  {ctx.Source.Name}: {opp.Name} goes down with it.", ct);
        if (crew is not null && Affectable(crew, ctx.Source)) await NullifyAsync(crew, ctx.Source, ct);
        if (UnitInPlay(opp) && Affectable(opp, ctx.Source)) await NullifyAsync(opp, ctx.Source, ct);
    }
}
