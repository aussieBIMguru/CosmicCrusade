"""Builds cards.json for Cosmic Crusade from hand-verified card data.
Rules text is transcribed from the card images (OCR + visual check); flavour comes from split.json."""
import json, re, sys

SPLIT = json.load(open(sys.argv[1]))
OUT = sys.argv[2]

RACE = {"0": "GalacticCouncil", "1": "SpaceCowboy", "2": "Necrolite", "3": "Overmind",
        "4": "Blaarg", "5": "Synthenoid", "6": "Neutral"}
WEAK_TARGET = {"SpaceCowboy": "Necrolite", "Necrolite": "Overmind", "Overmind": "Blaarg",
               "Blaarg": "Synthenoid", "Synthenoid": "SpaceCowboy"}

# ---------- DSL helpers ----------
def F(**kw):            # selector / filter
    return kw
def op(name, **kw):
    d = {"op": name}; d.update(kw); return d
def eff(trigger, do, **kw):
    d = {"trigger": trigger}; d.update(kw)
    d["do"] = do if isinstance(do, list) else [do]
    return d
def script(trigger, name, **kw):
    d = {"trigger": trigger, "script": name}; d.update(kw); return d

UNIT_INPLAY = dict(zone="InPlay", kind="Unit")
def units(**kw):
    d = dict(UNIT_INPLAY); d.update(kw); return d

def race_buff(r, n=1):
    return [eff("Static", op("ModifyRank", amount=n, targets=units(race=r)))]
def champion(r):
    return [eff("Static", op("ModifyRank", amount=1, targets=units(race=r, controller="You", excludeSelf=True)))]
def hate_minion(r):
    return [eff("OnPlay", op("Nullify", targets="OpposingUnit"), optional=True,
                condition={"unit": "OpposingUnit", "race": r})]
def hate_underling(r):
    return [eff("OnPlay", [op("Nullify", targets="OpposingUnit"),
                           op("ModifyRank", amount=2, targets="Self", duration="WhileInPlay")],
                optional=True, condition={"unit": "OpposingUnit", "race": r})]
def stay_standby():
    return [eff("CleanUp", op("MoveToStandby", targets="Self", replaces="Retire"), optional=True)]
def hate_static(r):
    return [eff("Static", op("ModifyRank", amount=2, targets="Self"),
                condition={"exists": units(controller="Opponent", race=r)})]
def revenge():
    return [eff("OnSelfNullified", op("Nullify", targets="OpposingUnit"))]
def locker():
    return [eff("OnPlay", op("Lock", targets=F(choose="Node", min=0, max=1, filter=F(empty=True)), remember="lockedNode")),
            eff("OnLeavePlay", op("Unlock", targets="Remembered.lockedNode"))]

# ---------- Card table ----------
# id: (name, kind, sub, rank, rules, effects)   kind U = unit (sub = class), O = order (sub = type), W = warzone
C = {}
def card(cid, name, kind, sub, rank, rules, effects, **extra):
    C[cid] = dict(name=name, kind=kind, sub=sub, rank=rank, rules=rules, effects=effects, **extra)

# ===== GALACTIC COUNCIL =====
card("0L01", "Vigan the Untouchable", "U", "Leader", 4, "This Unit is Immune to all other Effects and Abilities.",
     [eff("Static", op("Immunity", targets="Self", scope="AllOtherEffects"))])
card("0L02", "Ace, Council Tactician", "U", "Leader", 1, "When you play this Unit, you may rearrange all Nodes (the new Nodes must be legal).",
     [script("OnPlay", "RearrangeAllNodes", optional=True, note="Includes Standby<->Unit Nodes; Leaders can't move; no crossing sides (errata).")])
card("0L03", "The Empress", "U", "Leader", 0, "At the End of the Main Phase, if your Opponents control no Leader Units, you win the round.",
     [eff("EndOfMain", op("WinRound", player="You"), condition={"notExists": units(controller="Opponent", unitClass="Leader")})])
card("0L04", "The Left Hand", "U", "Leader", 2, "When you play this Unit, you may Nullify the Opposing Leader and up to 1 other Unit that shares a Race with it.",
     [eff("OnPlay", [op("Nullify", targets="OpposingLeader", bind="leader"),
                     op("Nullify", targets=F(choose="Unit", min=0, max=1, filter=units(sharesRaceWith="Bound.leader", excludeBound=True)))],
          optional=True)])
card("0L05", "The Right Hand", "U", "Leader", 2, "When you play this Unit, you may Nullify any number of Ongoing Orders.",
     [eff("OnPlay", op("Nullify", targets=F(choose="Card", min=0, max="Any", filter=F(zone="OrderNode", orderType="Ongoing"))))])
card("0L06", "Lance Corporal", "U", "Leader", 1, "Whenever you play an Order, this Unit gets +2 Rank.",
     [eff("OnEvent", op("ModifyRank", amount=2, targets="Self", duration="WhileInPlay"), event="OrderPlayed", filter=F(controller="You"))])
card("0L07", "Preferred Candidate", "U", "Leader", 2, "During the Cleanup Phase, this Unit may remain in play. If it does, it gets +2 Rank.",
     [eff("CleanUp", [op("RemainInPlay", targets="Self", replaces="Retire"),
                      op("ModifyRank", amount=2, targets="Self", duration="WhileInPlay")], optional=True)])
card("0O01", "Consortium", "O", "Ongoing", None, "Cards have no Race (both in Play and in Hand).",
     [eff("Static", op("SetRaces", races=[], targets=F(kind="Unit", zone=["InPlay", "Hand"])), note="Errata: does not apply to Orders.")])
card("0O02", "Boarding Party", "O", "Ongoing", None, "Each Unit gets +1 Rank for each other Allied Unit that shares a Race with it.",
     [eff("Static", op("ModifyRank", amount={"countOf": units(allied="Each", sharesRaceWith="Each", excludeEach=True)}, targets=units(), perTarget=True))])
card("0O03", "Even Odds", "O", "Ongoing", None, "Opposed Units may only be Deployed if their Base Rank is equal to the Opposing Unit's Base Rank.",
     [eff("Static", op("RestrictDeploy", rule="IfTargetOpposed_BaseRankMustEqualOpposing"), note="Errata: Units may still be played to unopposed Nodes.")])
card("0O04", "Diplomatic Immunity", "O", "Ongoing", None, "'Visiting Diplomat' Units get +5 Rank and cannot be Nullified.",
     [eff("Static", [op("ModifyRank", amount=5, targets=units(name="Visiting Diplomat")),
                     op("CannotBeNullified", targets=units(name="Visiting Diplomat"))])])
card("0O05", "Extreme Sanctions", "O", "Ongoing", None, "As you Issue this Order, choose a Race. Cards of the chosen Race cannot be Deployed or Issued.",
     [eff("OnIssue", op("ChooseRace", remember="race")),
      eff("Static", op("RestrictPlay", targets=F(race="Remembered.race"), actions=["Deploy", "Issue"]))])
card("0O06", "Shadow Strike", "O", "React", None, "Nullify a Unit as it is being deployed (it is not played).",
     [eff("React", op("Nullify", targets="Event.Unit", cancelsPlay=True), when=F(event="UnitDeploying"))])
card("0O07", "Recolonize", "O", "React", None, "Choose an Empty Node as a Unit is being Deployed. That Unit is Deployed to that Node instead (it must be a legal option).",
     [eff("React", op("RedirectDeploy", node=F(choose="Node", filter=F(empty=True, legalFor="Event.Unit"))), when=F(event="UnitDeploying"))])
card("0O08", "Depose", "O", "Action", None, "Nullify a Leader.",
     [eff("OnResolve", op("Nullify", targets=F(choose="Unit", filter=units(unitClass="Leader"))))])
card("0O09", "Terraform", "O", "Action", None, "Lock a Node, and its opposing Node. The Unit's controllers may put those Units into their Standby Zone.",
     [script("OnResolve", "Terraform", note="Ruling: lock the chosen Node and its opposing Node. Each Unit's controller may move it to their Standby; if they don't or can't, it is Nullified (before the Lock).")])
card("0O10", "Respond in Kind", "O", "React", None, "Issue this Order as a Unit is Nullified to Nullify the opposing Unit as well.",
     [eff("React", op("Nullify", targets="Event.Unit.OpposingUnit", simultaneous=True), when=F(event="UnitNullifying"))])
card("0U01", "Visiting Diplomat", "U", "Bystander", 0, "You may play this Unit on any Node (even your Opponents').",
     [eff("Static", op("PlacementOverride", targets="Self", allowedNodes="AnyUnitNodeEitherSide"))])
card("0U02", "Tyrant Toppler", "U", "Underling", 1, "When you play this Unit, you may Nullify an Opposing Leader.",
     [eff("OnPlay", op("Nullify", targets="OpposingLeader"), optional=True)])
card("0U03", "Covert Operative", "U", "Minion", 2, "When you play this Unit, you may Nullify the Opposing Unit.",
     [eff("OnPlay", op("Nullify", targets="OpposingUnit"), optional=True)])
card("0U04", "Shot in the Dark", "U", "Underling", 1, "When you play this Unit, you may set another Units' Base Rank to 0.",
     [eff("OnPlay", op("SetBaseRank", value=0, targets=F(choose="Unit", filter=units(excludeSelf=True)), persistent=True), optional=True)])
card("0U05", "Corporate Detainer", "U", "Minion", 2, "When you play this Unit, you may set another Unit's Base Rank to 1.",
     [eff("OnPlay", op("SetBaseRank", value=1, targets=F(choose="Unit", filter=units(excludeSelf=True)), persistent=True), optional=True)])

# ===== SPACE COWBOY =====
SC = "SpaceCowboy"
card("1L01", "Quickraw Murphy", "U", "Leader", 3, "When you play this Unit, if the Opposing Leader isn't a Space Cowboy, Nullify it.",
     [eff("OnPlay", op("Nullify", targets="OpposingLeader"), condition={"unit": "OpposingLeader", "notRace": SC})])
card("1L02", "The Hangman", "U", "Leader", 3, "When you play this Unit, if you control another Space Cowboy, Nullify up to 1 Opposing Unit.",
     [eff("OnPlay", op("Nullify", targets=F(choose="Unit", min=0, max=1, filter=units(controller="Opponent"))),
          condition={"exists": units(controller="You", race=SC, excludeSelf=True)})])
card("1L03", "Y'nosh, Galactic Slaver", "U", "Leader", 2, "During Cleanup, you may place up to 1 non-Space Cowboy Unit in your Standby Zone (it may belong to an Opponent).",
     [eff("CleanUp", op("MoveToStandby", targets=F(choose="Unit", min=0, max=1, filter=units(notRace=SC)), standbyOf="You", gainControl=True),
          priority="First", note="Errata: takes priority over any other Unit Clean-up Ability.")])
card("1L04", "Dusty Trailblazer", "U", "Champion", 2, "All other Space Cowboy Units you control get +1 Rank.", champion(SC))
card("1L05", "Fast Eddie", "U", "Leader", 2, "When you play this Unit or another Space Cowboy Unit, you may have it Duel its Opposing Node.",
     [eff("OnEvent", op("Duel", a="Event.Unit", b="Event.Unit.OpposingUnit"), optional=True, event="UnitPlayed",
          filter=units(controller="You", race=SC), includeSelf=True)])
card("1L06", "Barkeep Bill", "U", "Leader", 3, "You may play Space Cowboy Units from the Retired Pile as if they were in your Hand.",
     [eff("Static", op("PlayFromZone", zone="RetiredPile", filter=F(kind="Unit", race=SC), player="You"))])
card("1O01", "High Noon", "O", "Ongoing", None, "At the end of the Main Phase, all Opposed Nodes Duel.",
     [eff("EndOfMain", op("DuelAllOpposed", simultaneous=True), ordering="AfterRankModifiers")])
card("1O02", "Force Field", "O", "Ongoing", None, "Space Cowboys you own cannot be Nullified.",
     [eff("Static", op("CannotBeNullified", targets=units(owner="You", race=SC)))])
card("1O03", "Xeno Laser", "O", "Ongoing", None, "Nullify the first non-Space Cowboy Unit that is Deployed.",
     [eff("OnEvent", op("Nullify", targets="Event.Unit"), event="UnitPlayed", filter=F(notRace=SC),
          oncePerRound=True, note="Ruling: hits after the Unit is Played (on-play abilities still fire). Errata: stays in play but can't trigger again this round.")])
card("1O04", "Shoot First, Think Second", "O", "Ongoing", None, "Your Opponent selects Nodes as they Deploy Units at random.",
     [eff("Static", op("OverrideDecision", decision="DeployTargetNode", forPlayer="Opponent", decider="Random"))])
card("1O05", "Blazing Saddles", "O", "Ongoing", None, "Space Cowboy Units get +1 Rank.", race_buff(SC))
card("1O06", "Blasters at Dawn", "O", "Action", None, "Choose an Opposed Node. The Units in those Nodes duel.",
     [eff("OnResolve", op("Duel", node=F(choose="Node", filter=F(nodeKind=["Unit", "Leader"], opposed=True))))])
card("1O07", "Sheriff's Badge", "O", "Action", None, "Choose a Unit. If that Unit is a Space Cowboy, it gets +2 Rank, otherwise it gets +1 Rank.",
     [eff("OnResolve", op("ModifyRank", targets=F(choose="Unit", filter=units()), amount={"ifRace": SC, "then": 2, "else": 1}, duration="WhileInPlay"))])
card("1O08", "Target Practice", "O", "React", None, "Nullify an Opposed Unit as it is being deployed (it is not played).",
     [eff("React", op("Nullify", targets="Event.Unit", cancelsPlay=True), when=F(event="UnitDeploying", targetNodeOpposed=True))])
card("1O09", "Decode", "O", "Action", None, "Unlock any number of Locked Nodes. Draw a card for each Node unlocked this way.",
     [eff("OnResolve", [op("Unlock", targets=F(choose="Node", min=0, max="Any", filter=F(locked=True)), countAs="n"),
                        op("Draw", count="Count.n", deckChoice="PerCard")])])
card("1O10", "New Recruits", "O", "Action", None, "Retire your Hand, then Draw that many Cards.",
     [eff("OnResolve", [op("RetireHand", countAs="hand"), op("Draw", count="Count.hand", distribution="SameAsRetired")])])
card("1U01", "Chrome Domer", "U", "Underling", 1, "When you play this Unit, if the Opposing Unit is a Necrolite, you may Nullify it and this Unit gets +2 Rank.", hate_underling("Necrolite"))
card("1U02", "Rogue Flakketeer", "U", "Minion", 2, "When you play this Unit, you may Nullify the Opposing Unit if it is a Necrolite.", hate_minion("Necrolite"))
card("1U03", "Steady Shooter", "U", "Minion", 2, "When you play this Unit, it Duels the Opposing Unit.",
     [eff("OnPlay", op("Duel", a="Self", b="OpposingUnit"), condition={"selfOpposed": True})])
card("1U04", "Crimson Redeemer", "U", "Minion", 2, "When you play this Unit, it Duels the Opposing Unit. This Unit cannot be Nullified in that Duel.",
     [eff("OnPlay", op("Duel", a="Self", b="OpposingUnit", protect=["a"]), condition={"selfOpposed": True})])
card("1U05", "Armament Master", "U", "Brute", 4, "", [])
card("1U06", "Lone Wolf", "U", "Minion", 2, "During Cleanup, you may place this Unit into your Standby Zone instead of Retiring it.", stay_standby())
card("1U07", "Rebel with a Cause", "U", "Minion", 2, "If an Opponent controls any Necrolite Units, this Unit gets +2 Rank.", hate_static("Necrolite"))
card("1U08", "Twisted Firestarter", "U", "Minion", 2, "If this Unit is Nullified, Nullify the Opposing Unit as well.", revenge())
card("1U09", "Roundup Gang", "U", "Underling", 1, "When you play this Unit, Lock up to 1 Empty Node. When this Unit leaves play, Unlock that Node.", locker())

# ===== NECROLITE =====
NE = "Necrolite"
card("2L01", "Cure Failure 349", "U", "Leader", "X", "This Units Base Rank is equal to the number of Nullified Necrolite Units.",
     [eff("Static", op("SetBaseRank", targets="Self", value={"countOf": F(zone="NullifiedPile", kind="Unit", race=NE)}))])
card("2L02", "Disturberus", "U", "Leader", 1, "Whenever a Unit is Nullified, this Unit gets +3 Rank.",
     [eff("OnEvent", op("ModifyRank", amount=3, targets="Self", duration="WhileInPlay"), event="UnitNullified",
          note="Errata: includes Units Nullified in the Standby Zone.")])
card("2L03", "Sisters of Blight", "U", "Leader", 3, "When you play this Unit, Lock up to 1 Node. If there is a Unit on that Node, its Controller places it in their Standby zone.",
     [eff("OnPlay", [op("MoveToStandby", targets="Chosen.node.Unit", standbyOf="Controller", ifNoRoom="Retire"),
                     op("Lock", targets="Chosen.node")],
          choose=F(name="node", choose="Node", min=0, max=1), note="Errata: Unit moves before the Node is Locked.")])
card("2L04", "Festering Matron", "U", "Champion", 2, "All other Necrolite Units you control get +1 Rank.", champion(NE))
card("2L05", "Galactomancer", "U", "Leader", 3, "You may play Necrolite Units from the Nullified Pile as though they were in your Hand.",
     [eff("Static", op("PlayFromZone", zone="NullifiedPile", filter=F(kind="Unit", race=NE), player="You"))])
card("2L06", "The Creeper", "U", "Leader", -1, "You may play this Unit on your Opponent's Leader Node. Other Units you control get -1 Rank.",
     [eff("Static", op("PlacementOverride", targets="Self", allowedNodes=["OwnLeader", "OpponentLeader"])),
      eff("Static", op("ModifyRank", amount=-1, targets=units(controller="You", excludeSelf=True)))])
card("2O01", "We Come in Pieces", "O", "Ongoing", None, "Necrolite Units get +1 Rank.", race_buff(NE))
card("2O02", "Acid Rain", "O", "Ongoing", None, "All Units get -2 Rank.",
     [eff("Static", op("ModifyRank", amount=-2, targets=units(), floor=0), note="Errata: the Rank of a Unit cannot become negative.")])
card("2O03", "Rise of the Tyrant", "O", "Ongoing", None, "Leader Units get +X Rank, where X is the number of Allied Units that share a Race with it.",
     [eff("Static", op("ModifyRank", targets=units(unitClass="Leader"), perTarget=True,
                       amount={"countOf": units(allied="Each", sharesRaceWith="Each", excludeEach=True)}),
          note="Errata: Standby Units are not counted.")])
card("2O04", "Weed out the Weak", "O", "Ongoing", None, "At the end of the Main Phase, Nullify the Unit with the lowest Rank (if there are tied Units, Nullify them all).",
     [eff("EndOfMain", op("Nullify", targets=F(lowestRank=units(), ties="All"), simultaneous=True), ordering="BeforeRankTotal")])
card("2O05", "Abominations", "O", "Ongoing", None, "Players may Nullify a Necrolite Card from their hand to have a non-Necrolite Unit get +2 Rank. That Unit becomes a Necrolite.",
     [eff("Static", op("GrantAction", player="Each", actionName="Abomination", countsAsTurn=True,
                       cost=op("NullifyFromHand", filter=F(race=NE)),
                       do=[op("ModifyRank", amount=2, targets=F(choose="Unit", filter=units(notRace=NE)), duration="WhileInPlay", bind="u"),
                           op("SetRaces", races=[NE], targets="Bound.u")]))])
card("2O06", "Shallow Grave", "O", "Action", None, "Place a Nullified Unit into your Standby Zone.",
     [eff("OnResolve", op("MoveToStandby", targets=F(choose="Card", filter=F(zone="NullifiedPile", kind="Unit")), standbyOf="You", gainControl=True))])
card("2O07", "Overwhelm", "O", "Action", None, "Nullify an Opposed Unit with Base Rank less than the number of Necrolite Cards in play.",
     [eff("OnResolve", op("Nullify", targets=F(choose="Unit", filter=units(opposed=True, baseRankLessThan={"countOf": F(zone="InPlay", race=NE)}))),
          note="Errata: Standby Units are not counted.")])
card("2O08", "Already Dead", "O", "React", None, "Issue this Order if a Unit would be Nullified to prevent that Unit being Nullified. That Unit becomes a Necrolite.",
     [eff("React", [op("PreventEvent"), op("SetRaces", races=[NE], targets="Event.Unit")], when=F(event="UnitNullifying"))])
card("2O09", "Spreading Sickness", "O", "React", None, "Issue this order as a Unit is Deployed to Lock its Node. Its controller places it in their Standby zone.",
     [eff("React", [op("MoveToStandby", targets="Event.Unit", standbyOf="Controller", cancelsPlay=True, ifNoRoom="Retire"),
                    op("Lock", targets="Event.TargetNode")], when=F(event="UnitDeploying"),
          note="Errata (Sisters of Blight/Spreading Blight): Unit moves before the Node is Locked; Retire it if no Standby room.")])
card("2O10", "Intergalactic Plague", "O", "Action", None, "Nullify all Retired Units and all Units in Standby Zones.",
     [eff("OnResolve", [op("Nullify", targets=F(zone="RetiredPile", kind="Unit")), op("Nullify", targets=F(zone="Standby", kind="Unit"))])])
card("2U01", "Mind Maggots", "U", "Underling", 1, "When you play this Unit, if the Opposing Unit is an Overmind you may Nullify it and this Unit gets +2 Rank.", hate_underling("Overmind"))
card("2U02", "Brainless Mob", "U", "Minion", 2, "When you play this Unit, you may Nullify the Opposing Unit if it is an Overmind.", hate_minion("Overmind"))
card("2U03", "Grim Angler", "U", "Underling", 1, "When you play this Unit, you may Return up to 2 Nullified Units to your Standby Zone.",
     [eff("OnPlay", op("MoveToStandby", targets=F(choose="Card", min=0, max=2, filter=F(zone="NullifiedPile", kind="Unit")), standbyOf="You", gainControl=True))])
card("2U04", "Reaper of Rot", "U", "Minion", 2, "When you play this Unit, you may Return up to 1 Nullified Unit to your Standby Zone.",
     [eff("OnPlay", op("MoveToStandby", targets=F(choose="Card", min=0, max=1, filter=F(zone="NullifiedPile", kind="Unit")), standbyOf="You", gainControl=True))])
card("2U05", "Insatiable Mawhead", "U", "Brute", 4, "", [])
card("2U06", "Mindless Mass", "U", "Minion", 2, "During Cleanup, you may place this Unit into your Standby Zone instead of Retiring it.", stay_standby())
card("2U07", "Cruel Vicisector", "U", "Minion", 2, "If an Opponent controls any Overmind Units, this Unit gets +2 Rank.", hate_static("Overmind"))
card("2U08", "Lurking Parasite", "U", "Minion", 2, "If this Unit is Nullified, Nullify the Opposing Unit as well.", revenge())
card("2U09", "Terrortorialist", "U", "Underling", 1, "When you play this Unit, Lock up to 1 Empty Node. When this Unit leaves play, Unlock that Node.", locker())

# ===== OVERMIND =====
OV = "Overmind"
card("3L01", "Indominus", "U", "Leader", 6, "This Unit gets -X Rank, where X is the greatest number of Cards in your Opponents' hands.",
     [eff("Static", op("ModifyRank", targets="Self", amount={"negate": {"maxHandSize": "Opponents"}}))])
card("3L02", "The All-knowing", "U", "Leader", 3, "Your Opponents play with their Hands revealed.",
     [eff("Static", op("RevealHands", players="Opponents"))])
card("3L03", "Psyphoon", "U", "Leader", 1, "When you play this Unit, place all other Overmind Units you control into your Standby Zone, then Draw that many Cards.",
     [eff("OnPlay", [op("MoveToStandby", targets=units(controller="You", race=OV, excludeSelf=True), standbyOf="You", ifNoRoom="Retire", countAs="n"),
                     op("Draw", count="Count.n", deckChoice="PerCard")],
          note="Errata: excess Units without Standby room are Retired, but still count for the Draw.")])
card("3L04", "Think Tank", "U", "Champion", 2, "All other Overmind Units you control get +1 Rank.", champion(OV))
card("3L05", "Arganium", "U", "Leader", 3, "When you play this Unit, you may switch the contents of any 2 Unit Nodes.",
     [eff("OnPlay", op("SwapNodeContents", nodes=F(choose="Node", min=2, max=2, filter=F(nodeKind="Unit", side="Any")), controlFollowsSide=True), optional=True)])
card("3L06", "Cortexella", "U", "Leader", 2, "Each time you draw a Card, this Unit gets +1 Rank (for each Card drawn).",
     [eff("OnEvent", op("ModifyRank", amount=1, targets="Self", duration="WhileInPlay"), event="CardDrawn", filter=F(player="You"))])
card("3O01", "Transparency", "O", "Ongoing", None, "Your Opponents play with their Hands revealed.",
     [eff("Static", op("RevealHands", players="Opponents"))])
card("3O02", "Temporal Stasis", "O", "Ongoing", None, "No other Orders may be Issued.",
     [eff("Static", op("RestrictPlay", targets=F(kind="Order"), actions=["Issue"]), note="Errata: does not affect Ongoing Orders already in play.")])
card("3O03", "Mind Games", "O", "Ongoing", None, "Your Opponents must play Units on Opposed Nodes if able.",
     [eff("Static", op("RestrictDeploy", forPlayer="Opponent", rule="MustTargetOwnOpposedNodeIfAble"),
          note="Errata: if they cannot, they may play Units on their own Nodes.")])
card("3O04", "Hive Mind", "O", "Ongoing", None, "Overmind Units get +1 Rank.", race_buff(OV))
card("3O05", "Psychic Coercion", "O", "Ongoing", None, "You choose which Nodes your opponents Deploy their Units to (before you know what Unit they are Deploying).",
     [eff("Static", op("OverrideDecision", decision="DeployTargetNode", forPlayer="Opponent", decider="Controller", timing="BeforeCardChosen"))])
card("3O06", "Second Thought", "O", "React", None, "Nullify a React or Action as it is being Issued (it is not played).",
     [eff("React", op("Nullify", targets="Event.Order", cancelsPlay=True), when=F(event="OrderIssuing", orderType=["React", "Action"]))])
card("3O07", "Commandeer", "O", "React", None, "Gain control of a React or Action as it is being Issued. You may choose new Targets for it.",
     [eff("React", [op("GainControl", targets="Event.Order"), op("Retarget", targets="Event.Order", optional=True)],
          when=F(event="OrderIssuing", orderType=["React", "Action"]))])
card("3O08", "Meditate", "O", "Action", None, "Draw up to 1 Unit Card, and up to 1 Order Card.",
     [eff("OnResolve", [op("Draw", deck="Units", min=0, max=1), op("Draw", deck="Orders", min=0, max=1)])])
card("3O09", "One of Us", "O", "Action", None, "Gain control of a Unit if you have at least 1 Empty Node. You must choose a new Node for it (it must be a legal choice).",
     [eff("OnResolve", op("GainControl", targets=F(choose="Unit", filter=units(controller="Opponent")), moveTo=F(choose="Node", filter=F(empty=True, side="You", legalFor="Target")), entersPlay=False),
          condition={"exists": F(nodeOf="You", empty=True)}, note="Errata: the Unit does not come into play again, but remains in play.")])
card("3O10", "Reminisce", "O", "Action", None, "Return an Issued Order to your Hand.",
     [eff("OnResolve", op("ReturnToHand", targets=F(choose="Card", filter=F(zone="IssuedPile")), hand="You"))])
card("3U01", "Echo Chamber", "U", "Underling", 1, "When you play this Unit, if the Opposing Unit is a Blaarg you may Nullify it and this Unit gets +2 Rank.", hate_underling("Blaarg"))
card("3U02", "Willbreaker", "U", "Minion", 2, "When you play this Unit, you may Nullify the Opposing Unit if it is a Blaarg.", hate_minion("Blaarg"))
card("3U03", "Neuroling", "U", "Underling", 1, "When you play this Unit, Draw up to 2 Unit Cards.",
     [eff("OnPlay", op("Draw", deck="Units", min=0, max=2))])
card("3U04", "Neurotron", "U", "Underling", 1, "When you play this Unit, Draw up to 2 Order Cards.",
     [eff("OnPlay", op("Draw", deck="Orders", min=0, max=2))])
card("3U05", "Grand Mastermind", "U", "Brute", 4, "", [])
card("3U06", "Grey Matter Master", "U", "Minion", 2, "During Cleanup, you may place this Unit into your Standby Zone instead of Retiring it.", stay_standby())
card("3U07", "Vortex Cortex", "U", "Minion", 2, "If an Opponent controls any Blaarg Units, this Unit gets +2 Rank.", hate_static("Blaarg"))
card("3U08", "Mind Blower", "U", "Minion", 2, "If this Unit is Nullified, Nullify the Opposing Unit as well.", revenge())
card("3U09", "Neural Constrictor", "U", "Underling", 1, "When you play this Unit, Lock up to 1 Empty Node. When this Unit leaves play, Unlock that Node.", locker())

# ===== BLAARG =====
BL = "Blaarg"
card("4L01", "Mr Morph", "U", "Leader", 0, "This Unit may enter as a copy of another Unit, except it is a Blaarg.",
     [script("AsEnters", "EnterAsCopy", optional=True, params={"setRaces": [BL], "copySource": units()}, note="Errata: Deployed as written, Played as the chosen Unit. Ruling: copy a Unit in play.")])
card("4L02", "Glorpaglorp", "U", "Leader", 3, "If another Unit you control would be Nullified whilst this Unit is in play, you may place it in your Standby Zone instead.",
     [eff("Replacement", op("MoveToStandby", targets="Event.Unit", standbyOf="You", ifNoRoom="Nullify"), optional=True,
          replaces=F(event="UnitNullifying", filter=units(controller="You", excludeSelf=True)), note="Errata: if Standby is full, the Unit is Nullified.")])
card("4L03", "The Voidslime", "U", "Leader", 1, "All other Units have a Base Rank of 1 and are Blaarg.",
     [eff("Static", [op("SetBaseRank", value=1, targets=units(excludeSelf=True)), op("SetRaces", races=[BL], targets=units(excludeSelf=True))],
          note="Errata: the Units also lose their base Race.")])
card("4L04", "Arrgarrblarr", "U", "Champion", 2, "All other Blaarg Units you control get +1 Rank.", champion(BL))
card("4L05", "The Resonator", "U", "Leader", 0, "Other Units you control get +X Rank, where X is this Unit's Rank.",
     [eff("Static", op("ModifyRank", targets=units(controller="You", excludeSelf=True), amount={"rankOf": "Self"}))])
card("4L06", "Potency Tank", "U", "Leader", 3, "If you would Manipulate, increase the Number by 2.",
     [eff("Static", op("ModifyManipulate", player="You", addMagnitude=2))])
card("4O01", "Claggyness", "O", "Ongoing", None, "Blaarg Units get +1 Rank.", race_buff(BL))
card("4O02", "Stagnation", "O", "Ongoing", None, "Replace all Race words with Blaarg (including Card Races).",
     [eff("Static", op("ReplaceRaceWords", race=BL, scope="AllCards"), note="Errata: if it's on a Card and it's written, it counts.")])
card("4O03", "Primordium", "O", "Ongoing", None, "All Units have a Base Rank of 1.",
     [eff("Static", op("SetBaseRank", value=1, targets=units()))])
card("4O04", "Suppressing Goop", "O", "Ongoing", None, "Suppress all Card Abilities and Effects (even when not in play).",
     [eff("Static", op("Suppress", targets=F(zone="Any", excludeSelf=True), what="AllAbilitiesAndEffects"),
          note="Errata: also disables play restrictions, including extra play costs.")])
card("4O05", "Caustic Creek", "O", "Action", None, "For each Player, Lock an Empty Node that Player controls. Play this card only if each Player has at least 1 Empty Node.",
     [eff("OnResolve", op("Lock", targets=F(choose="Node", perPlayer=True, filter=F(empty=True))),
          playCondition={"eachPlayerHas": F(empty=True, minCount=1)})])
card("4O06", "Adapt", "O", "Action", None, "Manipulate 2.", [eff("OnResolve", op("Manipulate", amount=2))])
card("4O07", "Slime Moulding", "O", "Action", None, "Manipulate 3. The chosen Unit must be a Blaarg.",
     [eff("OnResolve", op("Manipulate", amount=3, filter=units(race=BL)))])
card("4O08", "Gloop!", "O", "React", None, "Issue this Order as a Unit is Deployed. Suppress all Abilities for that Unit as it is played.",
     [eff("React", op("Suppress", targets="Event.Unit", what="AllAbilities", duration="WhileInPlay"), when=F(event="UnitDeploying"))])
card("4O09", "Dissolve", "O", "Action", None, "Return a Unit to its Controller's Hand.",
     [eff("OnResolve", op("ReturnToHand", targets=F(choose="Unit", filter=units()), hand="Controller"))])
card("4O10", "Forced Mitosis", "O", "React", None, "Issue this Order as a Unit is Deployed. That Unit enters play as a copy of another Unit, except it is a Blaarg.",
     [script("React", "EnterAsCopy", when=F(event="UnitDeploying"), params={"subject": "Event.Unit", "chooser": "OrderController", "setRaces": [BL], "copySource": units()})])
card("4U01", "Slimeball", "U", "Underling", 1, "When you play this Unit, if the Opposing Unit is a Synthenoid, you may Nullify it and this Unit gets +2 Rank.", hate_underling("Synthenoid"))
card("4U02", "Coiling Loogie", "U", "Minion", 2, "When you play this Unit, you may Nullify the Opposing Unit if it is a Synthenoid.", hate_minion("Synthenoid"))
card("4U03", "Graftling", "U", "Underling", 1, "When you play this Unit, you may Manipulate 2.",
     [eff("OnPlay", op("Manipulate", amount=2), optional=True)])
card("4U04", "Gestating Globule", "U", "Minion", 2, "When you play this Unit, you may Manipulate 1.",
     [eff("OnPlay", op("Manipulate", amount=1), optional=True)])
card("4U05", "Gelatinous Mass", "U", "Brute", 4, "", [])
card("4U06", "Regeneratrix", "U", "Minion", 2, "During Cleanup, you may place this Unit into your Standby Zone instead of Retiring it.", stay_standby())
card("4U07", "Bag of Bones", "U", "Minion", 2, "If an Opponent controls any Synthenoid Units, this Unit gets +2 Rank.", hate_static("Synthenoid"))
card("4U08", "Conductive Vessel", "U", "Minion", 2, "If this unit is Nullified, Nullify the Opposing Unit as well.", revenge())
card("4U09", "Abominable Ooze", "U", "Underling", 1, "When you play this Unit, Lock up to 1 Empty Node. When this Unit leaves play, Unlock that Node.", locker())

# ===== SYNTHENOID =====
SY = "Synthenoid"
card("5L01", "Mechatron", "U", "Leader", 8, "This Unit can only be played if you Retire 2 other Synthenoid Unit Cards from your Hand.",
     [eff("PlayCost", op("RetireFromHand", count=2, filter=F(kind="Unit", race=SY))),
      eff("Static", op("DeployRequiresPlayable", targets="Self"), note="Errata: cannot be Deployed unless it can be Played.")])
card("5L02", "10K-LAN", "U", "Leader", 3, "When you play this Unit, Retire any number of Synthenoid Cards from your Hand, then Lock that many Empty Nodes.",
     [eff("OnPlay", [op("RetireFromHand", min=0, max="Any", filter=F(race=SY), countAs="n"),
                     op("Lock", targets=F(choose="Node", count="Count.n", filter=F(empty=True)))])])
card("5L03", "SAL-VGR", "U", "Leader", 1, "When you play this Unit, place up to 3 Retired Synthenoid Units into your Standby Zone. Return any of those Units with Assemble to your hand instead.",
     [eff("OnPlay", op("MoveToStandby", targets=F(choose="Card", min=0, max=3, filter=F(zone="RetiredPile", kind="Unit", race=SY)),
                       standbyOf="You", gainControl=True, ifHasKeyword={"Assemble": "ReturnToHand"}))])
card("5L04", "Mechaniking", "U", "Champion", 2, "All other Synthenoid Units you control get +1 Rank.", champion(SY))
card("5L05", "Cntrl-C Cntrl-V", "U", "Leader", 3, "Whenever you Assemble onto another Unit, Assemble onto Cntrl-C Cntrl-V as well.",
     [eff("OnEvent", op("AddAssembledRank", targets="Self", amount="Event.AssembledRank"), event="Assembled",
          filter=F(player="You", targetNot="Self"))])
card("5L06", "TR-1993.r", "U", "Leader", 1, "When you play this Unit, you may Trigger any number of Units' Triggered Abilities.",
     [script("OnPlay", "RetriggerAbilities", params={"abilities": "OnPlay", "units": "AnyInPlay", "min": 0, "max": "Any", "choicesBy": "UnitController"}, note="Ruling: re-run the 'When you play' abilities of any chosen Units in play, each as its controller.")])
card("5O01", "Rise of the Robots", "O", "Ongoing", None, "Synthenoid Units get +1 Rank.", race_buff(SY))
card("5O02", "Rapid Fabrication", "O", "Ongoing", None, "Whenever you Play or Retire a Synthenoid Card from your Hand, you may Draw a Card.",
     [eff("OnEvent", op("Draw", count=1, deckChoice="PerCard"), optional=True, event=["CardPlayedFromHand", "CardRetiredFromHand"],
          filter=F(player="You", race=SY), note="Errata: only the Order's controller draws.")])
card("5O03", "Cybernetic Enhancements", "O", "Ongoing", None, "Synthenoid Unit cards gain Assemble and may be Assembled onto non-Synthenoid Units.",
     [eff("Static", op("GrantKeyword", keyword="Assemble", targets=F(kind="Unit", race=SY, zone="Any"), assembleOnto="AnyUnit"),
          note="Errata: applies to all Synthenoid cards in players' hands.")])
card("5O04", "Containment Network", "O", "Ongoing", None, "As you Issue this Order choose a Race. Units of that Race get -1 Rank.",
     [eff("OnIssue", op("ChooseRace", remember="race")),
      eff("Static", op("ModifyRank", amount=-1, targets=units(race="Remembered.race")))])
card("5O05", "No Gods, No Masters", "O", "Ongoing", None, "Units you control cannot be Nullified if your Leader Node is Empty.",
     [eff("Static", op("CannotBeNullified", targets=units(controller="You")), condition={"node": "YourLeaderNode", "empty": True},
          note="Errata: a Locked Node is not Empty.")])
card("5O06", "Scrapped Metal", "O", "Action", None, "Retire a Synthenoid Card from your Hand to Draw 2 Cards.",
     [eff("PlayCost", op("RetireFromHand", count=1, filter=F(race=SY))), eff("OnResolve", op("Draw", count=2, deckChoice="PerCard"))])
card("5O07", "Death Laser", "O", "Action", None, "Nullify a Unit if it is Opposed by a Synthenoid Unit.",
     [eff("OnResolve", op("Nullify", targets=F(choose="Unit", filter=units(opposingUnitRace=SY))))])
card("5O08", "Salvage for Parts", "O", "Action", None, "Assemble a Retired or Nullified Synthenoid Unit card onto a Synthenoid Unit in play.",
     [eff("OnResolve", op("Assemble", source=F(choose="Card", filter=F(zone=["RetiredPile", "NullifiedPile"], kind="Unit", race=SY)),
                          onto=F(choose="Unit", filter=units(race=SY)), ignoreKeywordRequirement=True))])
card("5O09", "Fatal Error", "O", "Action", None, "Nullify a Synthenoid Unit.",
     [eff("OnResolve", op("Nullify", targets=F(choose="Unit", filter=units(race=SY))))])
card("5O10", "Titanium Plating", "O", "Action", None, "Choose a Synthenoid Unit. That Unit gets +3 Rank.",
     [eff("OnResolve", op("ModifyRank", amount=3, targets=F(choose="Unit", filter=units(race=SY)), duration="WhileInPlay"))])
card("5U01", "Bolt Thrower", "U", "Underling", 1, "When you play this Unit, if the Opposing Unit is a Space Cowboy, you may Nullify it and this Unit gets +2 Rank.", hate_underling(SC))
card("5U02", "Whirling Dervish", "U", "Minion", 2, "When you play this Unit, you may Nullify the opposing Unit if it is a Space Cowboy.", hate_minion(SC))
card("5U03", "Sprocket Spider", "U", "Underling", 1, "This Card may be Assembled onto other Synthenoid Units.", [], keywords=["Assemble"])
card("5U04", "Cogwork Soldier", "U", "Minion", 2, "This Card may be Assembled onto other Synthenoid Units.", [], keywords=["Assemble"])
card("5U05", "Siege Breaker", "U", "Brute", 4, "", [])
card("5U06", "Repair Drone", "U", "Minion", 2, "During Cleanup, you may place this Unit into your Standby Zone instead of Retiring it.", stay_standby())
card("5U07", "Terminator", "U", "Minion", 2, "If an opponent controls any Space Cowboy Units, this Unit gets +2 Rank.", hate_static(SC))
card("5U08", "Hoverbomber", "U", "Minion", 2, "If this Unit is Nullified, Nullify the Opposing Unit as well.", revenge())
card("5U09", "Hackbot", "U", "Underling", 1, "When you play this Unit, Lock up to 1 Empty Node. When this Unit leaves play, Unlock that Node.", locker())

# ===== WARZONES EXPANSION =====
def wz_buff(r, n): return [eff("Static", op("ModifyRank", amount=n, targets=units(race=r)))]
card("0W01", "Military Academy", "W", None, None, "There are no more than 3 Deployment Turns for each Player whilst this Warzone is in play.",
     [eff("Static", op("LimitTurns", kind="DeployUnit", max=3, perPlayer=True), note="Ruling: counts Unit deploys from any zone (hand, Standby, piles).")])
card("0W02", "Senate Chambers", "W", None, None, "If a Player fills all 4 Nodes they control, they Win the Round.",
     [eff("StateCheck", op("WinRound", player="Event.Player"), condition={"playerFillsAll": ["Unit", "Leader"]}, timing="Immediate", note="Ruling: checked immediately; the round ends at once.")])
card("0W03", "Galactic Capital", "W", None, None, "Galactic Council Units get +1 Rank.", wz_buff("GalacticCouncil", 1))
card("0W04", "The Campaign Trail", "W", None, None, "Leader Units get +3 Rank.",
     [eff("Static", op("ModifyRank", amount=3, targets=units(unitClass="Leader")))])
card("0W05", "Fallout Shelter", "W", None, None, "If a Unit would be Nullified, its Controller may place it in the Standby Zone instead.",
     [eff("Replacement", op("MoveToStandby", targets="Event.Unit", standbyOf="Controller", ifNoRoom="Nullify"), optional=True,
          decider="Event.Unit.Controller", replaces=F(event="UnitNullifying"))])
card("1W01", "Planet Earth", "W", None, None, "Space Cowboy Units get +1 Rank.", wz_buff(SC, 1))
card("1W02", "The Scorched Lands", "W", None, None, "Necrolite Units get -1 Rank.", wz_buff(NE, -1))
card("1W03", "Mercenary Camp", "W", None, None, "Each time a Unit is Nullified by an Opponent of that Unit's Controller, that Opponent may Draw a Card.",
     [eff("OnEvent", op("Draw", count=1, player="Event.Source.Controller", deckChoice="PerCard"), optional=True,
          event="UnitNullified", filter=F(sourceIsOpponentOfUnitController=True))])
card("1W04", "Training Grounds", "W", None, None, "Underling Units get +2 Rank.",
     [eff("Static", op("ModifyRank", amount=2, targets=units(unitClass="Underling")))])
card("2W01", "Nyxthian Hatchery", "W", None, None, "Necrolite Units get +1 Rank.", wz_buff(NE, 1))
card("2W02", "Vivisection Laboratory", "W", None, None, "Overmind Units get -1 Rank.", wz_buff(OV, -1))
card("2W03", "Hills of Decay", "W", None, None, "Nullified Necrolite Units may be Deployed as if they were in Player's Hands.",
     [eff("Static", op("PlayFromZone", zone="NullifiedPile", filter=F(kind="Unit", race=NE), player="Each"))])
card("2W04", "Swarm Warrens", "W", None, None, "Minion Units get +2 Rank.",
     [eff("Static", op("ModifyRank", amount=2, targets=units(unitClass="Minion")))])
card("3W01", "Central Hive", "W", None, None, "Overmind Units get +1 Rank.", wz_buff(OV, 1))
card("3W02", "Cryogenic Chambers", "W", None, None, "Blaarg Units get -1 Rank.", wz_buff(BL, -1))
card("3W03", "Oracle's Arch", "W", None, None, "Players play with their Hands revealed.",
     [eff("Static", op("RevealHands", players="All"))])
card("3W04", "The Oculus", "W", None, None, "The Player with the lowest total Rank wins instead of the Player with the greatest. Players may place Units on their Opponent's nodes.",
     [eff("Static", op("InvertRoundWinner")),
      eff("Static", op("PlacementOverride", targets=F(kind="Unit"), allowedNodes="AlsoOpponentNodes"))])
card("4W01", "Blaarxis", "W", None, None, "Blaarg Units get +1 Rank.", wz_buff(BL, 1))
card("4W02", "Corrosive Channels", "W", None, None, "Synthenoid Units get -1 Rank.", wz_buff(SY, -1))
card("4W03", "Primordial Marsh", "W", None, None, "Whenever a Card is played, its Controller may Manipulate 1.",
     [eff("OnEvent", op("Manipulate", amount=1, player="Event.Card.Controller"), optional=True, event="CardPlayed")])
card("4W04", "Refraction Network", "W", None, None, "Suppress all Abilities that would Trigger when Cards are Played.",
     [eff("Static", op("Suppress", targets=F(zone="Any"), what="TriggeredOn:Played"))])
card("5W01", "Cyber Moon", "W", None, None, "Synthenoid Units get +1 Rank.", wz_buff(SY, 1))
card("5W02", "Vacuum Spire", "W", None, None, "Space Cowboy Units get -1 Rank.", wz_buff(SC, -1))
card("5W03", "Production Facility", "W", None, None, "All Synthenoid Unit Cards gain Assemble.",
     [eff("Static", op("GrantKeyword", keyword="Assemble", targets=F(kind="Unit", race=SY, zone="Any")))])
card("5W04", "Polarity Storm", "W", None, None, "Effects that would increase a Unit's Rank Decrease their Rank instead, and vice versa.",
     [eff("Static", op("InvertRankModifiers"))])
card("6O01", "Wormhole", "O", "Action", None, "Look at the top 3 Warzones. Retire the current Warzone(s) and put 1 of the other Warzones in play. Retire the rest.",
     [script("OnResolve", "Wormhole")])
card("6O02", "Atmosphere Generator", "O", "Ongoing", None, "Suppress all Warzones.",
     [eff("Static", op("Suppress", targets=F(kind="Warzone"), what="AllAbilitiesAndEffects"))])
card("6O03", "Dimensional Rift", "O", "Action", None, "Add the top 2 Warzones to the current Warzone. Play this Card only if 1 Warzone is in play.",
     [eff("OnResolve", op("RevealWarzones", count=2, add=True), playCondition={"activeWarzones": 1})])
card("6W01", "Uncharted Space", "W", None, None, "(No Effect)", [])
card("6W02", "Black Hole", "W", None, None, "If a Unit would be Retired or put into a Standby Zone, Nullify it instead.",
     [eff("Replacement", op("Nullify", targets="Event.Unit"), replaces=F(event=["UnitRetiring", "UnitMovingToStandby"]))])

# ---------- assemble ----------
PRINTED = {"0L02": "Ace, Council Tactcian", "0U02": "Tyramt Toppler", "1L04": "Dustry Trailblazer",
           "1L03": "Y'nsoh, Galactic Slaver", "3O05": "Psychic Coersion", "4O04": "Supressing Goop", "4W03": "Priomordial Marsh"}
FOLDER = {"0": "0 Galactic Council", "1": "1 Space Cowboy", "2": "2 Necrolite", "3": "3 Overmind",
          "4": "4 Blaarg", "5": "5 Synthenoid"}
def clean_flavor(s):
    s = re.sub(r"^\s*[.\*\s]+(?=[A-Z])", "", s)
    s = s.replace("’Il", "’ll").replace("(No Effect) ", "").replace("dabble in Al.", "dabble in AI.")
    return s.strip()

cards = []
for cid in sorted(C, key=lambda k: (k[1] in "W" or k[0] == "6", k)):
    c = C[cid]
    race = RACE[cid[0]]
    expansion = cid[1] == "W" or cid[0] == "6"
    d = {"id": cid, "name": c["name"], "set": "Warzones" if expansion else "Base"}
    if c["kind"] == "U":
        d.update(kind="Unit", unitClass=c["sub"], baseRank=c["rank"])
    elif c["kind"] == "O":
        d.update(kind="Order", orderType=c["sub"])
    else:
        d.update(kind="Warzone")
    d["races"] = [race]
    if cid in PRINTED: d["printedName"] = PRINTED[cid]
    if c.get("keywords"): d["keywords"] = c["keywords"]
    d["text"] = c["rules"]
    d["flavor"] = clean_flavor(SPLIT[cid]["flavor"])
    d["image"] = f"images/{cid}.jpg"
    d["effects"] = c["effects"]
    cards.append(d)

assert len(cards) == 177, len(cards)
json.dump({"schemaVersion": 1, "races": list(RACE.values()), "weakness": WEAK_TARGET, "cards": cards},
          open(OUT, "w"), indent=1, ensure_ascii=False)
print("cards:", len(cards), "scripted:", sum(1 for c in cards for e in c["effects"] if "script" in e))
