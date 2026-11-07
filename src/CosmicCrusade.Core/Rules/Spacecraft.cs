using CosmicCrusade.Core.Model;

namespace CosmicCrusade.Core.Rules;

/// <summary>
/// Spacecraft module (design/spacecraft.md). A spacecraft is a Unit (ruling S21) that can carry one crewing Unit on
/// its node (S3). The spacecraft is <see cref="Node.Card"/> and the crew is <see cref="Node.Crew"/>, so everything
/// that reads "the Unit on that node" from the ground (an Opposing-Unit ability, a Duel) meets the spacecraft and
/// misses the elevated crew (S4, S5). Crews are in play: statics, scoring and "any Unit" choices include them.
///   • Boarding (S1, S2): a Unit can be Deployed onto your uncrewed spacecraft, or a spacecraft onto your Unit; only
///     the card being Deployed is Played. "When this becomes crewed" fires however the crew arrives (S24).
///   • Crew class follows the node (S11): on a Leader Node only a Leader or Champion crews; on a Unit Node any Unit
///     allowed there.
///   • A spacecraft leaving play drops its crew to the ground (brief); a crew leaving leaves the spacecraft (S12).
/// </summary>
public static class Spacecraft
{
    public static bool IsShip(CardInstance card) => card.Definition.IsUnit && card.Definition.IsSpacecraft;

    /// <summary>The Unit aboard this spacecraft, or null.</summary>
    public static CardInstance? CrewOf(CardInstance ship) =>
        ship.Location is { Kind: LocationKind.Node, Node: { } n } && ReferenceEquals(n.Card, ship) ? n.Crew : null;

    /// <summary>The spacecraft this Unit is crewing, or null (a ground Unit).</summary>
    public static CardInstance? ShipOf(CardInstance crew) =>
        crew.Location is { Kind: LocationKind.Node, Node: { } n } && ReferenceEquals(n.Crew, crew) ? n.Card : null;

    public static bool IsCrew(CardInstance card) => ShipOf(card) is not null;

    /// <summary>A crewed spacecraft (a spacecraft with a Unit aboard).</summary>
    public static bool IsCrewed(CardInstance card) => IsShip(card) && CrewOf(card) is not null;

    /// <summary>
    /// In the air: a spacecraft or the Unit crewing one. Its effects reach both levels (S6), so an Opposing-Unit
    /// effect from it may pick a crew as well as a spacecraft (S8).
    /// </summary>
    public static bool InAir(CardInstance card) => IsShip(card) || IsCrew(card);

    /// <summary>Is this crew out of reach of the ground right now? (Gravity Well switches elevation off: S19.)</summary>
    public static bool IsElevated(GameState state, CardInstance card) => IsCrew(card) && !StaticLayer.ElevationDisabled(state);

    /// <summary>Can this spacecraft take a crew (Remnant AI, Stagnant Station and Auto-Turret can't)?</summary>
    public static bool Crewable(GameState state, CardInstance ship) => IsShip(ship) && !StaticLayer.CannotBeCrewed(state, ship);

    /// <summary>
    /// Could <paramref name="unit"/> crew <paramref name="ship"/> on <paramref name="node"/>? Class follows the node
    /// (S11); Viscous Fog stops all boarding. Doesn't check whether the ship already has a crew.
    /// </summary>
    public static bool CanCrew(GameState state, CardInstance unit, CardInstance ship, Node node) =>
        unit.Definition.IsUnit && !unit.Definition.IsSpacecraft && !ReferenceEquals(unit, ship)
        && node.IsScoring && Crewable(state, ship)
        && Placement.ClassAllows(unit.Definition, node.Kind)
        && Placement.ClassAllows(ship.Definition, node.Kind)
        && !StaticLayer.BoardingRestricted(state);

    /// <summary>
    /// Can <paramref name="card"/> join what is on <paramref name="node"/> (S1)? A Unit boarding the uncrewed
    /// spacecraft there, or a spacecraft being laid under the lone Unit there. Structural only: the side, Locks and
    /// deploy restrictions are the caller's (a Deploy only goes to your own node).
    /// </summary>
    public static bool CanJoin(GameState state, CardInstance card, Node node)
    {
        if (node.Card is not { } occupant || node.Crew is not null || node.IsLocked || !node.IsScoring) return false;
        if (!card.Definition.IsUnit || !occupant.Definition.IsUnit) return false;
        if (card.Definition.IsSpacecraft == occupant.Definition.IsSpacecraft) return false;
        return card.Definition.IsSpacecraft
            ? CanCrew(state, occupant, card, node)
            : CanCrew(state, card, occupant, node);
    }

    /// <summary>Can the cards on <paramref name="from"/> leave their spacecraft? (Viscous Fog: "Units can't ... leave spacecraft".)</summary>
    public static bool CanLeaveShip(GameState state) => !StaticLayer.BoardingRestricted(state);

    /// <summary>
    /// Ship + crew Rank of everything on a node, for the opposed-pair tiebreak (S23). Null when the node is empty.
    /// A spacecraft whose Rank doesn't count (Dreadnought without a Leader crew) adds 0.
    /// </summary>
    public static int? NodeRank(GameState state, Node node)
    {
        if (node.Card is null) return null;
        int sum = 0;
        foreach (var c in node.Occupants)
            if (!StaticLayer.RankDoesNotCount(state, c)) sum += RankCalculator.GetRank(state, c);
        return sum;
    }

    /// <summary>Races for a Duel tie (S7): a crewed spacecraft counts as having its crew's races too.</summary>
    public static IReadOnlySet<Race> DuelRaces(GameState state, CardInstance card)
    {
        var races = StaticLayer.Races(state, card);
        if (CrewOf(card) is not { } crew) return races;
        var all = new HashSet<Race>(races);
        all.UnionWith(StaticLayer.Races(state, crew));
        return all;
    }

    /// <summary>The empty Unit/Leader nodes of <paramref name="side"/>'s whose uncrewed spacecraft <paramref name="unit"/> could crew.</summary>
    public static List<Node> ShipsToBoard(GameState state, CardInstance unit, int side) =>
        state.Players[side].ScoringNodes
            .Where(n => n.Card is { } ship && IsShip(ship) && n.Crew is null && !n.IsLocked && CanCrew(state, unit, ship, n))
            .ToList();
}
