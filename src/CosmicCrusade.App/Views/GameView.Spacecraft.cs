using System.Windows.Controls;
using CosmicCrusade.App.Board;
using CosmicCrusade.App.Controls;
using CosmicCrusade.Core.Engine;
using CosmicCrusade.Core.Model;
using CosmicCrusade.Core.Rules;

namespace CosmicCrusade.App.Views;

/// <summary>
/// Spacecraft module UI (design/spacecraft.md): a spacecraft and its crew share a node, drawn offset so both stay
/// visible (ship up-left, crew down-right in front). Boarding without a Deploy (Beam Up, Chrome Steed, Abductor,
/// Mindscrambler, a crewed spacecraft advancing) and the Mothership's Fighter tokens get their own visuals.
/// </summary>
public partial class GameView
{
    /// <summary>Cards currently drawn at a crewed-node offset (their instance ids).</summary>
    private readonly HashSet<int> _offsetCards = new();

    /// <summary>
    /// Re-places the cards on crewed nodes (and those that just stopped being crewed) after every event, so crews stay
    /// in front of their spacecraft. Cards on other nodes are left alone (their animations aren't interrupted).
    /// </summary>
    private void SyncCrewLayout()
    {
        if (_state is null) return;
        var now = new HashSet<int>();
        foreach (var node in State.AllNodes)
        {
            if (node.Crew is null) continue;
            foreach (var c in node.Occupants) now.Add(c.InstanceId);
            if (_boardCards.TryGetValue(node.Crew.InstanceId, out var crewView)) Panel.SetZIndex(crewView, 12);
            if (node.Card is { } ship && _boardCards.TryGetValue(ship.InstanceId, out var shipView)) Panel.SetZIndex(shipView, 10);
        }

        // Crewed nodes: ship and crew at their offsets. Cards that were offset but no longer are: back to the centre.
        foreach (var id in now.Concat(_offsetCards).Distinct().ToList())
        {
            if (!_boardCards.TryGetValue(id, out var v) || v.Card?.Location.Node is not { } node) continue;
            PlaceOnBoard(v, node);
            if (!now.Contains(id)) Panel.SetZIndex(v, 10);
        }

        _offsetCards.Clear();
        _offsetCards.UnionWith(now);
    }

    private async Task OnCrewBoardedAsync(CrewBoardedEvent e)
    {
        UpdatePiles();

        // A crew coming out of a pile (Chrome Steed: Retired, Sarcophagar: Nullified) has no board view yet, or only a
        // stale one: fly the card from the pile up onto the spacecraft, then put a fresh view there.
        bool fromPile = e.FromPile is not null || e.FromNode is null;
        if (_boardCards.TryGetValue(e.Crew.InstanceId, out var old) && (fromPile || !BoardCanvas.Children.Contains(old)))
        {
            BoardCanvas.Children.Remove(old);
            _boardCards.Remove(e.Crew.InstanceId);
        }
        if (e.FromPile is PileKind pile)
        {
            Sfx.Play(SfxKind.Tractor, 0.7);
            var target = ProjectRect(BoardLayout.CardRectAt(BoardLayout.CardCenter(e.Node, e.Crew)));
            await FlyAsync(e.Crew, PileCardRect(pile), target, false, false, 560, Cyan);
        }
        if (!_boardCards.TryGetValue(e.Crew.InstanceId, out var view))
            view = AddBoardCard(e.Crew, e.Node);
        if (e.FromNode is not null && e.FromNode != e.Node)
        {
            TractorBeam(BoardLayout.Center(e.Node), BoardLayout.Center(e.FromNode), 520);
            Sfx.Play(SfxKind.Tractor, 0.8);
        }
        else Sfx.Play(SfxKind.Thruster, 0.7);
        PlaceOnBoard(view, e.Node);
        Panel.SetZIndex(view, 12);
        if (e.Node.Card is { } ship && _boardCards.TryGetValue(ship.InstanceId, out var shipView))
        {
            PlaceOnBoard(shipView, e.Node); // the spacecraft slides back to make room
            GlowBriefly(shipView, Cyan, 1200);
        }
        GlowBriefly(view, Cyan, 1200);
        Shockwave(BoardLayout.CardCenter(e.Node, e.Crew), Cyan);
        _hud.AddLog($"  {e.Crew.Name} boards {e.Ship.Name}.");
        await Anim.Delay(260);
    }

    private async Task OnTokenCreatedAsync(TokenCreatedEvent e)
    {
        var view = AddBoardCard(e.Token, e.Node);
        Panel.SetZIndex(view, 10);
        Shockwave(BoardLayout.Center(e.Node), Gold);
        Sfx.Play(SfxKind.Thruster, 0.6, minGapMs: 120);
        _hud.AddLog($"  {e.Source?.Name ?? "A spacecraft"} launches a {e.Token.Name} under {e.Node.Crew?.Name ?? "a Unit"}.");
        await Anim.Delay(180);
    }
}
