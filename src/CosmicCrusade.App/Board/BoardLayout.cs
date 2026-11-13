using System.Windows;
using CosmicCrusade.App.Rendering;
using CosmicCrusade.Core.Model;

namespace CosmicCrusade.App.Board;

/// <summary>
/// Positions on the 1600×1000 board surface (the texture of the tilted 3D plane).
/// The local player (<see cref="Perspective.Me"/>) is the bottom half, the opponent the top half; the control line is y = 500.
///
///   x:  O-col 120 | L 340 | U1/S1 560 | U2/S2 760 | U3/S3 960 | piles 1290 / 1470
///   Warzones (top right): Active Warzone 1290,140 · Warzone deck 1470,140 · Archon deck 1470,860 (beside Issued)
///   y:  AI back 130, AI front 360 | line 500 | you front 640, you back 870
/// </summary>
public static class BoardLayout
{
    public const double Width = 1600;
    public const double Height = 1000;
    public const double ControlLineY = 500;

    public const double CardW = 150;
    public static double CardH => CardW / CardFaceFactory.Aspect;
    public const double SlotW = 166;
    public const double SlotH = 226;

    private const double FrontYMe = 640, BackYMe = 870, FrontYOpp = 360, BackYOpp = 130;

    public static Point Center(Node node)
    {
        bool me = node.Side == Perspective.Me; // online, the guest (player 1) sits at the bottom of their own screen
        double front = me ? FrontYMe : FrontYOpp;
        double back = me ? BackYMe : BackYOpp;
        return node.Kind switch
        {
            NodeKind.Unit => new Point(560 + 200 * node.Index, front),
            NodeKind.Leader => new Point(340, front),
            NodeKind.Standby => new Point(560 + 200 * node.Index, back),
            NodeKind.Order => new Point(120, node.Index == 0 ? front : back),
            _ => new Point(Width / 2, ControlLineY),
        };
    }

    /// <summary>Spacecraft module: a crewed spacecraft sits up-left on its node, its crew down-right in front of it.</summary>
    public const double ShipDX = -22, ShipDY = -26, CrewDX = 22, CrewDY = 26;

    /// <summary>
    /// Where a card on <paramref name="node"/> is drawn: the node centre, or (Spacecraft) offset when the node holds a
    /// spacecraft and its crew, so both stay visible.
    /// </summary>
    public static Point CardCenter(Node node, CardInstance? card)
    {
        var c = Center(node);
        if (card is null || node.Crew is null) return c;
        if (ReferenceEquals(node.Crew, card)) return new Point(c.X + CrewDX, c.Y + CrewDY);
        if (ReferenceEquals(node.Card, card)) return new Point(c.X + ShipDX, c.Y + ShipDY);
        return c;
    }

    public static Point PileCenter(PileKind pile) => pile switch
    {
        PileKind.UnitDeck => new Point(1290, 385),
        PileKind.OrderDeck => new Point(1470, 385),
        PileKind.RetiredUnits => new Point(1290, 615),
        PileKind.NullifiedUnits => new Point(1470, 615),
        PileKind.IssuedOrders => new Point(1290, 860),
        PileKind.ActiveWarzone => new Point(1290, 140),
        PileKind.WarzoneDeck => new Point(1470, 140),
        PileKind.ArchonDeck => new Point(1470, 860),
        _ => new Point(Width / 2, ControlLineY),
    };

    public static Rect CardRectAt(Point center) =>
        new(center.X - CardW / 2, center.Y - CardH / 2, CardW, CardH);

    public static string PileLabel(PileKind pile) => pile switch
    {
        PileKind.UnitDeck => "UNIT DECK",
        PileKind.OrderDeck => "ORDER DECK",
        PileKind.RetiredUnits => "RETIRED",
        PileKind.NullifiedUnits => "NULLIFIED",
        PileKind.IssuedOrders => "ISSUED",
        PileKind.WarzoneDeck => "WARZONES",
        PileKind.ActiveWarzone => "ACTIVE WARZONE",
        PileKind.ArchonDeck => "ARCHONS",
        _ => "",
    };
}
