using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CosmicCrusade.Core.Model;

namespace CosmicCrusade.App.Rendering;

/// <summary>
/// Draws card faces in code from card-layout.json + art + strips (algorithm from tools/render_card.py),
/// so text, rank and race can change live later. Results are cached and frozen.
/// Canvas is 780×1075; everything is drawn in that space and scaled by the caller.
/// </summary>
public static class CardFaceFactory
{
    public const double CardWidth = 780;
    public const double CardHeight = 1075;
    public const double CornerRadius = 34;

    private static readonly Dictionary<string, Drawing> _vector = new();
    private static readonly Dictionary<string, BitmapSource> _bitmaps = new();
    private static readonly Dictionary<string, BitmapSource> _images = new();
    private static Drawing? _back;
    private static BitmapSource? _backBitmap;

    private static readonly FontFamily DisplayFont = new("Haettenschweiler, Impact, Arial Narrow");
    private static readonly FontFamily FlavorFont = new("Calibri, Segoe UI");
    private static readonly Typeface Display = new(DisplayFont, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    private static readonly Typeface FlavorFace = new(FlavorFont, FontStyles.Italic, FontWeights.Normal, FontStretches.Normal);

    private static readonly Brush Grey = Frozen(new SolidColorBrush(CardLayout.Hex("#4B4B4B")));
    private static readonly Brush FlavorGrey = Frozen(new SolidColorBrush(CardLayout.Hex("#7E7E7E")));

    /// <summary>Card aspect ratio (width / height).</summary>
    public static double Aspect => CardWidth / CardHeight;

    /// <summary>Crisp vector face, for the large zoom view.</summary>
    public static ImageSource GetVectorFace(CardDefinition def)
    {
        var img = new DrawingImage(GetDrawing(def));
        img.Freeze();
        return img;
    }

    /// <summary>Rasterised face (half resolution, 390×538), cheap to draw many times on the board and in hands.</summary>
    public static BitmapSource GetFace(CardDefinition def)
    {
        if (_bitmaps.TryGetValue(def.Id, out var bmp)) return bmp;
        bmp = Rasterize(GetDrawing(def), 0.5);
        _bitmaps[def.Id] = bmp;
        return bmp;
    }

    public static BitmapSource GetBack()
    {
        return _backBitmap ??= Rasterize(GetBackDrawing(), 0.5);
    }

    public static ImageSource GetVectorBack()
    {
        var img = new DrawingImage(GetBackDrawing());
        img.Freeze();
        return img;
    }

    // =========================================================================== face

    private static Drawing GetDrawing(CardDefinition def)
    {
        if (_vector.TryGetValue(def.Id, out var d)) return d;
        d = BuildFace(def);
        _vector[def.Id] = d;
        return d;
    }

    private static Drawing BuildFace(CardDefinition def)
    {
        var layout = CardLayout.Instance;
        var group = new DrawingGroup();
        using (var dc = group.Open())
        {
            dc.PushClip(new RectangleGeometry(new Rect(0, 0, CardWidth, CardHeight), CornerRadius, CornerRadius));
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, CardWidth, CardHeight));

            string raceKey = def.Races.Count > 0 ? def.Races[0].ToString() : "Neutral";
            var own = Frozen(new SolidColorBrush(layout.Style(raceKey).TextColor));

            // 1. Race strip
            string stripStyle = def.Kind == CardKind.Warzone ? "Warzone" : raceKey;
            var strip = LoadImage(AppPaths.Strip(stripStyle));
            if (strip is not null) dc.DrawImage(strip, new Rect(0, 0, 190, CardHeight));
            else dc.DrawRectangle(Frozen(new SolidColorBrush(layout.Style(stripStyle).StripColor)), null, new Rect(0, 0, 190, CardHeight));

            // 2. Art, clipped with a rounded top-right corner
            var artRect = new Rect(200, 77, 503, 441);
            var art = LoadImage(AppPaths.Art(def.Id));
            if (art is not null)
            {
                dc.PushClip(TopRightRounded(artRect, 56));
                dc.DrawImage(art, artRect);
                dc.Pop();
            }

            // 3. Name, rotated to read bottom-to-top
            DrawName(dc, def.Name);

            // 4. Header rows
            DrawHeader(dc, def, own);

            // 5. Ability + flavour
            DrawRulesText(dc, def, own);

            dc.Pop();
        }
        group.Freeze();
        return group;
    }

    private static void DrawName(DrawingContext dc, string name)
    {
        double size = 92;
        FormattedText ft = Text(name, Display, size, Brushes.White);
        while (ft.Width > 700 && size > 40)
        {
            size -= 2;
            ft = Text(name, Display, size, Brushes.White);
        }

        var geometry = ft.BuildGeometry(new Point(-ft.Width / 2, -ft.Height / 2));

        // Shadow: offset down/right in card space, so translate before rotating.
        dc.PushTransform(new TranslateTransform(112 + 5, 540 + 5));
        dc.PushTransform(new RotateTransform(-90));
        dc.DrawGeometry(Brushes.Black, null, geometry);
        dc.Pop();
        dc.Pop();

        dc.PushTransform(new TranslateTransform(112, 540));
        dc.PushTransform(new RotateTransform(-90));
        dc.DrawGeometry(Brushes.White, new Pen(Brushes.Black, 3) { LineJoin = PenLineJoin.Round }, geometry);
        dc.Pop();
        dc.Pop();
    }

    private static void DrawHeader(DrawingContext dc, CardDefinition def, Brush own)
    {
        var layout = CardLayout.Instance;
        string races = def.Races.Count == 0 ? "Neutral" : string.Join(" / ", def.Races.Select(r => layout.Style(r).Display));

        (string Label, (string Text, Brush Brush)[] Parts)[] rows = def.Kind switch
        {
            CardKind.Unit => new[]
            {
                ("Race:", new[] { (races, own) }),
                ("Rank:", new[]
                {
                    (def.BaseRankIsX ? "X" : (def.BaseRank ?? 0).ToString(CultureInfo.InvariantCulture), own),
                    ("  (" + (def.IsLeader ? "*Leader*" : def.UnitClass?.ToString() ?? "") + ")", Grey),
                }),
            },
            CardKind.Order => new[]
            {
                ("Race:", new[] { (races, own) }),
                ("Type:", new[] { ("Order", own), ("  (" + def.OrderType + ")", Grey) }),
            },
            _ => new[]
            {
                ("Type:", new[] { ("Warzone", (Brush)Brushes.Black) }),
                ("Race:", new[] { (races, own) }),
            },
        };

        double[] tops = { 545, 616 };
        for (int i = 0; i < rows.Length; i++)
        {
            double y = tops[i] - 8;
            dc.DrawText(Text(rows[i].Label, Display, 58, Brushes.Black), new Point(201, y));

            string value = string.Concat(rows[i].Parts.Select(p => p.Text));
            var ft = Text(value, Display, 58, Brushes.Black);
            int start = 0;
            foreach (var part in rows[i].Parts)
            {
                if (part.Text.Length > 0) ft.SetForegroundBrush(part.Brush, start, part.Text.Length);
                start += part.Text.Length;
            }
            dc.DrawText(ft, new Point(372, y));
        }
    }

    private static readonly Regex NumberPattern = new(@"[+\-−]?\d+|[+\-]X\b|\bX\b", RegexOptions.Compiled);

    private static void DrawRulesText(DrawingContext dc, CardDefinition def, Brush own)
    {
        const double top = 706, x = 200, width = 560, bottomLimit = 1050;
        bool hasText = !string.IsNullOrWhiteSpace(def.Text);
        bool hasFlavor = !string.IsNullOrWhiteSpace(def.Flavor);

        double size = 50;
        double flavorSize = 30;
        FormattedText? ability = null;
        FormattedText? flavor = null;

        while (true)
        {
            ability = hasText ? AbilityText(def.Text, size, own) : null;
            flavor = hasFlavor ? FlavorText(def.Flavor, flavorSize) : null;
            double need = top + (ability?.Height ?? 0) + (flavor is null ? 0 : 40 + flavor.Height);
            if (need <= bottomLimit) break;
            if (size > 38) size -= 1;
            else if (flavorSize > 22) flavorSize -= 1;
            else break;
        }

        double abilityBottom = top;
        if (ability is not null)
        {
            dc.DrawText(ability, new Point(x, top - 6));
            abilityBottom = top - 6 + ability.Height;
        }

        if (flavor is not null)
        {
            double flavorTop = Math.Max(hasText ? abilityBottom + 40 : 780, 1010 - flavor.Height);
            dc.DrawText(flavor, new Point(x, flavorTop));
        }
    }

    private static FormattedText AbilityText(string text, double size, Brush own)
    {
        var ft = Text(text, Display, size, Brushes.Black);
        ft.MaxTextWidth = 560;
        ft.LineHeight = 64 * size / 50;

        // Race words in their race colour (longest names first so "Galactic Council" wins over partial matches).
        foreach (Race race in Enum.GetValues<Race>())
        {
            var style = CardLayout.Instance.Style(race);
            if (race == Race.Neutral) continue;
            var brush = Frozen(new SolidColorBrush(style.TextColor));
            foreach (var word in new[] { style.Display, style.Display + "s" })
            {
                int idx = 0;
                while ((idx = text.IndexOf(word, idx, StringComparison.Ordinal)) >= 0)
                {
                    ft.SetForegroundBrush(brush, idx, word.Length);
                    idx += word.Length;
                }
            }
        }

        foreach (Match m in NumberPattern.Matches(text))
            ft.SetForegroundBrush(own, m.Index, m.Length);

        return ft;
    }

    private static FormattedText FlavorText(string text, double size)
    {
        var ft = Text(text, FlavorFace, size, FlavorGrey);
        ft.MaxTextWidth = 470;
        ft.LineHeight = 37 * size / 30;
        return ft;
    }

    // =========================================================================== back

    private static Drawing GetBackDrawing()
    {
        if (_back is not null) return _back;

        var group = new DrawingGroup();
        using (var dc = group.Open())
        {
            var rect = new Rect(0, 0, CardWidth, CardHeight);
            dc.PushClip(new RectangleGeometry(rect, CornerRadius, CornerRadius));

            var bg = new RadialGradientBrush(CardLayout.Hex("#3A1C78"), CardLayout.Hex("#070418"))
            {
                GradientOrigin = new Point(0.5, 0.45),
                Center = new Point(0.5, 0.45),
                RadiusX = 0.8,
                RadiusY = 0.7,
            };
            dc.DrawRectangle(Frozen(bg), null, rect);

            // Deterministic starfield.
            var rng = new Random(1977);
            for (int i = 0; i < 160; i++)
            {
                double r = rng.NextDouble() * 2.6 + 0.6;
                byte a = (byte)rng.Next(90, 255);
                dc.DrawEllipse(Frozen(new SolidColorBrush(Color.FromArgb(a, 255, 255, 255))), null,
                    new Point(rng.NextDouble() * CardWidth, rng.NextDouble() * CardHeight), r, r);
            }

            // Orbit rings.
            var ringPen = new Pen(Frozen(new SolidColorBrush(Color.FromArgb(150, 255, 196, 64))), 6);
            dc.DrawEllipse(null, ringPen, new Point(390, 537), 250, 250);
            dc.DrawEllipse(null, new Pen(Frozen(new SolidColorBrush(Color.FromArgb(90, 120, 200, 255))), 3), new Point(390, 537), 300, 120);
            dc.DrawEllipse(Frozen(new RadialGradientBrush(CardLayout.Hex("#FFD27A"), CardLayout.Hex("#B4530A"))), null, new Point(390, 537), 70, 70);

            DrawCentered(dc, "COSMIC", 140, 360);
            DrawCentered(dc, "CRUSADE", 140, 714);

            dc.Pop();
            dc.DrawRoundedRectangle(null, new Pen(Frozen(new SolidColorBrush(CardLayout.Hex("#D4A23A"))), 18),
                new Rect(9, 9, CardWidth - 18, CardHeight - 18), CornerRadius - 6, CornerRadius - 6);
        }
        group.Freeze();
        _back = group;
        return group;
    }

    private static void DrawCentered(DrawingContext dc, string text, double size, double centerY)
    {
        var ft = Text(text, Display, size, Brushes.White);
        var geo = ft.BuildGeometry(new Point(CardWidth / 2 - ft.Width / 2, centerY - ft.Height / 2));
        dc.DrawGeometry(Brushes.White, new Pen(Frozen(new SolidColorBrush(CardLayout.Hex("#2B0F5C"))), 5), geo);
    }

    // =========================================================================== helpers

    private static FormattedText Text(string text, Typeface face, double size, Brush brush) =>
        new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, size, brush, 1.0);

    private static Geometry TopRightRounded(Rect r, double radius)
    {
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(r.TopLeft, true, true);
            ctx.LineTo(new Point(r.Right - radius, r.Top), false, false);
            ctx.ArcTo(new Point(r.Right, r.Top + radius), new Size(radius, radius), 0, false, SweepDirection.Clockwise, false, false);
            ctx.LineTo(r.BottomRight, false, false);
            ctx.LineTo(r.BottomLeft, false, false);
        }
        g.Freeze();
        return g;
    }

    /// <summary>Loads (and caches) an image file; null when missing.</summary>
    public static BitmapSource? LoadImage(string path)
    {
        if (_images.TryGetValue(path, out var cached)) return cached;
        if (!File.Exists(path)) return null;

        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.UriSource = new Uri(path, UriKind.Absolute);
        bmp.EndInit();
        bmp.Freeze();
        _images[path] = bmp;
        return bmp;
    }

    private static BitmapSource Rasterize(Drawing drawing, double scale)
    {
        int w = (int)Math.Round(CardWidth * scale);
        int h = (int)Math.Round(CardHeight * scale);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.PushTransform(new ScaleTransform(scale, scale));
            dc.DrawDrawing(drawing);
            dc.Pop();
        }
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        rtb.Freeze();
        return rtb;
    }

    private static T Frozen<T>(T f) where T : Freezable
    {
        f.Freeze();
        return f;
    }
}
