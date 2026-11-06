using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CosmicCrusade.App.Board;
using CosmicCrusade.App.Controls;
using CosmicCrusade.Core.Engine;
using CosmicCrusade.Core.Model;

namespace CosmicCrusade.App.Views;

/// <summary>
/// Ability choices for the human (M3). The panel lists every option (mini card, node name, Yes/No, a number...).
/// Options that are on the board also light up there and can be clicked directly: Units glow gold, nodes pulse.
/// One-pick choices submit on click; multi-pick choices toggle and need CONFIRM (or Enter).
/// </summary>
public partial class GameView
{
    private static readonly Brush OptionBrush = new SolidColorBrush(Color.FromRgb(30, 36, 72));
    private static readonly Brush OptionSelectedBrush = new SolidColorBrush(Color.FromRgb(161, 98, 7));

    private readonly List<Button> _choiceButtons = new();
    private readonly HashSet<int> _choicePicked = new();
    private readonly Dictionary<CardInstance, int> _choiceByCard = new();
    private readonly Dictionary<Node, int> _choiceByNode = new();

    private ChoiceRequest? ActiveChoice => _human.CurrentChoice;

    private bool SinglePick(ChoiceRequest r) => r.Kind == ChoiceKind.YesNo || (r.Min == 1 && r.Max == 1);

    private void OnHumanChoiceRequested(ChoiceRequest r)
    {
        ClearChoiceVisuals();
        ClearSelection();
        HideMagnifier();

        ChoiceTitle.Text = r.Prompt;
        ChoiceCardText.Text = r.Source?.Definition.Text ?? "";
        ChoiceCardText.Visibility = string.IsNullOrEmpty(ChoiceCardText.Text) ? Visibility.Collapsed : Visibility.Visible;

        for (int i = 0; i < r.Options.Count; i++)
        {
            var option = r.Options[i];
            int index = i;
            var button = new Button
            {
                Style = (Style)FindResource("SmallButton"),
                Margin = new Thickness(4),
                Background = OptionBrush,
                Content = OptionContent(option),
                ToolTip = option.Label,
            };
            button.Click += (_, _) => ToggleChoice(index);
            ChoiceOptions.Children.Add(button);
            _choiceButtons.Add(button);

            // Mirror the option on the board / in the hand.
            if (option.Card is { } card)
            {
                _choiceByCard[card] = index;
                if (_boardCards.TryGetValue(card.InstanceId, out var bv)) bv.SetGlow(Gold, pulse: true);
                if (_handViews.FirstOrDefault(v => v.Card == card) is { } hv) hv.SetGlow(Gold, pulse: true);
            }
            if (option.Card is not null && option.Node is { } destination)
            {
                // A card going to a known node (Psychic Coercion): just show where; the card is the choice.
                if (_slots.TryGetValue(destination, out var hintSlot)) hintSlot.SetTarget(true);
            }
            else if (option.Node is { } node)
            {
                _choiceByNode[node] = index;
                if (_slots.TryGetValue(node, out var slot)) slot.SetTarget(true);
                if (node.Card is not null && _boardCards.TryGetValue(node.Card.InstanceId, out var nv)) nv.SetGlow(Gold, pulse: true);
            }
        }

        bool single = SinglePick(r);
        ChoiceConfirm.Visibility = single ? Visibility.Collapsed : Visibility.Visible;
        ChoiceCount.Visibility = single ? Visibility.Collapsed : Visibility.Visible;
        UpdateChoiceCount();

        ChoicePanel.Visibility = Visibility.Visible;
        Fixed(ChoicePanel, OpacityProperty, 0, 1, 140);
        _hud.Status = "Choose";
        _hud.Hint = single ? "Click an option (here or on the board)."
            : r.Min == 0 && r.Max == 1 ? "Click an option (here or on the board), or NONE."
            : "Select options, then CONFIRM (Enter).";
        _hud.CanPass = false;
    }

    private object OptionContent(ChoiceOption option)
    {
        if (option.Card is null) return option.Label;

        var panel = new StackPanel { Width = 96 };
        var mini = new CardView(option.Card, faceDown: false, 80) { HorizontalAlignment = HorizontalAlignment.Center, IsHitTestVisible = true };
        mini.MouseEnter += (_, _) => ShowZoom(option.Card);
        mini.MouseRightButtonDown += Card_MouseRightButtonDown;
        panel.Children.Add(mini);
        panel.Children.Add(new TextBlock
        {
            Text = option.Label,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 4, 0, 0),
        });
        return panel;
    }

    private void ToggleChoice(int index)
    {
        var r = ActiveChoice;
        if (r is null || index < 0 || index >= r.Options.Count) return;

        // One pick or none (Ace's "next node, or NONE"): a click submits it; NONE stays on the CONFIRM button.
        if (SinglePick(r) || (r.Min == 0 && r.Max == 1))
        {
            _human.SubmitChoice(new[] { index });
            return;
        }

        if (!_choicePicked.Remove(index))
        {
            if (_choicePicked.Count >= r.Max) return; // full: deselect something first
            _choicePicked.Add(index);
        }
        _choiceButtons[index].Background = _choicePicked.Contains(index) ? OptionSelectedBrush : OptionBrush;
        UpdateChoiceCount();
    }

    private void UpdateChoiceCount()
    {
        var r = ActiveChoice;
        if (r is null) return;
        string range = r.Min == r.Max ? $"{r.Max}" : r.Max >= r.Options.Count ? $"{r.Min}+" : $"{r.Min}–{r.Max}";
        ChoiceCount.Text = $"Selected {_choicePicked.Count} (choose {range})";
        ChoiceConfirm.IsEnabled = _choicePicked.Count >= r.Min && _choicePicked.Count <= r.Max;
        ChoiceConfirm.Content = _choicePicked.Count == 0 && r.Min == 0 ? "NONE" : "CONFIRM";
    }

    private void ChoiceConfirm_Click(object sender, RoutedEventArgs e) => ConfirmChoice();

    private void ConfirmChoice()
    {
        var r = ActiveChoice;
        if (r is null || SinglePick(r)) return;
        _human.SubmitChoice(_choicePicked.OrderBy(i => i).ToList());
    }

    private void OnHumanChoiceCompleted()
    {
        ClearChoiceVisuals();
        ChoicePanel.Visibility = Visibility.Collapsed;
        _hud.Status = "";
        _hud.Hint = "";
    }

    private void ClearChoiceVisuals()
    {
        foreach (var card in _choiceByCard.Keys)
        {
            if (_boardCards.TryGetValue(card.InstanceId, out var bv)) bv.SetGlow(null);
            if (_handViews.FirstOrDefault(v => v.Card == card) is { } hv) hv.SetGlow(null);
        }
        foreach (var node in _choiceByNode.Keys)
        {
            if (_slots.TryGetValue(node, out var slot)) slot.SetTarget(false);
            if (node.Card is not null && _boardCards.TryGetValue(node.Card.InstanceId, out var nv)) nv.SetGlow(null);
        }
        foreach (var slot in _slots.Values) if (slot.IsTarget) slot.SetTarget(false); // incl. "where it goes" hints
        _choiceByCard.Clear();
        _choiceByNode.Clear();
        _choicePicked.Clear();
        _choiceButtons.Clear();
        ChoiceOptions.Children.Clear();
    }

    /// <summary>Board/hand clicks while a choice is open. Returns true when the click was used.</summary>
    private bool TryChoiceClick(CardInstance? card, Node? node)
    {
        if (ActiveChoice is null) return false;
        if (card is not null && _choiceByCard.TryGetValue(card, out int ci))
        {
            ToggleChoice(ci);
            return true;
        }
        if (node is not null && _choiceByNode.TryGetValue(node, out int ni))
        {
            ToggleChoice(ni);
            return true;
        }
        // A Unit sitting on a selectable node.
        if (card?.Location.Node is { } on && _choiceByNode.TryGetValue(on, out int oi))
        {
            ToggleChoice(oi);
            return true;
        }
        return true; // swallow other clicks while choosing
    }
}
