using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using CosmicCrusade.App.Board;
using CosmicCrusade.App.Controls;
using CosmicCrusade.App.Rendering;
using CosmicCrusade.Core.Model;

namespace CosmicCrusade.App.Views;

/// <summary>
/// The pile viewer: left-click a face-up pile (Retired, Nullified, Issued, Active Warzone) to see every card in it,
/// newest first. Two layouts: the cards laid out in order ("card view"), or a compact list. Hover a card or row to
/// preview it big on the right (and in the side panel); right-click to enlarge it. Click outside, CLOSE or Esc closes.
/// The viewer only reads the state: it stays open (and refreshes) while the game carries on.
/// </summary>
public partial class GameView
{
    private const double PileCardW = 118;
    private const double PilePreviewW = 280;

    private PileKind? _viewedPile;
    private static bool _pileListMode; // remembered between games in this run

    private bool IsPileViewerOpen => PileViewerOverlay.Visibility == Visibility.Visible;

    /// <summary>Piles anyone may look through (decks and the Archon deck are face down).</summary>
    private static bool CanBrowse(PileKind pile) =>
        pile is PileKind.RetiredUnits or PileKind.NullifiedUnits or PileKind.IssuedOrders or PileKind.ActiveWarzone;

    private void OpenPileViewer(PileKind pile)
    {
        HideMagnifier();
        CloseInspect();
        _viewedPile = pile;

        // Fit the panel into the play area.
        PileViewerPanel.MaxWidth = Math.Max(420, PlayArea.ActualWidth - 60);
        PileViewerPanel.MaxHeight = Math.Max(320, PlayArea.ActualHeight - 60);

        FillPileViewer(scrollToTop: true);
        PileViewerOverlay.Visibility = Visibility.Visible;
        Fixed(PileViewerOverlay, OpacityProperty, 0, 1, 140);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        Fixed(PileViewerScale, ScaleTransform.ScaleXProperty, 0.94, 1, 180, ease);
        Fixed(PileViewerScale, ScaleTransform.ScaleYProperty, 0.94, 1, 180, ease);
        Sfx.Play(SfxKind.Riffle, 0.35, minGapMs: 200);
    }

    private void ClosePileViewer()
    {
        if (!IsPileViewerOpen) return;
        _viewedPile = null;
        PileViewerOverlay.BeginAnimation(OpacityProperty, null);
        PileViewerOverlay.Visibility = Visibility.Collapsed;
        PileViewerContent.Content = null;
        PileViewerPreview.Child = null;
    }

    /// <summary>Called from UpdatePiles: keeps an open viewer in step with the game.</summary>
    private void RefreshPileViewer()
    {
        if (IsPileViewerOpen && _viewedPile is not null) FillPileViewer(scrollToTop: false);
    }

    private void FillPileViewer(bool scrollToTop)
    {
        if (_viewedPile is not { } pile || _state is null) return;

        // The pile's last card is its top: show newest first, numbered from the top.
        var cards = Enumerable.Reverse(State.GetPile(pile)).ToList();

        PileViewerTitle.Text = BoardLayout.PileLabel(pile);
        PileViewerSubtitle.Text = cards.Count switch
        {
            0 => "Empty.",
            1 => "1 card. Right-click it to enlarge.",
            _ => $"{cards.Count} cards, newest first (1 = top of the pile). Hover to preview, right-click to enlarge.",
        };
        PileViewerModeButton.Content = _pileListMode ? "CARD VIEW" : "LIST VIEW";
        PileViewerModeButton.Visibility = cards.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        if (cards.Count == 0)
        {
            PileViewerContent.Content = new TextBlock
            {
                Text = "Nothing here yet.",
                Style = (Style)FindResource("Muted"),
                FontSize = 16,
                Margin = new Thickness(20),
            };
            PileViewerPreview.Child = null;
            PileViewerPreview.Visibility = Visibility.Collapsed;
            return;
        }

        PileViewerContent.Content = _pileListMode ? BuildPileList(cards) : BuildPileSpread(cards);
        PileViewerPreview.Visibility = Visibility.Visible;

        // Keep the previewed card if it's still there; otherwise preview the top card.
        var previewed = (PileViewerPreview.Child as CardView)?.Card;
        ShowPilePreview(previewed is not null && cards.Contains(previewed) ? previewed : cards[0], force: true);
        if (scrollToTop) PileViewerScroll.ScrollToTop();
    }

    // ------------------------------------------------------------------ card view

    private UIElement BuildPileSpread(IReadOnlyList<CardInstance> cards)
    {
        var wrap = new WrapPanel { Margin = new Thickness(2) };
        for (int i = 0; i < cards.Count; i++)
        {
            var card = cards[i];
            var view = new CardView(card, faceDown: false, PileCardW) { Cursor = Cursors.Help };
            view.MouseRightButtonDown += Card_MouseRightButtonDown;
            var accent = CardLayout.Instance.Accent(card.Definition);
            view.MouseEnter += (_, _) =>
            {
                view.SetGlow(accent);
                ShowPilePreview(card);
                Sfx.Play(SfxKind.Hover, 0.4, minGapMs: 40);
            };
            view.MouseLeave += (_, _) => view.SetGlow(null);

            var cell = new Grid { Margin = new Thickness(6) };
            cell.Children.Add(view);
            cell.Children.Add(PositionBadge(i + 1, i == 0));
            wrap.Children.Add(cell);
        }
        return wrap;
    }

    /// <summary>The little "1", "2"... in a card's corner (1 = the top card, highlighted).</summary>
    private static Border PositionBadge(int position, bool top) => new()
    {
        HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Top,
        Margin = new Thickness(-4, -4, 0, 0),
        MinWidth = 24,
        Height = 24,
        CornerRadius = new CornerRadius(12),
        Background = new SolidColorBrush(top ? Color.FromRgb(255, 201, 74) : Color.FromRgb(20, 26, 60)),
        BorderBrush = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)),
        BorderThickness = new Thickness(1.5),
        IsHitTestVisible = false,
        ToolTip = top ? "Top of the pile" : null,
        Child = new TextBlock
        {
            Text = position.ToString(),
            FontWeight = FontWeights.Bold,
            FontSize = 12,
            Foreground = top ? Brushes.Black : Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 6, 0),
        },
    };

    // ------------------------------------------------------------------ list view

    private UIElement BuildPileList(IReadOnlyList<CardInstance> cards)
    {
        var list = new StackPanel { MinWidth = 420 };
        for (int i = 0; i < cards.Count; i++)
        {
            var card = cards[i];
            var def = card.Definition;
            var accent = CardLayout.Instance.Accent(def);
            var idle = new SolidColorBrush(Color.FromArgb((byte)(i % 2 == 0 ? 40 : 18), 160, 170, 255));
            var hover = new SolidColorBrush(Color.FromArgb(70, accent.R, accent.G, accent.B));

            var row = new Grid { Background = idle, Cursor = Cursors.Help, Tag = card };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var number = new TextBlock
            {
                Text = (i + 1).ToString(),
                FontWeight = i == 0 ? FontWeights.Bold : FontWeights.Normal,
                Foreground = i == 0 ? (Brush)FindResource("GoldBrush") : new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var bar = new Border { Background = new SolidColorBrush(accent), Margin = new Thickness(0, 4, 0, 4), CornerRadius = new CornerRadius(2) };
            Grid.SetColumn(bar, 1);

            var text = new StackPanel { Margin = new Thickness(10, 5, 10, 5) };
            text.Children.Add(new TextBlock { Text = def.Name, FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White });
            text.Children.Add(new TextBlock { Text = Describe(def), FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(185, 198, 240)) });
            Grid.SetColumn(text, 2);

            var rank = new TextBlock
            {
                Text = def.IsUnit ? (def.BaseRankIsX ? "X" : def.BaseRank?.ToString() ?? "") : "",
                FontFamily = (FontFamily)FindResource("DisplayFont"),
                FontSize = 26,
                Foreground = new SolidColorBrush(accent),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 12, 0),
                ToolTip = def.IsUnit ? "Printed Rank" : null,
            };
            Grid.SetColumn(rank, 3);

            row.Children.Add(number);
            row.Children.Add(bar);
            row.Children.Add(text);
            row.Children.Add(rank);

            row.MouseEnter += (_, _) =>
            {
                row.Background = hover;
                ShowPilePreview(card);
            };
            row.MouseLeave += (_, _) => row.Background = idle;
            row.MouseRightButtonDown += (_, e) =>
            {
                e.Handled = true;
                OpenInspect(card);
            };
            list.Children.Add(row);
        }
        return list;
    }

    /// <summary>"Space Cowboy · Champion · Rank 4", "Necrolite · Action Order", "Warzone".</summary>
    private static string Describe(CardDefinition def)
    {
        var parts = new List<string>();
        string races = string.Join(" / ", def.Races.Select(r => CardLayout.Instance.Style(r).Display));
        if (races.Length > 0) parts.Add(races);
        switch (def.Kind)
        {
            case CardKind.Unit:
                string cls = def.IsSpacecraft
                    ? (def.UnitClass is UnitClass.Craft ? "Spacecraft" : $"{def.UnitClass} spacecraft")
                    : def.UnitClass?.ToString() ?? "Unit";
                parts.Add(cls);
                break;
            case CardKind.Order:
                parts.Add($"{def.OrderType} Order");
                break;
            default:
                parts.Add("Warzone");
                break;
        }
        return string.Join(" · ", parts);
    }

    // ------------------------------------------------------------------ preview

    private void ShowPilePreview(CardInstance card, bool force = false)
    {
        if (!force && (PileViewerPreview.Child as CardView)?.Card == card) return;
        var view = new CardView(card, faceDown: false, PilePreviewW, vector: true) { IsHitTestVisible = false };
        view.SetGlow(CardLayout.Instance.Accent(card.Definition));
        PileViewerPreview.Child = view;
        ShowZoom(card);
    }

    // ------------------------------------------------------------------ input

    private void PileViewerOverlay_MouseDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true; // never reaches the board underneath
        if (e.ChangedButton == MouseButton.Left) ClosePileViewer();
    }

    /// <summary>Clicks inside the panel don't close it.</summary>
    private void PileViewerPanel_MouseDown(object sender, MouseButtonEventArgs e) => e.Handled = true;

    private void PileViewerClose_Click(object sender, RoutedEventArgs e) => ClosePileViewer();

    private void PileViewerMode_Click(object sender, RoutedEventArgs e)
    {
        _pileListMode = !_pileListMode;
        FillPileViewer(scrollToTop: true);
    }
}
