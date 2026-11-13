using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using CosmicCrusade.App.Board;
using CosmicCrusade.App.Controls;
using CosmicCrusade.Core.Engine;
using CosmicCrusade.Core.Model;
using Path = System.Windows.Shapes.Path;

namespace CosmicCrusade.App.Views;

/// <summary>
/// Archons module UI: taking an Archon (it sits face up in its owner's hand row), losing it at Clean-up, the
/// "invocation" every Archon gets as it is played (ancient galactic power: a veil, a turning ring of runes, light rays
/// and the card itself), and a themed flourish per Archon.
/// </summary>
public partial class GameView
{
    private static readonly Color ArchonBlue = Color.FromRgb(1, 126, 255);
    private static readonly Color ArchonGlow = Color.FromRgb(120, 200, 255);
    private static readonly Color ArchonGold = Color.FromRgb(255, 214, 120);

    private static bool IsArchon(CardInstance? c) => c is not null && c.PrintedDefinition.IsArchon;

    // =================================================================== taking / losing

    private async Task OnArchonTakenAsync(ArchonTakenEvent e)
    {
        string fewer = e.Skipped == PileKind.UnitDeck ? "Unit" : "Order";
        _hud.AddLog($"{Who(e.Player)} take{(e.Player == Me ? "" : "s")} the Archon draw: {e.Archon.Name} (one fewer {fewer} this round).");
        UpdatePiles();
        Sfx.Play(SfxKind.ArchonBell, 0.8);

        var deckCenter = ProjectToFx(BoardLayout.PileCenter(PileKind.ArchonDeck));
        if (e.Player == Me)
        {
            var v = CreateHandCard(e.Archon);
            _handViews.Add(v);
            HandCanvas.Children.Add(v);
            var start = FxCanvas.TranslatePoint(deckCenter, HandCanvas);
            v.Translate.X = start.X - v.Width / 2;
            v.Translate.Y = start.Y - v.Height / 2;
            v.Scale.ScaleX = v.Scale.ScaleY = 0.45;
            LayoutHand();
            GlowBriefly(v, ArchonGlow, 1600);
        }
        else
        {
            var v = CreateOpponentHandCard(e.Archon);
            _oppHandViews.Add(v);
            OpponentHandCanvas.Children.Add(v);
            var start = FxCanvas.TranslatePoint(deckCenter, OpponentHandCanvas);
            v.Translate.X = start.X - v.Width / 2;
            v.Translate.Y = start.Y - v.Height / 2;
            LayoutOpponentHand();
            GlowBriefly(v, ArchonGlow, 1600);
        }
        await ShowBannerAsync("ARCHON", $"{(e.Player == Me ? "You hold" : TheOppCap + " holds")} {e.Archon.Name}", ArchonGlow, 900);
    }

    private async Task OnArchonLostAsync(ArchonLostEvent e)
    {
        _hud.AddLog($"  {e.Archon.Name} fades unplayed and returns to the Archon deck.");
        var views = e.Player == Me ? _handViews : _oppHandViews;
        var view = views.FirstOrDefault(v => v.Card == e.Archon);
        if (view is null)
        {
            UpdatePiles();
            return;
        }
        var from = FxBounds(view);
        views.Remove(view);
        (e.Player == Me ? HandCanvas : OpponentHandCanvas).Children.Remove(view);
        if (e.Player == Me) LayoutHand(); else LayoutOpponentHand();
        await FlyAsync(e.Archon, from, PileCardRect(PileKind.ArchonDeck), startFaceDown: false, flipToFaceUp: false, ms: 520,
            glow: ArchonGlow, fadeOut: true);
        UpdatePiles();
    }

    // =================================================================== invocation (every Archon)

    /// <summary>
    /// As an Archon is Deployed or Issued: the board dims under a deep-blue veil, a ring of ancient runes turns, light
    /// rays fan out, stardust implodes into the card and its name burns in. About 1.6 s (shorter with Reduce effects).
    /// </summary>
    private async Task ArchonInvocationAsync(CardInstance card, int player)
    {
        _hud.AddLog($"{(player == Me ? "You invoke" : TheOppCap + " invokes")} the Archon {card.Name}.");
        Sfx.Play(SfxKind.ArchonInvoke, 0.9);
        bool reduced = FxSettings.ReduceEffects;
        double w = Math.Max(1, FxCanvas.ActualWidth), h = Math.Max(1, FxCanvas.ActualHeight);
        var c = new Point(w / 2, h * 0.46);
        var elements = new List<UIElement>();

        // 1. Veil.
        var veil = new Rectangle
        {
            Width = w,
            Height = h,
            Fill = new RadialGradientBrush(Color.FromArgb(150, 6, 28, 70), Color.FromArgb(235, 0, 2, 10)),
            Opacity = 0,
        };
        elements.Add(AddScreenFx(veil, 0, 0, 180));
        _ = Anim.To(veil, OpacityProperty, reduced ? 0.55 : 0.92, 380, Anim.Out);

        // 2. Light rays.
        int rayCount = FxSettings.Count(14);
        for (int i = 0; i < rayCount; i++)
        {
            var ray = new Rectangle
            {
                Width = 10,
                Height = Math.Max(w, h) * 0.8,
                Fill = new LinearGradientBrush(Color.FromArgb(150, ArchonGlow.R, ArchonGlow.G, ArchonGlow.B), Color.FromArgb(0, 0, 60, 160), 90),
                RenderTransformOrigin = new Point(0.5, 0),
                Opacity = 0,
            };
            var rot = new RotateTransform(360.0 * i / rayCount + Rand(-6, 6));
            var sc = new ScaleTransform(1, 0.05);
            var tg = new TransformGroup();
            tg.Children.Add(sc);
            tg.Children.Add(rot);
            ray.RenderTransform = tg;
            elements.Add(AddScreenFx(ray, c.X - 5, c.Y, 181));
            _ = Anim.To(ray, OpacityProperty, Rand(0.35, 0.8), 300, Anim.Out, delayMs: 150);
            _ = Anim.To(sc, ScaleTransform.ScaleYProperty, 1, 900, Anim.Out, delayMs: 150);
            if (!reduced) rot.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(rot.Angle, rot.Angle + 25, Anim.Ms(1800)));
        }

        // 3. The rune ring: two turning circles with procedurally drawn glyphs between them.
        const double ring = 460;
        var host = new Canvas { Width = ring, Height = ring, RenderTransformOrigin = new Point(0.5, 0.5) };
        var spin = new RotateTransform();
        var grow = new ScaleTransform(0.2, 0.2);
        var hostT = new TransformGroup();
        hostT.Children.Add(grow);
        hostT.Children.Add(spin);
        host.RenderTransform = hostT;
        host.Effect = reduced ? null : new DropShadowEffect { Color = ArchonGlow, BlurRadius = 24, ShadowDepth = 0, Opacity = 0.9 };
        foreach (var (d, stroke, dash) in new[] { (ring, ArchonGlow, 0.0), (ring - 70, ArchonGold, 6.0), (ring - 150, ArchonGlow, 2.0) })
        {
            var e = new Ellipse { Width = d, Height = d, Stroke = Solid(stroke, 220), StrokeThickness = dash == 0 ? 3 : 2 };
            if (dash > 0) e.StrokeDashArray = new DoubleCollection { dash, dash * 0.6 };
            Canvas.SetLeft(e, (ring - d) / 2);
            Canvas.SetTop(e, (ring - d) / 2);
            host.Children.Add(e);
        }
        int glyphs = 18;
        for (int i = 0; i < glyphs; i++)
        {
            double a = 2 * Math.PI * i / glyphs;
            double r = (ring - 35) / 2;
            var glyph = Rune(ArchonGold);
            glyph.RenderTransform = new RotateTransform(a * 180 / Math.PI + 90, 9, 9);
            Canvas.SetLeft(glyph, ring / 2 + Math.Cos(a) * r - 9);
            Canvas.SetTop(glyph, ring / 2 + Math.Sin(a) * r - 9);
            host.Children.Add(glyph);
        }
        // A star in the centre: two overlaid triangles.
        foreach (double off in new[] { 0.0, 180.0 })
        {
            var tri = new Polygon
            {
                Points = new PointCollection { new(0, -150), new(130, 75), new(-130, 75) },
                Stroke = Solid(ArchonGlow, 160),
                StrokeThickness = 1.5,
                RenderTransform = new RotateTransform(off),
            };
            Canvas.SetLeft(tri, ring / 2);
            Canvas.SetTop(tri, ring / 2);
            host.Children.Add(tri);
        }
        elements.Add(AddScreenFx(host, c.X - ring / 2, c.Y - ring / 2, 182));
        _ = Anim.To(grow, ScaleTransform.ScaleXProperty, 1, 650, Anim.Back);
        _ = Anim.To(grow, ScaleTransform.ScaleYProperty, 1, 650, Anim.Back);
        spin.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, reduced ? 40 : 160, Anim.Ms(2200)));

        // 4. Stardust implodes into the centre.
        int motes = FxSettings.Count(36);
        for (int i = 0; i < motes; i++)
        {
            double a = Rand(0, Math.PI * 2), d = Rand(260, 520), size = Rand(2, 5);
            var mote = new Ellipse { Width = size, Height = size, Fill = Solid(i % 3 == 0 ? ArchonGold : Colors.White) };
            var t = new TranslateTransform(Math.Cos(a) * d, Math.Sin(a) * d);
            mote.RenderTransform = t;
            elements.Add(AddScreenFx(mote, c.X, c.Y, 183));
            double ms = Rand(500, 900);
            _ = Anim.To(t, TranslateTransform.XProperty, 0, ms, Anim.In, delayMs: 100);
            _ = Anim.To(t, TranslateTransform.YProperty, 0, ms, Anim.In, delayMs: 100);
            _ = FadeAndRemoveScreenAsync(mote, 120, ms);
        }

        // 5. The card itself rises in the middle, with its name.
        var big = new CardView(card, faceDown: false, 230);
        big.SetGlow(ArchonGlow, pulse: true);
        big.Scale.CenterX = big.Width / 2;
        big.Scale.CenterY = big.Height / 2;
        big.Scale.ScaleX = big.Scale.ScaleY = 0.5;
        big.Opacity = 0;
        elements.Add(AddScreenFx(big, c.X - big.Width / 2, c.Y - big.Height / 2, 185));

        var title = new StackPanel { Width = 900, Opacity = 0 };
        title.Children.Add(new TextBlock
        {
            Text = "A R C H O N",
            FontFamily = new FontFamily("Haettenschweiler, Impact"),
            FontSize = 26,
            Foreground = Solid(ArchonGold),
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        title.Children.Add(new TextBlock
        {
            Text = card.Name.ToUpperInvariant(),
            FontFamily = new FontFamily("Haettenschweiler, Impact"),
            FontSize = 64,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            Effect = new DropShadowEffect { Color = ArchonGlow, BlurRadius = 28, ShadowDepth = 0, Opacity = 1 },
        });
        elements.Add(AddScreenFx(title, c.X - 450, c.Y + big.Height / 2 + 10, 186));

        await Anim.Delay(reduced ? 250 : 650);
        CameraPush(1.035, 900);
        Shake(0.5);
        _ = Anim.To(big, OpacityProperty, 1, 260, Anim.Out);
        _ = Anim.To(big.Scale, ScaleTransform.ScaleXProperty, 1, 420, Anim.Back);
        _ = Anim.To(big.Scale, ScaleTransform.ScaleYProperty, 1, 420, Anim.Back);
        _ = Anim.To(title, OpacityProperty, 1, 300, Anim.Out, delayMs: 120);

        // A flash where the stardust meets.
        var flash = new Ellipse
        {
            Width = 240,
            Height = 240,
            Fill = new RadialGradientBrush(Color.FromArgb((byte)(255 * FxSettings.Flash(1)), 255, 255, 255), Color.FromArgb(0, 120, 200, 255)),
            Opacity = 1,
        };
        elements.Add(AddScreenFx(flash, c.X - 120, c.Y - 120, 184));
        _ = FadeAndRemoveScreenAsync(flash, 500);

        await Anim.Delay(reduced ? 500 : 1000);

        foreach (var el in elements)
            if (FxCanvas.Children.Contains(el)) _ = FadeAndRemoveScreenAsync(el, 380);
        await Anim.Delay(250);
    }

    /// <summary>A small "ancient" glyph: a few random strokes and a dot inside an 18×18 box.</summary>
    private Path Rune(Color color)
    {
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            int strokes = 2 + _fxRng.Next(2);
            for (int s = 0; s < strokes; s++)
            {
                ctx.BeginFigure(new Point(Rand(2, 16), Rand(1, 6)), false, false);
                ctx.LineTo(new Point(Rand(2, 16), Rand(7, 12)), true, false);
                ctx.LineTo(new Point(Rand(2, 16), Rand(12, 17)), true, false);
            }
            ctx.BeginFigure(new Point(9, 8), true, true);
            ctx.ArcTo(new Point(9.01, 8), new Size(1.5, 1.5), 0, true, SweepDirection.Clockwise, true, false);
        }
        g.Freeze();
        return new Path { Data = g, Stroke = Solid(color), StrokeThickness = 1.6, Width = 18, Height = 18, Fill = Solid(color) };
    }

    // =================================================================== per-Archon flourishes

    /// <summary>Actions (before their effect lands): called on the Archon's OnResolve trigger.</summary>
    private async Task ArchonActionFlourishAsync(CardInstance card, int controller)
    {
        switch (card.PrintedDefinition.Name)
        {
            case "Nebula Cannon":
                await NebulaChargeAsync();
                break;
            case "Planetscape":
                await PlanetscapeAsync();
                break;
            case "Ceasefire":
                Sfx.Play(SfxKind.ArchonBell, 0.7);
                await PeaceWaveAsync();
                break;
            case "Insurrection":
                await InsurrectionAsync(controller);
                break;
            case "Quantum Leap":
                await TesseractAsync();
                break;
        }
    }

    /// <summary>Ongoings once they sit on their Order Node; Units once they land.</summary>
    private async Task ArchonArrivalFlourishAsync(CardInstance card, int controller, Node? node)
    {
        var mine = State.Players[controller].UnitsInPlay
            .Select(u => _boardCards.TryGetValue(u.InstanceId, out var v) ? v : null).OfType<CardView>().ToList();
        switch (card.PrintedDefinition.Name)
        {
            case "Archon Arsenal":
                Sfx.Play(SfxKind.Servo, 0.7);
                foreach (var v in mine)
                {
                    var at = BoardCenterOf(v);
                    Shockwave(at, ArchonBlue);
                    SparkBurst(at, ArchonGlow, 14, 80, 6);
                    GlowBriefly(v, ArchonGlow, 900);
                    await Anim.Delay(110);
                }
                break;

            case "Unstoppable":
                Sfx.Play(SfxKind.Shield, 0.8);
                foreach (var v in mine)
                {
                    ShieldBubble(BoardCenterOf(v));
                    GlowBriefly(v, ArchonGold, 1000);
                    await Anim.Delay(90);
                }
                break;

            case "Galactic Siege":
                await SiegeShadowAsync(controller);
                break;

            case "Open Source":
                await DataRainAsync(controller);
                break;

            case "Deify":
            {
                var leader = State.Players[controller].UnitsInPlay.FirstOrDefault(u => u.Definition.IsLeader);
                if (leader is not null && _boardCards.TryGetValue(leader.InstanceId, out var lv))
                    await DivineLightAsync(BoardCenterOf(lv));
                else if (node is not null)
                    await DivineLightAsync(BoardLayout.Center(node));
                break;
            }

            case "The Unnameable":
                if (node is not null) await UnnameableAsync(BoardLayout.Center(node));
                break;

            case "Androsiar":
                if (node is not null)
                {
                    Sfx.Play(SfxKind.Mirror, 0.8);
                    var at = BoardLayout.Center(node);
                    PsionicAura(at, card.InstanceId);
                    Shockwave(at, PsiViolet);
                    await Anim.Delay(300);
                    Shockwave(at, ArchonGlow);
                }
                break;
        }
    }

    // ------------------------------------------------------------------ Nebula Cannon

    /// <summary>The orbital ring above the table charges up (the strike comes with the Nullify / Lock).</summary>
    private async Task NebulaChargeAsync()
    {
        Sfx.Play(SfxKind.Holo, 0.6);
        var c = new Point(BoardLayout.Width / 2, BoardLayout.ControlLineY);
        var halo = new Ellipse
        {
            Width = 520,
            Height = 220,
            Stroke = Solid(ArchonGlow, 230),
            StrokeThickness = 6,
            StrokeDashArray = new DoubleCollection { 1, 1.5 },
            Effect = new DropShadowEffect { Color = ArchonGlow, BlurRadius = 30, ShadowDepth = 0 },
            Opacity = 0,
        };
        AddFx(halo, c.X - 260, c.Y - 110, 60);
        _ = Anim.To(halo, OpacityProperty, 1, 300, Anim.Out);
        await Anim.Delay(500);
        _ = FadeAndRemoveAsync(halo, 400, 300);
    }

    /// <summary>A column of light from the sky onto a node.</summary>
    private async Task NebulaBeamAsync(Point at, bool small = false)
    {
        Sfx.Play(SfxKind.ArchonBeam, small ? 0.5 : 0.9);
        double width = small ? 60 : 130;
        var beam = new Rectangle
        {
            Width = width,
            Height = at.Y + 200,
            Fill = new LinearGradientBrush(
                new GradientStopCollection
                {
                    new(Color.FromArgb(0, 120, 220, 255), 0),
                    new(Color.FromArgb(200, 170, 235, 255), 0.7),
                    new(Color.FromArgb(255, 255, 255, 255), 1),
                }, 90),
            Effect = new BlurEffect { Radius = FxSettings.HighQuality ? 14 : 4 },
            RenderTransformOrigin = new Point(0.5, 0.5),
        };
        var sx = new ScaleTransform(0.1, 1);
        beam.RenderTransform = sx;
        AddFx(beam, at.X - width / 2, -200, 70);
        await Anim.To(sx, ScaleTransform.ScaleXProperty, 1, 160, Anim.Out);
        if (!small)
        {
            Shockwave(at, ArchonGlow);
            SparkBurst(at, Colors.White, 18, 110, 6);
            Shake(0.8);
        }
        await Anim.Delay(small ? 120 : 260);
        await Anim.To(sx, ScaleTransform.ScaleXProperty, 0, 260, Anim.In);
        RemoveFx(beam);
    }

    // ------------------------------------------------------------------ Planetscape

    /// <summary>A planet forms over the board, cracks with light and bursts: a white flash and a board-wide shockwave.</summary>
    private async Task PlanetscapeAsync()
    {
        var c = new Point(BoardLayout.Width / 2, BoardLayout.ControlLineY);
        const double size = 300;
        var planet = new Grid { Width = size, Height = size, Opacity = 0 };
        planet.Children.Add(new Ellipse
        {
            Fill = new RadialGradientBrush(Color.FromRgb(90, 120, 170), Color.FromRgb(14, 18, 40)) { GradientOrigin = new Point(0.35, 0.3) },
            Effect = new DropShadowEffect { Color = Color.FromRgb(255, 140, 60), BlurRadius = 40, ShadowDepth = 0, Opacity = 0.8 },
        });
        var cracks = new Canvas { Width = size, Height = size, Opacity = 0 };
        for (int i = 0; i < 9; i++)
        {
            double a = Rand(0, Math.PI * 2);
            var pl = new Polyline { Stroke = Solid(Color.FromRgb(255, 190, 90)), StrokeThickness = Rand(2, 4) };
            var p = new Point(size / 2, size / 2);
            pl.Points.Add(p);
            for (int k = 0; k < 4; k++)
            {
                a += Rand(-0.6, 0.6);
                p = new Point(p.X + Math.Cos(a) * Rand(20, 40), p.Y + Math.Sin(a) * Rand(20, 40));
                pl.Points.Add(p);
            }
            cracks.Children.Add(pl);
        }
        cracks.Effect = new DropShadowEffect { Color = Color.FromRgb(255, 160, 60), BlurRadius = 16, ShadowDepth = 0 };
        planet.Children.Add(cracks);
        planet.Clip = new EllipseGeometry(new Point(size / 2, size / 2), size / 2, size / 2);
        AddFx(planet, c.X - size / 2, c.Y - size / 2, 65);

        Sfx.Play(SfxKind.ArchonQuake, 0.9);
        await Anim.To(planet, OpacityProperty, 1, 400, Anim.Out);
        await Anim.To(cracks, OpacityProperty, 1, 600, Anim.In);
        Shake(1.6);

        var flash = new Rectangle
        {
            Width = BoardLayout.Width,
            Height = BoardLayout.Height,
            Fill = Brushes.White,
            Opacity = FxSettings.Flash(0.9),
        };
        AddFx(flash, 0, 0, 90);
        RemoveFx(planet);
        foreach (var x in new[] { 300.0, 800, 1300 })
            Shockwave(new Point(x, BoardLayout.ControlLineY), Color.FromRgb(255, 170, 80));
        for (int i = 0; i < FxSettings.Count(5); i++)
            SparkBurst(new Point(c.X + Rand(-200, 200), c.Y + Rand(-120, 120)), Color.FromRgb(255, 170, 80), 12, 160, 7);
        await FadeAndRemoveAsync(flash, 700);
    }

    // ------------------------------------------------------------------ Ceasefire, Insurrection, Quantum Leap

    /// <summary>A soft white wave rolls out from the control line: the guns fall silent.</summary>
    private async Task PeaceWaveAsync()
    {
        var c = new Point(BoardLayout.Width / 2, BoardLayout.ControlLineY);
        for (int i = 0; i < 3; i++)
        {
            var ring = new Ellipse
            {
                Width = 200,
                Height = 120,
                Stroke = Solid(Colors.White, 200),
                StrokeThickness = 10,
                Effect = new BlurEffect { Radius = 8 },
                RenderTransformOrigin = new Point(0.5, 0.5),
            };
            var sc = new ScaleTransform(0.3, 0.3);
            ring.RenderTransform = sc;
            AddFx(ring, c.X - 100, c.Y - 60, 70);
            _ = Anim.To(sc, ScaleTransform.ScaleXProperty, 9, 1600, Anim.Out, delayMs: i * 250);
            _ = Anim.To(sc, ScaleTransform.ScaleYProperty, 9, 1600, Anim.Out, delayMs: i * 250);
            _ = FadeAndRemoveAsync(ring, 1600, i * 250);
        }
        await ShowBannerAsync("CEASEFIRE", "The guns fall silent", Colors.White, 800);
    }

    /// <summary>Every Unit of the rising side sends up a column of sparks; a crowd roars.</summary>
    private async Task InsurrectionAsync(int controller)
    {
        Sfx.Play(SfxKind.Crowd, 0.9);
        var crimson = Color.FromRgb(255, 80, 70);
        foreach (var u in State.Players[controller].UnitsInPlay)
        {
            if (!_boardCards.TryGetValue(u.InstanceId, out var v)) continue;
            var at = BoardCenterOf(v);
            Shockwave(at, crimson);
            for (int k = 0; k < 3; k++) SparkBurst(new Point(at.X, at.Y - k * 40), ArchonGold, 10, 70, 6);
            GlowBriefly(v, crimson, 1200);
            await Anim.Delay(120);
        }
        Shake(0.9);
        await ShowBannerAsync("INSURRECTION", "Together, they are unstoppable", crimson, 800);
    }

    /// <summary>A rotating tesseract (two nested squares joined at the corners) folds open over the board.</summary>
    private async Task TesseractAsync()
    {
        Sfx.Play(SfxKind.Portal, 0.8);
        var c = new Point(BoardLayout.Width / 2, BoardLayout.ControlLineY);
        var host = new Canvas { Width = 0, Height = 0 };
        var rot = new RotateTransform();
        var sc = new ScaleTransform(0.2, 0.2);
        var tg = new TransformGroup();
        tg.Children.Add(sc);
        tg.Children.Add(rot);
        host.RenderTransform = tg;
        host.Effect = new DropShadowEffect { Color = ArchonGlow, BlurRadius = 20, ShadowDepth = 0 };
        double outer = 110, inner = 55;
        Point[] Square(double s, double turn) => Enumerable.Range(0, 4)
            .Select(i => new Point(Math.Cos(turn + i * Math.PI / 2) * s, Math.Sin(turn + i * Math.PI / 2) * s)).ToArray();
        var o = Square(outer, Math.PI / 4);
        var n = Square(inner, Math.PI / 4 + 0.35);
        host.Children.Add(new Polygon { Points = new PointCollection(o), Stroke = Solid(ArchonGlow), StrokeThickness = 3 });
        host.Children.Add(new Polygon { Points = new PointCollection(n), Stroke = Solid(ArchonGold), StrokeThickness = 3 });
        for (int i = 0; i < 4; i++)
            host.Children.Add(new Line { X1 = o[i].X, Y1 = o[i].Y, X2 = n[i].X, Y2 = n[i].Y, Stroke = Solid(Colors.White, 200), StrokeThickness = 2 });
        AddFx(host, c.X, c.Y, 70);
        rot.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 270, Anim.Ms(1300)));
        _ = Anim.To(sc, ScaleTransform.ScaleXProperty, 1.6, 700, Anim.Back);
        await Anim.To(sc, ScaleTransform.ScaleYProperty, 1.6, 700, Anim.Back);
        await Anim.Delay(300);
        _ = Anim.To(sc, ScaleTransform.ScaleXProperty, 0, 300, Anim.In);
        await Anim.To(sc, ScaleTransform.ScaleYProperty, 0, 300, Anim.In);
        RemoveFx(host);
        SparkBurst(c, ArchonGlow, 16, 120, 6);
    }

    // ------------------------------------------------------------------ Galactic Siege, Open Source, Deify, The Unnameable

    /// <summary>A vast ship's shadow slides over the opponent's half; red sparks lock down their Order Nodes.</summary>
    private async Task SiegeShadowAsync(int controller)
    {
        Sfx.Play(SfxKind.ArchonQuake, 0.6);
        int opp = 1 - controller;
        double y = opp == Me ? 760 : 240;
        var shadow = new Ellipse
        {
            Width = 1500,
            Height = 420,
            Fill = new RadialGradientBrush(Color.FromArgb(200, 0, 0, 0), Color.FromArgb(0, 0, 0, 0)),
            Opacity = 0,
        };
        var t = new TranslateTransform(-900, 0);
        shadow.RenderTransform = t;
        AddFx(shadow, 50, y - 210, 55);
        _ = Anim.To(shadow, OpacityProperty, 1, 500, Anim.Out);
        await Anim.To(t, TranslateTransform.XProperty, 0, 1100, Anim.Out);
        var red = Color.FromRgb(255, 70, 70);
        foreach (var node in State.Players[opp].OrderNodes)
        {
            var at = BoardLayout.Center(node);
            SparkBurst(at, red, 12, 60, 6);
            Shockwave(at, red);
        }
        Sfx.Play(SfxKind.Lock, 0.8);
        await Anim.Delay(500);
        await FadeAndRemoveAsync(shadow, 700);
    }

    /// <summary>Columns of glowing data fall over the issuer's half of the board.</summary>
    private async Task DataRainAsync(int controller)
    {
        Sfx.Play(SfxKind.Tick, 0.6);
        double top = controller == Me ? 520 : 0;
        int columns = FxSettings.Count(22);
        var tasks = new List<Task>();
        for (int i = 0; i < columns; i++)
        {
            double x = Rand(200, 1150);
            var col = new StackPanel();
            int bits = 6 + _fxRng.Next(6);
            for (int b = 0; b < bits; b++)
                col.Children.Add(new Rectangle
                {
                    Width = 6,
                    Height = _fxRng.Next(2) == 0 ? 6 : 14,
                    Margin = new Thickness(0, 3, 0, 3),
                    Fill = Solid(b == bits - 1 ? Colors.White : ArchonGlow, (byte)(80 + 170 * b / bits)),
                });
            var t = new TranslateTransform(0, -150);
            col.RenderTransform = t;
            AddFx(col, x, top, 60);
            double delay = Rand(0, 600);
            tasks.Add(Anim.To(t, TranslateTransform.YProperty, 480, Rand(700, 1100), null, delayMs: delay));
            _ = FadeAndRemoveAsync(col, 300, delay + 900);
        }
        await Task.WhenAll(tasks);
    }

    /// <summary>A pillar of golden light descends on the Leader, leaving a halo.</summary>
    private async Task DivineLightAsync(Point at)
    {
        Sfx.Play(SfxKind.Fanfare, 0.7);
        var pillar = new Rectangle
        {
            Width = 180,
            Height = at.Y + 200,
            Fill = new LinearGradientBrush(Color.FromArgb(0, 255, 230, 160), Color.FromArgb(220, 255, 240, 200), 90),
            Effect = new BlurEffect { Radius = FxSettings.HighQuality ? 20 : 6 },
            Opacity = 0,
        };
        AddFx(pillar, at.X - 90, -200, 70);
        await Anim.To(pillar, OpacityProperty, 1, 350, Anim.Out);
        var halo = new Ellipse
        {
            Width = 150,
            Height = 46,
            Stroke = Solid(ArchonGold),
            StrokeThickness = 6,
            Effect = new DropShadowEffect { Color = ArchonGold, BlurRadius = 24, ShadowDepth = 0 },
        };
        AddFx(halo, at.X - 75, at.Y - BoardLayout.CardH / 2 - 40, 71);
        SparkBurst(at, ArchonGold, 18, 100, 6);
        Shockwave(at, ArchonGold);
        await Anim.Delay(500);
        _ = FadeAndRemoveAsync(pillar, 600);
        await FadeAndRemoveAsync(halo, 900, 300);
    }

    /// <summary>The board darkens, the ground shakes and a red glare opens where the Unnameable stands.</summary>
    private async Task UnnameableAsync(Point at)
    {
        Sfx.Play(SfxKind.ArchonQuake, 1);
        Sfx.Play(SfxKind.Thunder, 0.6);
        var dark = new Rectangle
        {
            Width = BoardLayout.Width,
            Height = BoardLayout.Height,
            Fill = new RadialGradientBrush(Color.FromArgb(40, 120, 0, 0), Color.FromArgb(230, 0, 0, 0))
            {
                Center = new Point(at.X / BoardLayout.Width, at.Y / BoardLayout.Height),
                GradientOrigin = new Point(at.X / BoardLayout.Width, at.Y / BoardLayout.Height),
                RadiusX = 0.35,
                RadiusY = 0.5,
            },
            Opacity = 0,
        };
        AddFx(dark, 0, 0, 58);
        await Anim.To(dark, OpacityProperty, FxSettings.Flash(1), 400, Anim.Out);
        Shake(2);
        CameraPush(1.06, 900);
        var glare = new Ellipse
        {
            Width = 260,
            Height = 260,
            Fill = new RadialGradientBrush(Color.FromArgb(220, 255, 40, 40), Color.FromArgb(0, 120, 0, 0)),
        };
        AddFx(glare, at.X - 130, at.Y - 130, 59);
        Shockwave(at, Color.FromRgb(200, 20, 20));
        await Anim.Delay(700);
        _ = FadeAndRemoveAsync(glare, 600);
        await FadeAndRemoveAsync(dark, 900);
    }
}
