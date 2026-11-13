using System.Text.Json;
using CosmicCrusade.Core.Model;

namespace CosmicCrusade.Core.Rules;

/// <summary>
/// M3.2: continuous ("Static") effects of cards in play – Units on Unit/Leader nodes, Ongoing Orders on Order nodes
/// and the Active Warzone(s). Nothing is stored: every query re-reads the board, so an anthem starts and stops the
/// moment its source enters or leaves play.
///
/// Each Static effect in cards.json is compiled once into a <see cref="Ability"/>. Like <c>EffectSupport</c>, the
/// rule is all-or-nothing: an effect with any key, op, filter, amount or condition this layer doesn't understand
/// compiles to nothing and stays inert, so a card never does half of what it says.
///
/// What this layer answers:
///   • Rank layers 2–3 (§7.4): static Base Rank overrides (Primordium, The Voidslime, Cure Failure 349) and static
///     Rank modifiers (anthems, debuffs, conditional and counted bonuses, Acid Rain's floor), Polarity Storm.
///   • Current races (Consortium, The Voidslime).
///   • Immunity (Vigan), "cannot be Nullified" (Diplomatic Immunity, Force Field, No Gods, No Masters).
///   • Suppression (Suppressing Goop, Atmosphere Generator, Refraction Network) on top of Gloop!'s flag.
///   • Rule changes: Potency Tank, The Oculus, Extreme Sanctions, Temporal Stasis, Even Odds, Mind Games,
///     Military Academy.
/// PlacementOverride and GrantKeyword compile as "handled elsewhere" (Placement, AssembleRules).
/// </summary>
public static class StaticLayer
{
    // ================================================================== compiled model

    internal enum Op
    {
        ModifyRank, SetBaseRank, SetRaces, Immunity, CannotBeNullified, Suppress, ModifyManipulate,
        InvertRankModifiers, InvertRoundWinner, RestrictPlay, RestrictDeploy, LimitTurns, PlayFromZone, HandledElsewhere,
        // M3.2c
        RevealHands, OverrideDecision, GrantAction, ReplaceRaceWords,
        // Archons
        ExtraAction, CountAsZero, OfferCopyOfSelf,
        // Spacecraft
        CannotBeCrewed, RestrictBoarding, DisableElevation, RevealTopCards, ModifyAssembledRank, RankDoesNotCount,
    }

    internal sealed class Filter
    {
        public bool Self;
        public string[]? Zones;          // null = InPlay
        public CardKind? Kind;
        public string? Race;             // a race name or "Remembered.<key>"
        public string? NotRace;
        public UnitClass? UnitClass;
        public string? Name;
        public string? Controller;       // "You" / "Opponent" ("owner" is read as controller: ruling #15)
        public bool ExcludeSelf;
        public bool AlliedEach, SharesRaceEach, ExcludeEach;
        // Spacecraft
        public bool Crew;                // "Crew": the Unit aboard the source spacecraft
        public bool? Spacecraft;         // is / isn't a spacecraft
        public bool? Crewed;             // a spacecraft with / without a crew (never matches a non-spacecraft)
        public bool? InSpacecraft;       // a Unit aboard a spacecraft / on the ground
    }

    internal sealed class Amount
    {
        public int? Const;
        public Filter? CountOf;
        public Amount? Negate;
        public bool MaxHandOpponents;
        public bool RankOfSelf;
        public int Times = 1;            // Earthrise: "+2 Rank for each ..."
        public bool BaseRankOfCrew;      // High Roller
    }

    internal sealed class Condition
    {
        public Filter? Exists;
        public bool YourLeaderNodeEmpty;
        // Spacecraft: about the source spacecraft's crew.
        public bool? Crewed;
        public UnitClass[]? CrewClassNot;   // Dreadnought: no crew, or one that isn't a Leader/Champion
        public string? CrewRace;            // Blazing Saddle
    }

    internal sealed class Ability
    {
        public required Op Op;
        public Filter? Targets;
        public Amount? Amount;
        public int? Floor;
        public Race[] Races = Array.Empty<Race>();
        public bool SuppressAll;          // false = only "abilities that trigger when cards are Played"
        public string[] Actions = Array.Empty<string>();
        public string? Rule;
        public string? ForPlayer;
        public int Max;
        public int Add;
        public PileKind? Pile;            // PlayFromZone
        public bool EachPlayer;           // PlayFromZone: "player": "Each"
        public Condition? Condition;
        public required string Text;      // short description for logs / inspect
        // M3.2c
        public bool AllPlayers;           // RevealHands "All" (else "Opponents"); GrantAction "Each" (else "You")
        public bool RandomDecider;        // OverrideDecision: "Random" (else the source's controller decides)
        public string? ActionName;        // GrantAction
        public JsonElement Payload;       // GrantAction: the op itself ("do" is run by the EffectRunner)
        public string? CostRace;          // GrantAction NullifyFromHand filter
        public CardKind? CostKind;
        public int Multiplier = 1;        // ModifyAssembledRank (The Crucible)
    }

    /// <summary>A card whose Static effects could apply right now, and who controls it (-1 for a Warzone).</summary>
    internal readonly record struct Source(CardInstance Card, int Controller, Ability[] Abilities);

    // ================================================================== public queries

    /// <summary>True when every Static effect of the card compiled (false when it has none).</summary>
    public static bool HasLiveStatics(CardDefinition def) =>
        StaticEffectCount(def) > 0 && Compiled(def).Inert.Count == 0;

    /// <summary>Static effect entries this layer can't run yet (for coverage tests and logs).</summary>
    public static IReadOnlyList<string> InertStatics(CardDefinition def) => Compiled(def).Inert;

    public static bool IsImmune(GameState s, CardInstance card) => ImmunityGranter(s, card) is not null;

    /// <summary>
    /// The card that makes this Unit Immune: the Unit itself (Vigan) or another card granting it (Deify: "Your Leader
    /// ... is Immune"). The granter's own effects still apply to it. Null when the Unit isn't Immune.
    /// </summary>
    internal static CardInstance? ImmunityGranter(GameState s, CardInstance card)
    {
        if (!card.Definition.IsUnit || !IsUnitInPlay(card)) return null;
        if (!card.AbilitiesSuppressed && Compiled(card.Definition).Abilities.Any(a => a.Op == Op.Immunity && a.Targets is { Self: true }))
            return card;
        if (_inImmune) return null;
        _inImmune = true;
        try
        {
            foreach (var src in CandidateSources(s))
            {
                if (src.Card.AbilitiesSuppressed || ReferenceEquals(src.Card, card)) continue;
                foreach (var ab in src.Abilities)
                    if (ab.Op == Op.Immunity && ab.Targets is { Self: false } && CondOk(s, ab, src)
                        && Matches(s, ab.Targets, card, src) && !IsSuppressed(s, src.Card))
                        return src.Card;
            }
            return null;
        }
        finally { _inImmune = false; }
    }

    // ------------------------------------------------------------------ Spacecraft

    /// <summary>Remnant AI, Stagnant Station, Auto-Turret: "cannot be crewed" (Ion Storm switches it off with the rest).</summary>
    public static bool CannotBeCrewed(GameState s, CardInstance ship)
    {
        foreach (var src in ActiveSources(s))
            foreach (var ab in src.Abilities)
                if (ab.Op == Op.CannotBeCrewed && Applies(s, ab, src, ship)) return true;
        // A spacecraft in hand isn't a source yet: read its own text (unless Suppressed, e.g. Suppressing Goop).
        if (!IsInPlay(ship) && !IsSuppressed(s, ship))
            foreach (var ab in Compiled(ship.Definition).Abilities)
                if (ab.Op == Op.CannotBeCrewed && ab.Targets is { Self: true }) return true;
        return false;
    }

    /// <summary>Viscous Fog: "Units can't board or leave spacecraft."</summary>
    public static bool BoardingRestricted(GameState s) => AnyActive(s, Op.RestrictBoarding, out _);

    /// <summary>Gravity Well: "Spacecraft and their crews can be affected from the ground" (elevation off, S19).</summary>
    public static bool ElevationDisabled(GameState s) => AnyActive(s, Op.DisableElevation, out _);

    /// <summary>Research Vessel: may <paramref name="player"/> look at the top card of each deck?</summary>
    public static bool CanSeeTopOfDecks(GameState s, int player)
    {
        foreach (var src in ActiveSources(s))
            foreach (var ab in src.Abilities)
                if (ab.Op == Op.RevealTopCards && src.Controller == player && CondOk(s, ab, src)) return true;
        return false;
    }

    /// <summary>The Crucible: Rank <paramref name="player"/> Assembles is multiplied (doubled per active source).</summary>
    public static int AssembleMultiplier(GameState s, int player)
    {
        int m = 1;
        foreach (var src in ActiveSources(s))
            foreach (var ab in src.Abilities)
                if (ab.Op == Op.ModifyAssembledRank && src.Controller == player && CondOk(s, ab, src)) m *= Math.Max(1, ab.Multiplier);
        return m;
    }

    /// <summary>Dreadnought: "This spacecraft's Rank only counts if it is crewed by a Leader or Champion" (total and pairs).</summary>
    public static bool RankDoesNotCount(GameState s, CardInstance card)
    {
        if (!IsUnitInPlay(card) || card.AbilitiesSuppressed || IsSuppressed(s, card)) return false;
        foreach (var ab in Compiled(card.Definition).Abilities)
            if (ab.Op == Op.RankDoesNotCount && ab.Targets is { Self: true }
                && CondOk(s, ab, new Source(card, card.Location.Node!.Side, Compiled(card.Definition).Abilities)))
                return true;
        return false;
    }

    // ------------------------------------------------------------------ Archons

    /// <summary>Open Source: how many additional actions <paramref name="player"/> may take each turn.</summary>
    public static int ExtraActions(GameState s, int player)
    {
        int n = 0;
        foreach (var src in ActiveSources(s))
            foreach (var ab in src.Abilities)
                if (ab.Op == Op.ExtraAction && src.Controller == player && CondOk(s, ab, src)) n += Math.Max(1, ab.Max);
        return n;
    }

    /// <summary>The Unnameable: this Unit counts as 0 towards its controller's total Rank (Immune Units are unaffected).</summary>
    public static bool CountsAsZero(GameState s, CardInstance card)
    {
        if (!IsUnitInPlay(card) || IsImmune(s, card)) return false;
        foreach (var src in ActiveSources(s))
            foreach (var ab in src.Abilities)
                if (ab.Op == Op.CountAsZero && Applies(s, ab, src, card)) return true;
        return false;
    }

    /// <summary>
    /// Androsiar: Units in play that may have <paramref name="entering"/> enter play as a copy of themselves, in board
    /// order. Their controller decides. Immune Units can't be made copies.
    /// </summary>
    public static List<CardInstance> CopyOfferSources(GameState s, CardInstance entering)
    {
        var list = new List<CardInstance>();
        if (IsImmune(s, entering)) return list;
        foreach (var src in ActiveSources(s))
        {
            if (ReferenceEquals(src.Card, entering) || !src.Card.Definition.IsUnit) continue;
            if (src.Abilities.Any(ab => ab.Op == Op.OfferCopyOfSelf && Applies(s, ab, src, entering)))
                list.Add(src.Card);
        }
        return list;
    }

    /// <summary>All of the card's abilities and effects are ignored (Gloop!'s flag, Suppressing Goop...).</summary>
    public static bool IsSuppressed(GameState s, CardInstance card)
    {
        if (card.AbilitiesSuppressed) return true;
        if (_inSuppress) return false;
        if (IsImmune(s, card)) return false; // Immune: unaffected by other effects, static ones included.
        _inSuppress = true;
        try
        {
            foreach (var (src, ab) in ActiveSuppressors(s))
                if (ab.SuppressAll && !ReferenceEquals(src.Card, card) && Matches(s, ab.Targets!, card, src))
                    return true;
            return false;
        }
        finally { _inSuppress = false; }
    }

    private static readonly HashSet<string> PlayedTriggers = new()
    {
        "OnPlay", "OnEvent:UnitPlayed", "OnEvent:OrderPlayed", "OnEvent:CardPlayed", "OnEvent:CardPlayedFromHand",
    };

    /// <summary>
    /// Is this trigger of the card ignored? Refraction Network only stops abilities that trigger when cards are
    /// Played ("OnPlay", and "OnEvent:UnitPlayed" / OrderPlayed / CardPlayed...).
    /// </summary>
    public static bool IsTriggerSuppressed(GameState s, CardInstance card, string trigger)
    {
        if (IsSuppressed(s, card)) return true;
        if (!PlayedTriggers.Contains(trigger) || IsImmune(s, card)) return false;
        foreach (var (src, ab) in ActiveSuppressors(s))
            if (!ab.SuppressAll && !ReferenceEquals(src.Card, card) && Matches(s, ab.Targets!, card, src))
                return true;
        return false;
    }

    /// <summary>The card's current races after static race changes (Consortium, The Voidslime).</summary>
    public static IReadOnlySet<Race> Races(GameState s, CardInstance card)
    {
        if (_inRaces) return card.Races;
        // Stagnation: every card's race is Blaarg (Immune Units keep theirs). It overrides Consortium / The Voidslime.
        if (ReplacementRace(s) is Race replaced && !IsImmune(s, card)) return new HashSet<Race> { replaced };
        if (!card.Definition.IsUnit) return card.Races;
        if (!AnyOp(s, Op.SetRaces) || IsImmune(s, card)) return card.Races;
        _inRaces = true;
        try
        {
            IReadOnlySet<Race>? result = null;
            foreach (var src in ActiveSources(s))
                foreach (var ab in src.Abilities)
                    if (ab.Op == Op.SetRaces && Applies(s, ab, src, card))
                        result = new HashSet<Race>(ab.Races);
            return result ?? card.Races;
        }
        finally { _inRaces = false; }
    }

    public static bool HasRace(GameState s, CardInstance card, Race race) => Races(s, card).Contains(race);

    public static bool SharesRace(GameState s, CardInstance a, CardInstance b) => Races(s, a).Overlaps(Races(s, b));

    /// <summary>Layer 2 statics: the Base Rank after static overrides (the card's own first, then others').</summary>
    internal static int ApplyBaseOverrides(GameState s, CardInstance card, int printed)
    {
        int rank = printed;
        if (!AnyOp(s, Op.SetBaseRank)) return rank;
        bool immune = IsImmune(s, card);
        var sources = ActiveSources(s).ToList();
        foreach (var src in sources.Where(x => ReferenceEquals(x.Card, card)).Concat(sources.Where(x => !ReferenceEquals(x.Card, card))))
        {
            if (immune && !ReferenceEquals(src.Card, card)) continue;
            foreach (var ab in src.Abilities)
                if (ab.Op == Op.SetBaseRank && Applies(s, ab, src, card))
                    rank = Eval(s, ab.Amount!, src, card);
        }
        return rank;
    }

    /// <summary>Layer 3: the sum of static Rank modifiers on the card, and the highest floor among them.</summary>
    internal static (int Sum, int? Floor) RankModifiers(GameState s, CardInstance card)
    {
        int sum = 0;
        int? floor = null;
        if (!AnyOp(s, Op.ModifyRank)) return (0, null);
        var granter = ImmunityGranter(s, card);
        foreach (var src in ActiveSources(s))
        {
            if (granter is not null && !ReferenceEquals(src.Card, card) && !ReferenceEquals(src.Card, granter)) continue;
            foreach (var ab in src.Abilities)
            {
                if (ab.Op != Op.ModifyRank || !Applies(s, ab, src, card)) continue;
                sum += Eval(s, ab.Amount!, src, card);
                if (ab.Floor is int f) floor = floor is int g ? Math.Max(f, g) : f;
            }
        }
        return (sum, floor);
    }

    /// <summary>Polarity Storm: Rank increases become decreases and vice versa (not for Immune Units).</summary>
    internal static bool RankModifiersInverted(GameState s, CardInstance card) =>
        AnyActive(s, Op.InvertRankModifiers, out _) && !IsImmune(s, card);

    /// <summary>Human-readable list of the statics currently changing this card (for Inspect and logs).</summary>
    public static IReadOnlyList<string> Explain(GameState s, CardInstance card)
    {
        var lines = new List<string>();
        if (IsImmune(s, card)) lines.Add("Immune to other effects");
        if (IsSuppressed(s, card)) lines.Add("Abilities Suppressed");
        if (ReplacementRace(s) is Race words)
            lines.Add(IsImmune(s, card) ? "Immune: keeps its Race and Race words" : $"Race words (and its Race) read as {words}");
        if (!IsUnitInPlay(card)) return lines;
        var granter = ImmunityGranter(s, card);
        if (CountsAsZero(s, card)) lines.Add("Counts as 0 towards its side's total Rank");
        foreach (var src in ActiveSources(s))
        {
            if (granter is not null && !ReferenceEquals(src.Card, card) && !ReferenceEquals(src.Card, granter)) continue;
            foreach (var ab in src.Abilities)
            {
                if (!Applies(s, ab, src, card)) continue;
                string who = ReferenceEquals(src.Card, card) ? "own ability" : src.Card.Name;
                switch (ab.Op)
                {
                    case Op.ModifyRank:
                        lines.Add($"{who}: {Eval(s, ab.Amount!, src, card):+0;-0;+0} Rank{(ab.Floor is int f ? $" (not below {f})" : "")}");
                        break;
                    case Op.SetBaseRank:
                        lines.Add($"{who}: Base Rank {Eval(s, ab.Amount!, src, card)}");
                        break;
                    case Op.SetRaces:
                        lines.Add($"{who}: {(ab.Races.Length == 0 ? "no Race" : "is " + string.Join("/", ab.Races))}");
                        break;
                    case Op.CannotBeNullified:
                        lines.Add($"{who}: cannot be Nullified");
                        break;
                }
            }
        }
        if (RankModifiersInverted(s, card)) lines.Add("Polarity Storm: Rank changes are inverted");
        return lines;
    }

    /// <summary>
    /// Would Nullifying this Unit (by <paramref name="by"/>) be prevented? Only Units in play are protected; a Unit
    /// being Deployed has no abilities and is not in play (Shadow Strike / Target Practice errata).
    /// </summary>
    public static bool PreventsNullify(GameState s, CardInstance card, CardInstance? by, out string reason)
    {
        reason = "";
        if (!IsUnitInPlay(card)) return false;
        if (IsImmune(s, card) && !ReferenceEquals(by, card))
        {
            reason = "it is Immune";
            return true;
        }
        if (!AnyOp(s, Op.CannotBeNullified)) return false;
        foreach (var src in ActiveSources(s))
            foreach (var ab in src.Abilities)
                if (ab.Op == Op.CannotBeNullified && Applies(s, ab, src, card))
                {
                    reason = ReferenceEquals(src.Card, card) ? "it cannot be Nullified" : $"{src.Card.Name}: it cannot be Nullified";
                    return true;
                }
        return false;
    }

    /// <summary>Potency Tank: extra magnitude for <paramref name="player"/>'s Manipulate effects.</summary>
    public static int ManipulateBonus(GameState s, int player)
    {
        int add = 0;
        foreach (var src in ActiveSources(s))
            foreach (var ab in src.Abilities)
                if (ab.Op == Op.ModifyManipulate && src.Controller == player && CondOk(s, ab, src)) add += ab.Add;
        return add;
    }

    /// <summary>The Oculus: the player with the lowest total wins instead.</summary>
    public static bool RoundWinnerInverted(GameState s) => AnyActive(s, Op.InvertRoundWinner, out _);

    /// <summary>Extreme Sanctions / Temporal Stasis: may this card be Deployed ("Deploy") or Issued ("Issue")?</summary>
    public static bool IsPlayRestricted(GameState s, CardInstance card, string action, out CardInstance? by)
    {
        by = null;
        foreach (var src in ActiveSources(s))
            foreach (var ab in src.Abilities)
            {
                if (ab.Op != Op.RestrictPlay || !ab.Actions.Contains(action) || !CondOk(s, ab, src)) continue;
                if (ReferenceEquals(src.Card, card)) continue; // "No other Orders"
                if (!Matches(s, ab.Targets!, card, src, ignoreZone: true)) continue;
                by = src.Card;
                return true;
            }
        return false;
    }

    /// <summary>
    /// Static deploy rules for one Deploy (from hand, Standby or a pile) by <paramref name="player"/> to
    /// <paramref name="node"/>: Extreme Sanctions, Even Odds and Military Academy. (Mind Games filters the whole
    /// list of options: see <see cref="MustDeployToOpposedNodes"/>.)
    /// </summary>
    public static bool DeployAllowed(GameState s, int player, CardInstance card, Node node)
    {
        if (IsPlayRestricted(s, card, "Deploy", out _)) return false;
        if (DeployTurnLimitReached(s, player)) return false;
        return NodeAllowsDeploy(s, card, node);
    }

    /// <summary>
    /// The node-specific part of <see cref="DeployAllowed"/> (Even Odds). Recolonize uses it on a Deploy that is
    /// already under way: the card was allowed to be Deployed and the Deployment turn has been counted already.
    /// </summary>
    public static bool NodeAllowsDeploy(GameState s, CardInstance card, Node node)
    {
        foreach (var src in ActiveSources(s))
            foreach (var ab in src.Abilities)
            {
                if (ab.Op != Op.RestrictDeploy || ab.Rule != "IfTargetOpposed_BaseRankMustEqualOpposing" || !CondOk(s, ab, src)) continue;
                if (s.OpposingNode(node)?.Card is { } opposing
                    && RankCalculator.BaseRank(card) != RankCalculator.CurrentBaseRank(s, opposing))
                    return false;
            }
        return true;
    }

    /// <summary>
    /// Barkeep Bill, Galactomancer, Hills of Decay: Units <paramref name="player"/> may Deploy from
    /// <paramref name="pile"/> "as if they were in your hand" (top of the pile first).
    /// </summary>
    public static List<CardInstance> PlayableFromPile(GameState s, int player, PileKind pile)
    {
        var result = new List<CardInstance>();
        foreach (var src in ActiveSources(s))
            foreach (var ab in src.Abilities)
            {
                if (ab.Op != Op.PlayFromZone || ab.Pile != pile || !CondOk(s, ab, src)) continue;
                if (!ab.EachPlayer && src.Controller != player) continue;
                foreach (var c in Enumerable.Reverse(s.GetPile(pile)))
                    if (c.Definition.IsUnit && !result.Contains(c) && Matches(s, ab.Targets!, c, src, ignoreZone: true))
                        result.Add(c);
            }
        return result;
    }

    /// <summary>Military Academy: has this player used all their Deployment turns this round?</summary>
    public static bool DeployTurnLimitReached(GameState s, int player)
    {
        foreach (var src in ActiveSources(s))
            foreach (var ab in src.Abilities)
                if (ab.Op == Op.LimitTurns && CondOk(s, ab, src) && s.Players[player].DeployTurnsThisRound >= ab.Max)
                    return true;
        return false;
    }

    /// <summary>Mind Games: must this player Deploy to one of their own Opposed nodes if able?</summary>
    public static bool MustDeployToOpposedNodes(GameState s, int player)
    {
        foreach (var src in ActiveSources(s))
            foreach (var ab in src.Abilities)
                if (ab.Op == Op.RestrictDeploy && ab.Rule == "MustTargetOwnOpposedNodeIfAble" && CondOk(s, ab, src)
                    && src.Controller >= 0 && player == (ab.ForPlayer == "Opponent" ? GameState.Other(src.Controller) : src.Controller))
                    return true;
        return false;
    }

    // ------------------------------------------------------------------ M3.2c: hidden information

    /// <summary>
    /// The All-knowing / Transparency ("Your Opponents play with their Hands revealed") and Oracle's Arch ("Players
    /// play with their Hands revealed"): is <paramref name="player"/>'s hand face up for everyone?
    /// </summary>
    public static bool IsHandRevealed(GameState s, int player)
    {
        foreach (var src in ActiveSources(s))
            foreach (var ab in src.Abilities)
                if (ab.Op == Op.RevealHands && CondOk(s, ab, src)
                    && (ab.AllPlayers || (src.Controller >= 0 && src.Controller != player)))
                    return true;
        return false;
    }

    /// <summary>Can <paramref name="viewer"/> see <paramref name="card"/>'s face? (Hands are hidden unless revealed.)</summary>
    public static bool CanSee(GameState s, int viewer, CardInstance card) =>
        card.Location.Kind != LocationKind.Hand || card.Location.Player == viewer || IsHandRevealed(s, card.Location.Player);

    // ------------------------------------------------------------------ M3.2c: deploy node overrides

    /// <summary>Who picks the node when <see cref="DeployNodeOverride"/> applies.</summary>
    /// <param name="Random">Shoot First, Think Second: the node is picked at random.</param>
    /// <param name="Chooser">Psychic Coercion: this player picks the node before the Unit is chosen.</param>
    public sealed record DeployOverride(CardInstance Source, bool Random, int Chooser);

    /// <summary>
    /// Shoot First, Think Second ("Your Opponent selects Nodes as they Deploy Units at random") and Psychic Coercion
    /// ("You choose which Nodes your opponents Deploy their Units to, before you know what Unit"). Applies to Deploys
    /// that choose a node: from hand and from piles. A Standby advance has only one node, so it is unaffected.
    /// When both apply to the same player, Psychic Coercion wins (a player's choice beats the dice).
    /// </summary>
    public static DeployOverride? DeployNodeOverride(GameState s, int player)
    {
        DeployOverride? random = null;
        foreach (var src in ActiveSources(s))
            foreach (var ab in src.Abilities)
            {
                if (ab.Op != Op.OverrideDecision || !CondOk(s, ab, src) || src.Controller < 0 || src.Controller == player) continue;
                if (!ab.RandomDecider) return new DeployOverride(src.Card, false, src.Controller);
                random ??= new DeployOverride(src.Card, true, -1);
            }
        return random;
    }

    // ------------------------------------------------------------------ M3.2c: granted actions

    /// <summary>A turn action a card in play grants (Abominations).</summary>
    public sealed record GrantedActionInfo(CardInstance Source, string Name, JsonElement Op, string? CostRace, CardKind? CostKind);

    /// <summary>Granted actions <paramref name="player"/> may take this turn (cost and targets not checked here).</summary>
    public static List<GrantedActionInfo> GrantedActions(GameState s, int player)
    {
        var list = new List<GrantedActionInfo>();
        foreach (var src in ActiveSources(s))
            foreach (var ab in src.Abilities)
                if (ab.Op == Op.GrantAction && CondOk(s, ab, src) && (ab.AllPlayers || src.Controller == player))
                    list.Add(new GrantedActionInfo(src.Card, ab.ActionName!, ab.Payload, ab.CostRace, ab.CostKind));
        return list;
    }

    // ------------------------------------------------------------------ M3.2c: Stagnation

    /// <summary>
    /// Stagnation: "Replace all Race words with Blaarg (including Card Races)". While it is active every card's race is
    /// that race, and every race named in card text reads as it (errata: if it's on a Card and it's written, it
    /// counts). Null when no such effect is active.
    /// </summary>
    public static Race? ReplacementRace(GameState s)
    {
        if (_inWords || !AnyOp(s, Op.ReplaceRaceWords)) return null;
        bool prevRaces = _inRaces;
        _inWords = true;
        _inRaces = true; // suppression checks below read races: use the printed / changed ones
        try
        {
            foreach (var src in ActiveSources(s))
                foreach (var ab in src.Abilities)
                    if (ab.Op == Op.ReplaceRaceWords && CondOk(s, ab, src))
                        return ab.Races[0];
            return null;
        }
        finally
        {
            _inWords = false;
            _inRaces = prevRaces;
        }
    }

    /// <summary>
    /// A race word written on <paramref name="source"/>'s text, as it currently reads (Stagnation). An Immune Unit's
    /// own text is unaffected. Remembered races ("choose a Race") are not written, so they never go through this.
    /// </summary>
    public static Race TextRace(GameState s, Race written, CardInstance? source)
    {
        if (source is not null && IsImmune(s, source)) return written;
        return ReplacementRace(s) ?? written;
    }

    // ================================================================== sources and matching

    [ThreadStatic] private static bool _inSuppress;
    [ThreadStatic] private static bool _inRaces;
    [ThreadStatic] private static bool _inWords;
    [ThreadStatic] private static bool _inImmune;

    private static bool IsUnitInPlay(CardInstance c) =>
        c.Location.Kind == LocationKind.Node && c.Location.Node is { IsScoring: true };

    /// <summary>On a Unit/Leader node, an Order node, or the Active Warzone slot.</summary>
    private static bool IsInPlay(CardInstance c) =>
        (c.Location.Kind == LocationKind.Node && c.Location.Node!.Kind != NodeKind.Standby)
        || (c.Location.Kind == LocationKind.Pile && c.Location.Pile == PileKind.ActiveWarzone);

    private static int ControllerOf(CardInstance c) => c.Location.Kind switch
    {
        LocationKind.Node => c.Location.Node!.Side,
        LocationKind.Hand or LocationKind.Stack or LocationKind.ArchonSlot => c.Location.Player,
        _ => -1,
    };

    /// <summary>Cards in play whose definition has at least one compiled Static ability. Suppression not applied.</summary>
    private static IEnumerable<Source> CandidateSources(GameState s)
    {
        foreach (var p in s.Players)
        {
            foreach (var n in p.ScoringNodes)
                foreach (var c in n.Occupants) // a spacecraft, then its crew
                    if (c.Definition.IsUnit && Compiled(c.Definition).Abilities.Length > 0)
                        yield return new Source(c, n.Side, Compiled(c.Definition).Abilities);
            foreach (var n in p.OrderNodes)
                if (n.Card is { } c && Compiled(c.Definition).Abilities.Length > 0)
                    yield return new Source(c, n.Side, Compiled(c.Definition).Abilities);
        }
        foreach (var wz in s.ActiveWarzones)
            if (Compiled(wz.Definition).Abilities.Length > 0)
                yield return new Source(wz, -1, Compiled(wz.Definition).Abilities);
    }

    private static IEnumerable<Source> ActiveSources(GameState s) =>
        CandidateSources(s).Where(src => !IsSuppressed(s, src.Card));

    /// <summary>
    /// Suppress statics that are themselves working. A suppressor X is switched off by another suppressor Y that
    /// suppresses everything on X (Atmosphere Generator switches off Refraction Network) unless X does the same to Y
    /// (two Suppressing Goops: both stay on – the result is the same either way).
    /// </summary>
    private static List<(Source Src, Ability Ab)> ActiveSuppressors(GameState s)
    {
        var all = new List<(Source Src, Ability Ab)>();
        foreach (var src in CandidateSources(s))
        {
            if (src.Card.AbilitiesSuppressed) continue;
            foreach (var ab in src.Abilities)
                if (ab.Op == Op.Suppress) all.Add((src, ab));
        }
        if (all.Count == 0) return all;

        bool Covers((Source Src, Ability Ab) y, CardInstance x) =>
            y.Ab.SuppressAll && !ReferenceEquals(y.Src.Card, x) && Matches(s, y.Ab.Targets!, x, y.Src);

        return all.Where(x => !all.Any(y => !ReferenceEquals(y.Src.Card, x.Src.Card)
                                           && Covers(y, x.Src.Card) && !Covers(x, y.Src.Card)
                                           && !IsImmune(s, x.Src.Card)))
                  .ToList();
    }

    private static bool AnyOp(GameState s, Op op) =>
        CandidateSources(s).Any(src => src.Abilities.Any(a => a.Op == op));

    private static bool AnyActive(GameState s, Op op, out Source source)
    {
        foreach (var src in ActiveSources(s))
            foreach (var ab in src.Abilities)
                if (ab.Op == op && CondOk(s, ab, src))
                {
                    source = src;
                    return true;
                }
        source = default;
        return false;
    }

    private static bool Applies(GameState s, Ability ab, Source src, CardInstance target) =>
        ab.Targets is not null && CondOk(s, ab, src) && Matches(s, ab.Targets, target, src);

    private static bool CondOk(GameState s, Ability ab, Source src)
    {
        var c = ab.Condition;
        if (c is null) return true;
        if (c.Exists is { } f && !CardsIn(s, f.Zones).Any(x => Matches(s, f, x, src))) return false;
        if (c.YourLeaderNodeEmpty && (src.Controller < 0 || s.Players[src.Controller].LeaderNode is not { IsEmpty: true })) return false;
        if (c.Crewed is not null || c.CrewClassNot is not null || c.CrewRace is not null)
        {
            var crew = Spacecraft.CrewOf(src.Card);
            if (c.Crewed is bool crewed && (crew is not null) != crewed) return false;
            if (c.CrewClassNot is { } classes && crew is not null && crew.Definition.UnitClass is { } uc && classes.Contains(uc)) return false;
            if (c.CrewRace is { } race && (crew is null || ResolveRace(s, race, src) is not { } r || !Races(s, crew).Contains(r))) return false;
        }
        return true;
    }

    private static IEnumerable<CardInstance> CardsIn(GameState s, string[]? zones)
    {
        var set = new HashSet<CardInstance>();
        foreach (var z in zones ?? new[] { "InPlay" })
        {
            switch (z)
            {
                case "InPlay":
                    foreach (var p in s.Players)
                    {
                        foreach (var n in p.ScoringNodes) foreach (var c in n.Occupants) set.Add(c);
                        foreach (var n in p.OrderNodes) if (n.Card is not null) set.Add(n.Card);
                    }
                    foreach (var w in s.ActiveWarzones) set.Add(w);
                    break;
                case "Hand":
                    foreach (var p in s.Players) foreach (var c in p.Hand) set.Add(c);
                    break;
                case "NullifiedPile": foreach (var c in s.NullifiedUnits) set.Add(c); break;
                case "RetiredPile": foreach (var c in s.RetiredUnits) set.Add(c); break;
            }
        }
        return set;
    }

    private static bool InZone(CardInstance c, string[]? zones)
    {
        foreach (var z in zones ?? new[] { "InPlay" })
        {
            switch (z)
            {
                case "Any": return true;
                case "InPlay" when IsInPlay(c): return true;
                case "Hand" when c.Location.Kind == LocationKind.Hand: return true;
                case "NullifiedPile" when c.Location is { Kind: LocationKind.Pile, Pile: PileKind.NullifiedUnits }: return true;
                case "RetiredPile" when c.Location is { Kind: LocationKind.Pile, Pile: PileKind.RetiredUnits }: return true;
            }
        }
        return false;
    }

    /// <param name="each">For "Each" keys (Boarding Party, Rise of the Tyrant): the Unit being buffed.</param>
    private static bool Matches(GameState s, Filter f, CardInstance card, Source src, CardInstance? each = null, bool ignoreZone = false)
    {
        if (f.Self) return ReferenceEquals(card, src.Card);
        if (f.Crew) return Spacecraft.CrewOf(src.Card) is { } crewOfSource && ReferenceEquals(card, crewOfSource);
        if (!ignoreZone && !InZone(card, f.Zones)) return false;
        if (f.Spacecraft is bool isShip && card.Definition.IsSpacecraft != isShip) return false;
        if (f.Crewed is bool crewed && (!Spacecraft.IsShip(card) || (Spacecraft.CrewOf(card) is not null) != crewed)) return false;
        if (f.InSpacecraft is bool aboard && Spacecraft.IsCrew(card) != aboard) return false;
        if (f.Kind is { } k && card.Definition.Kind != k) return false;
        if (f.UnitClass is { } uc && card.Definition.UnitClass != uc) return false;
        if (f.Name is { } name && card.Name != name) return false;
        if (f.ExcludeSelf && ReferenceEquals(card, src.Card)) return false;
        if (f.Controller is { } who)
        {
            if (src.Controller < 0) return false;
            int want = who == "You" ? src.Controller : GameState.Other(src.Controller);
            if (ControllerOf(card) != want) return false;
        }
        if (f.Race is { } race)
        {
            if (ResolveRace(s, race, src) is not { } r || !Races(s, card).Contains(r)) return false;
        }
        if (f.NotRace is { } notRace)
        {
            if (ResolveRace(s, notRace, src) is { } r && Races(s, card).Contains(r)) return false;
        }
        if (f.AlliedEach || f.SharesRaceEach || f.ExcludeEach)
        {
            if (each is null) return false;
            if (f.ExcludeEach && ReferenceEquals(card, each)) return false;
            if (f.AlliedEach && (ControllerOf(card) < 0 || ControllerOf(card) != ControllerOf(each))) return false;
            if (f.SharesRaceEach && !SharesRace(s, card, each)) return false;
        }
        return true;
    }

    private static Race? ResolveRace(GameState s, string text, Source src)
    {
        if (text.StartsWith("Remembered."))
            return src.Card.Memory.TryGetValue(text[11..], out var v) && v is Race r ? r : null;
        return Enum.TryParse<Race>(text, out var parsed) ? TextRace(s, parsed, src.Card) : null;
    }

    private static int Eval(GameState s, Amount a, Source src, CardInstance target)
    {
        if (a.Const is int c) return c;
        if (a.Negate is { } inner) return -Eval(s, inner, src, target);
        if (a.CountOf is { } f) return a.Times * CardsIn(s, f.Zones).Count(x => Matches(s, f, x, src, each: target));
        if (a.BaseRankOfCrew) return Spacecraft.CrewOf(src.Card) is { } crew ? RankCalculator.CurrentBaseRank(s, crew) : 0;
        if (a.MaxHandOpponents) return src.Controller < 0 ? 0 : s.Players[GameState.Other(src.Controller)].Hand.Count;
        if (a.RankOfSelf) return RankCalculator.GetRank(s, src.Card);
        return 0;
    }

    // ================================================================== compiler

    private sealed record CompiledCard(Ability[] Abilities, IReadOnlyList<string> Inert);

    private static readonly Dictionary<string, CompiledCard> _compiled = new();

    private static CompiledCard Compiled(CardDefinition def)
    {
        lock (_compiled)
        {
            if (!_compiled.TryGetValue(def.Id, out var c)) _compiled[def.Id] = c = Compile(def);
            return c;
        }
    }

    /// <summary>Does this one Static effect entry compile (i.e. is it live)? For <see cref="CardStatus"/>.</summary>
    internal static bool IsStaticEntryLive(JsonElement effect, CardDefinition def) => CompileEffect(effect, def) is not null;

    private static int StaticEffectCount(CardDefinition def) =>
        def.Effects.ValueKind != JsonValueKind.Array ? 0
        : def.Effects.EnumerateArray().Count(e => e.TryGetProperty("trigger", out var t) && t.GetString() == "Static");

    private static CompiledCard Compile(CardDefinition def)
    {
        var abilities = new List<Ability>();
        var inert = new List<string>();
        if (def.Effects.ValueKind != JsonValueKind.Array) return new(abilities.ToArray(), inert);

        foreach (var e in def.Effects.EnumerateArray())
        {
            if (!e.TryGetProperty("trigger", out var t) || t.GetString() != "Static") continue;
            var list = CompileEffect(e, def);
            if (list is null)
            {
                string ops = e.TryGetProperty("do", out var d) && d.ValueKind == JsonValueKind.Array
                    ? string.Join("+", d.EnumerateArray().Select(o => o.TryGetProperty("op", out var n) ? n.GetString() : "?"))
                    : "?";
                inert.Add($"{def.Name} ({ops})");
            }
            else abilities.AddRange(list);
        }
        return new(abilities.ToArray(), inert);
    }

    private static readonly HashSet<string> EffectKeys = new() { "trigger", "do", "condition", "note" };

    private static readonly Dictionary<string, HashSet<string>> OpKeys = new()
    {
        ["ModifyRank"] = new() { "op", "amount", "targets", "perTarget", "floor", "note" },
        ["SetBaseRank"] = new() { "op", "targets", "value", "note" },
        ["SetRaces"] = new() { "op", "races", "targets", "note" },
        ["Immunity"] = new() { "op", "targets", "scope", "note" },
        ["CannotBeNullified"] = new() { "op", "targets", "note" },
        ["Suppress"] = new() { "op", "targets", "what", "note" },
        ["ModifyManipulate"] = new() { "op", "player", "addMagnitude", "note" },
        ["InvertRankModifiers"] = new() { "op", "note" },
        ["InvertRoundWinner"] = new() { "op", "note" },
        ["RestrictPlay"] = new() { "op", "targets", "actions", "note" },
        ["RestrictDeploy"] = new() { "op", "rule", "forPlayer", "note" },
        ["LimitTurns"] = new() { "op", "kind", "max", "perPlayer", "note" },
        ["PlayFromZone"] = new() { "op", "zone", "filter", "player", "note" },
        // M3.2c
        ["RevealHands"] = new() { "op", "players", "note" },
        ["OverrideDecision"] = new() { "op", "decision", "forPlayer", "decider", "timing", "note" },
        ["GrantAction"] = new() { "op", "player", "actionName", "countsAsTurn", "cost", "do", "note" },
        ["ReplaceRaceWords"] = new() { "op", "race", "scope", "note" },
        // Archons
        ["ExtraAction"] = new() { "op", "player", "count", "note" },
        ["CountAsZero"] = new() { "op", "targets", "note" },
        ["OfferCopyOfSelf"] = new() { "op", "targets", "note" },
        // Spacecraft
        ["CannotBeCrewed"] = new() { "op", "targets", "note" },
        ["RestrictBoarding"] = new() { "op", "note" },
        ["DisableElevation"] = new() { "op", "note" },
        ["RevealTopCards"] = new() { "op", "to", "note" },
        ["ModifyAssembledRank"] = new() { "op", "player", "multiplier", "note" },
        ["RankDoesNotCount"] = new() { "op", "targets", "note" },
    };

    private static List<Ability>? CompileEffect(JsonElement e, CardDefinition def)
    {
        foreach (var p in e.EnumerateObject())
            if (!EffectKeys.Contains(p.Name)) return null;

        Condition? cond = null;
        if (e.TryGetProperty("condition", out var ce) && (cond = ParseCondition(ce)) is null) return null;
        if (!e.TryGetProperty("do", out var ops) || ops.ValueKind != JsonValueKind.Array) return null;

        var result = new List<Ability>();
        foreach (var op in ops.EnumerateArray())
        {
            var ab = CompileOp(op, def);
            if (ab is null) return null;
            ab.Condition = cond;
            result.Add(ab);
        }
        return result;
    }

    private static Ability? CompileOp(JsonElement op, CardDefinition def)
    {
        string name = Str(op, "op") ?? "";
        // Placement / AssembleRules read these; Mechatron's "cannot be Deployed unless it can be Played" is PlayRules.
        if (name is "PlacementOverride" or "GrantKeyword" or "DeployRequiresPlayable")
            return new Ability { Op = Op.HandledElsewhere, Text = name };
        if (!OpKeys.TryGetValue(name, out var keys)) return null;
        foreach (var p in op.EnumerateObject())
            if (!keys.Contains(p.Name)) return null;

        Filter? targets = null;
        if (op.TryGetProperty("targets", out var te) && (targets = ParseFilter(te)) is null) return null;

        switch (name)
        {
            case "ModifyRank":
            {
                if (targets is null || !op.TryGetProperty("amount", out var am) || ParseAmount(am) is not { } amount) return null;
                int? floor = null;
                if (op.TryGetProperty("floor", out var fl)) { if (fl.ValueKind != JsonValueKind.Number) return null; floor = fl.GetInt32(); }
                return new Ability { Op = Op.ModifyRank, Targets = targets, Amount = amount, Floor = floor, Text = name };
            }
            case "SetBaseRank":
            {
                if (targets is null || !op.TryGetProperty("value", out var v) || ParseAmount(v) is not { } amount) return null;
                return new Ability { Op = Op.SetBaseRank, Targets = targets, Amount = amount, Text = name };
            }
            case "SetRaces":
            {
                if (targets is null || !op.TryGetProperty("races", out var r) || r.ValueKind != JsonValueKind.Array) return null;
                var races = new List<Race>();
                foreach (var x in r.EnumerateArray())
                {
                    if (!Enum.TryParse<Race>(x.GetString(), out var race)) return null;
                    races.Add(race);
                }
                return new Ability { Op = Op.SetRaces, Targets = targets, Races = races.ToArray(), Text = name };
            }
            case "Immunity":
                // Vigan: "Self". Deify: a filter ("Your Leader ... is Immune"), granted by the Order.
                if (targets is null || Str(op, "scope") != "AllOtherEffects") return null;
                return new Ability { Op = Op.Immunity, Targets = targets, Text = name };
            case "ExtraAction":
            {
                if (Str(op, "player") != "You") return null;
                int count = 1;
                if (op.TryGetProperty("count", out var cn)) { if (cn.ValueKind != JsonValueKind.Number) return null; count = cn.GetInt32(); }
                return new Ability { Op = Op.ExtraAction, Max = count, Text = name };
            }
            case "CountAsZero":
                if (targets is null or { Self: true }) return null;
                return new Ability { Op = Op.CountAsZero, Targets = targets, Text = name };
            case "OfferCopyOfSelf":
                if (targets is null or { Self: true }) return null;
                return new Ability { Op = Op.OfferCopyOfSelf, Targets = targets, Text = name };
            case "CannotBeNullified":
                if (targets is null) return null;
                return new Ability { Op = Op.CannotBeNullified, Targets = targets, Text = name };
            // ---------------------------------------------------------------- Spacecraft
            case "CannotBeCrewed":
                if (targets is not { Self: true }) return null;
                return new Ability { Op = Op.CannotBeCrewed, Targets = targets, Text = name };
            case "RestrictBoarding":
                return new Ability { Op = Op.RestrictBoarding, Text = name };
            case "DisableElevation":
                return new Ability { Op = Op.DisableElevation, Text = name };
            case "RevealTopCards":
                if (Str(op, "to") != "You") return null;
                return new Ability { Op = Op.RevealTopCards, Text = name };
            case "ModifyAssembledRank":
                if (Str(op, "player") != "You" || !op.TryGetProperty("multiplier", out var mul) || mul.ValueKind != JsonValueKind.Number) return null;
                return new Ability { Op = Op.ModifyAssembledRank, Multiplier = mul.GetInt32(), Text = name };
            case "RankDoesNotCount":
                if (targets is not { Self: true }) return null;
                return new Ability { Op = Op.RankDoesNotCount, Targets = targets, Text = name };
            case "Suppress":
            {
                if (targets is null) return null;
                string? what = Str(op, "what");
                if (what is not ("AllAbilitiesAndEffects" or "TriggeredOn:Played")) return null;
                return new Ability { Op = Op.Suppress, Targets = targets, SuppressAll = what == "AllAbilitiesAndEffects", Text = name };
            }
            case "ModifyManipulate":
                if (Str(op, "player") != "You" || !op.TryGetProperty("addMagnitude", out var add) || add.ValueKind != JsonValueKind.Number) return null;
                return new Ability { Op = Op.ModifyManipulate, Add = add.GetInt32(), Text = name };
            case "InvertRankModifiers":
                return new Ability { Op = Op.InvertRankModifiers, Text = name };
            case "InvertRoundWinner":
                return new Ability { Op = Op.InvertRoundWinner, Text = name };
            case "RestrictPlay":
            {
                if (targets is null || !op.TryGetProperty("actions", out var acts) || acts.ValueKind != JsonValueKind.Array) return null;
                var list = acts.EnumerateArray().Select(a => a.GetString() ?? "").ToArray();
                if (list.Any(a => a is not ("Deploy" or "Issue"))) return null;
                return new Ability { Op = Op.RestrictPlay, Targets = targets, Actions = list, Text = name };
            }
            case "RestrictDeploy":
            {
                string? rule = Str(op, "rule");
                string? forPlayer = Str(op, "forPlayer");
                if (rule == "IfTargetOpposed_BaseRankMustEqualOpposing" && forPlayer is null)
                    return new Ability { Op = Op.RestrictDeploy, Rule = rule, Text = name };
                if (rule == "MustTargetOwnOpposedNodeIfAble" && forPlayer == "Opponent")
                    return new Ability { Op = Op.RestrictDeploy, Rule = rule, ForPlayer = forPlayer, Text = name };
                return null;
            }
            case "PlayFromZone":
            {
                PileKind? pile = Str(op, "zone") switch
                {
                    "RetiredPile" => PileKind.RetiredUnits,
                    "NullifiedPile" => PileKind.NullifiedUnits,
                    _ => null,
                };
                string? who = Str(op, "player");
                if (pile is null || who is not ("You" or "Each") || !op.TryGetProperty("filter", out var pf)
                    || ParseFilter(pf) is not { Self: false } filter) return null;
                return new Ability { Op = Op.PlayFromZone, Targets = filter, Pile = pile, EachPlayer = who == "Each", Text = name };
            }
            case "RevealHands":
                if (Str(op, "players") is not ("Opponents" or "All")) return null;
                return new Ability { Op = Op.RevealHands, AllPlayers = Str(op, "players") == "All", Text = name };
            case "OverrideDecision":
            {
                if (Str(op, "decision") != "DeployTargetNode" || Str(op, "forPlayer") != "Opponent") return null;
                string? decider = Str(op, "decider");
                string? timing = Str(op, "timing");
                if (decider == "Random" && timing is null)
                    return new Ability { Op = Op.OverrideDecision, RandomDecider = true, Text = name };
                // The chooser always picks the node before the Unit is chosen (spec §7.1).
                if (decider == "Controller" && timing is null or "BeforeCardChosen")
                    return new Ability { Op = Op.OverrideDecision, Text = name };
                return null;
            }
            case "GrantAction":
            {
                string? who = Str(op, "player");
                if (who is not ("Each" or "You") || Str(op, "actionName") is not { } actionName) return null;
                if (!op.TryGetProperty("countsAsTurn", out var cat) || cat.ValueKind != JsonValueKind.True) return null;
                if (!op.TryGetProperty("cost", out var cost) || cost.ValueKind != JsonValueKind.Object || Str(cost, "op") != "NullifyFromHand") return null;
                string? costRace = null;
                CardKind? costKind = null;
                foreach (var p in cost.EnumerateObject())
                {
                    if (p.Name is "op" or "note") continue;
                    if (p.Name != "filter" || p.Value.ValueKind != JsonValueKind.Object) return null;
                    foreach (var q in p.Value.EnumerateObject())
                    {
                        if (q.Name == "race" && Enum.TryParse<Race>(q.Value.GetString(), out _)) costRace = q.Value.GetString();
                        else if (q.Name == "kind" && Enum.TryParse<CardKind>(q.Value.GetString(), out var ck)) costKind = ck;
                        else return null;
                    }
                }
                if (!op.TryGetProperty("do", out var body) || !Effects.EffectSupport.OpsLive(body)) return null;
                return new Ability
                {
                    Op = Op.GrantAction, AllPlayers = who == "Each", ActionName = actionName, Payload = op,
                    CostRace = costRace, CostKind = costKind, Text = name,
                };
            }
            case "ReplaceRaceWords":
                if (Str(op, "scope") != "AllCards" || !Enum.TryParse<Race>(Str(op, "race"), out var replacement)) return null;
                return new Ability { Op = Op.ReplaceRaceWords, Races = new[] { replacement }, Text = name };
            case "LimitTurns":
                if (Str(op, "kind") != "DeployUnit" || !op.TryGetProperty("max", out var mx) || mx.ValueKind != JsonValueKind.Number) return null;
                if (op.TryGetProperty("perPlayer", out var pp) && pp.ValueKind != JsonValueKind.True) return null;
                return new Ability { Op = Op.LimitTurns, Max = mx.GetInt32(), Text = name };
        }
        return null;
    }

    private static readonly HashSet<string> FilterKeys = new()
    {
        "zone", "kind", "race", "notRace", "unitClass", "name", "controller", "owner", "excludeSelf",
        "allied", "sharesRaceWith", "excludeEach",
        "spacecraft", "crewed", "inSpacecraft", // Spacecraft
    };

    private static readonly HashSet<string> ZoneNames = new() { "InPlay", "Hand", "Any", "NullifiedPile", "RetiredPile" };

    private static Filter? ParseFilter(JsonElement f)
    {
        if (f.ValueKind == JsonValueKind.String)
            return f.GetString() switch
            {
                "Self" => new Filter { Self = true },
                "Crew" => new Filter { Crew = true }, // Spacecraft: the Unit aboard the source (Goo Bubble)
                _ => null,
            };
        if (f.ValueKind != JsonValueKind.Object) return null;
        var r = new Filter();
        foreach (var p in f.EnumerateObject())
        {
            if (!FilterKeys.Contains(p.Name)) return null;
            var v = p.Value;
            switch (p.Name)
            {
                case "zone":
                    var zones = v.ValueKind == JsonValueKind.Array ? v.EnumerateArray().Select(x => x.GetString() ?? "").ToArray()
                        : v.ValueKind == JsonValueKind.String ? new[] { v.GetString()! } : null;
                    if (zones is null || zones.Any(z => !ZoneNames.Contains(z))) return null;
                    r.Zones = zones;
                    break;
                case "kind":
                    if (!Enum.TryParse<CardKind>(v.GetString(), out var k)) return null;
                    r.Kind = k;
                    break;
                case "race":
                case "notRace":
                    string? race = v.GetString();
                    if (race is null || (!race.StartsWith("Remembered.") && !Enum.TryParse<Race>(race, out _))) return null;
                    if (p.Name == "race") r.Race = race; else r.NotRace = race;
                    break;
                case "unitClass":
                    if (!Enum.TryParse<UnitClass>(v.GetString(), out var uc)) return null;
                    r.UnitClass = uc;
                    break;
                case "name":
                    r.Name = v.GetString();
                    break;
                case "controller":
                case "owner":
                    if (v.GetString() is not ("You" or "Opponent")) return null;
                    r.Controller = v.GetString();
                    break;
                case "excludeSelf":
                    r.ExcludeSelf = v.ValueKind == JsonValueKind.True;
                    break;
                case "allied":
                    if (v.GetString() != "Each") return null;
                    r.AlliedEach = true;
                    break;
                case "sharesRaceWith":
                    if (v.GetString() != "Each") return null;
                    r.SharesRaceEach = true;
                    break;
                case "excludeEach":
                    r.ExcludeEach = v.ValueKind == JsonValueKind.True;
                    break;
                case "spacecraft":
                case "crewed":
                case "inSpacecraft":
                    if (v.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return null;
                    bool b = v.ValueKind == JsonValueKind.True;
                    if (p.Name == "spacecraft") r.Spacecraft = b;
                    else if (p.Name == "crewed") r.Crewed = b;
                    else r.InSpacecraft = b;
                    break;
            }
        }
        return r;
    }

    private static Amount? ParseAmount(JsonElement a)
    {
        if (a.ValueKind == JsonValueKind.Number) return new Amount { Const = a.GetInt32() };
        if (a.ValueKind != JsonValueKind.Object) return null;
        // Earthrise: { "countOf": {...}, "times": 2 }.
        if (a.TryGetProperty("times", out var times))
        {
            if (times.ValueKind != JsonValueKind.Number || a.EnumerateObject().Count() != 2
                || !a.TryGetProperty("countOf", out var co) || ParseFilter(co) is not { Self: false } cf) return null;
            return new Amount { CountOf = cf, Times = times.GetInt32() };
        }
        var props = a.EnumerateObject().ToList();
        if (props.Count != 1) return null;
        var p = props[0];
        switch (p.Name)
        {
            case "countOf":
                return ParseFilter(p.Value) is { Self: false } f ? new Amount { CountOf = f } : null;
            case "negate":
                return ParseAmount(p.Value) is { } inner ? new Amount { Negate = inner } : null;
            case "maxHandSize":
                return p.Value.GetString() == "Opponents" ? new Amount { MaxHandOpponents = true } : null;
            case "rankOf":
                return p.Value.GetString() == "Self" ? new Amount { RankOfSelf = true } : null;
            case "baseRankOf":
                return p.Value.GetString() == "Crew" ? new Amount { BaseRankOfCrew = true } : null; // High Roller
        }
        return null;
    }

    private static Condition? ParseCondition(JsonElement c)
    {
        if (c.ValueKind != JsonValueKind.Object) return null;
        var r = new Condition();
        if (c.TryGetProperty("exists", out var ex))
        {
            if (c.EnumerateObject().Count() != 1 || ParseFilter(ex) is not { Self: false } f) return null;
            r.Exists = f;
            return r;
        }
        // Spacecraft: conditions on the source spacecraft's crew (one key each).
        if (c.EnumerateObject().Count() == 1)
        {
            if (c.TryGetProperty("crewed", out var cw))
            {
                if (cw.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return null;
                r.Crewed = cw.ValueKind == JsonValueKind.True;
                return r;
            }
            if (c.TryGetProperty("crewClassNot", out var cc))
            {
                if (cc.ValueKind != JsonValueKind.Array) return null;
                var list = new List<UnitClass>();
                foreach (var x in cc.EnumerateArray())
                {
                    if (!Enum.TryParse<UnitClass>(x.GetString(), out var uc)) return null;
                    list.Add(uc);
                }
                r.CrewClassNot = list.ToArray();
                return r;
            }
            if (c.TryGetProperty("crewRace", out var cr))
            {
                if (!Enum.TryParse<Race>(cr.GetString(), out _)) return null;
                r.CrewRace = cr.GetString();
                return r;
            }
        }
        if (c.TryGetProperty("node", out var node))
        {
            if (node.GetString() != "YourLeaderNode" || !c.TryGetProperty("empty", out var em) || em.ValueKind != JsonValueKind.True
                || c.EnumerateObject().Count() != 2) return null;
            r.YourLeaderNodeEmpty = true;
            return r;
        }
        return null;
    }

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
