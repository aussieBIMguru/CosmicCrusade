using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using CosmicCrusade.App.Rendering;
using CosmicCrusade.Core.Model;
using CosmicCrusade.Core.Rules;

namespace CosmicCrusade.App.ViewModels;

/// <summary>One editable row in the card-pool table.</summary>
public sealed class CardCountRow : ObservableObject
{
    private int _copies;
    private readonly Action _changed;

    public CardCountRow(CardDefinition def, int copies, Action changed)
    {
        Definition = def;
        _copies = copies;
        _changed = changed;
    }

    public CardDefinition Definition { get; }
    public string Id => Definition.Id;
    public string Name => Definition.Name;
    public string Race => string.Join(" / ", Definition.Races.Select(r => CardLayout.Instance.Style(r).Display));
    public string Type => Definition.Kind switch
    {
        CardKind.Unit => $"{Definition.UnitClass} ({(Definition.BaseRankIsX ? "X" : Definition.BaseRank?.ToString())})",
        CardKind.Order => $"{Definition.OrderType} Order",
        _ => "Warzone",
    };
    public string SetName => Definition.Set;

    /// <summary>Implementation watermark: empty box = not implemented yet, half-filled = partly implemented.</summary>
    public Implementation Status => CardStatus.Of(Definition);
    public bool IsFullyImplemented => Status == Implementation.Full;
    public bool IsPartlyImplemented => Status == Implementation.Partial;
    public int StatusOrder => (int)Status;
    public string? StatusTip => IsFullyImplemented ? null : CardStatus.Describe(Definition);

    public int Copies
    {
        get => _copies;
        set
        {
            if (Set(ref _copies, Math.Clamp(value, 0, 20))) _changed();
        }
    }
}

public sealed class SetupViewModel : ObservableObject
{
    private readonly GameOptions _defaults;
    private int _roundsToWin;
    private DeckMode _deckMode;
    private EmptyDeckRule _emptyDeck;
    private bool _leaders;
    private bool _warzones;
    private string _seed = "";
    private string _filter = "";
    private CardCountRow? _selectedCard;
    private string _totals = "";

    public SetupViewModel()
    {
        _defaults = GameOptions.LoadDefaults(AppPaths.DataDir);
        _roundsToWin = _defaults.RoundsToWin;
        _deckMode = _defaults.DeckMode;
        _emptyDeck = _defaults.EmptyDeck;
        _leaders = _defaults.Leaders;
        _warzones = _defaults.Warzones;

        // Every card, Warzones included (their counts only matter while the Warzones expansion is on).
        foreach (var def in AppPaths.Database.All.OrderBy(d => d.Id, StringComparer.Ordinal))
        {
            _defaults.CopyCounts.TryGetValue(def.Id, out int n);
            Cards.Add(new CardCountRow(def, n, UpdateTotals));
        }

        CardsView = CollectionViewSource.GetDefaultView(Cards);
        CardsView.Filter = o => o is CardCountRow r
            && (string.IsNullOrWhiteSpace(Filter)
                || r.Name.Contains(Filter, StringComparison.OrdinalIgnoreCase)
                || r.Race.Contains(Filter, StringComparison.OrdinalIgnoreCase)
                || r.Type.Contains(Filter, StringComparison.OrdinalIgnoreCase)
                || r.Id.Contains(Filter, StringComparison.OrdinalIgnoreCase));

        ResetCountsCommand = new RelayCommand(_ => ResetCounts());
        SelectedCard = Cards.FirstOrDefault();
        UpdateTotals();
    }

    public ObservableCollection<CardCountRow> Cards { get; } = new();
    public ICollectionView CardsView { get; }
    public RelayCommand ResetCountsCommand { get; }

    public IReadOnlyList<DeckMode> DeckModes { get; } = Enum.GetValues<DeckMode>();
    public IReadOnlyList<EmptyDeckRule> EmptyDeckRules { get; } = Enum.GetValues<EmptyDeckRule>();

    public int RoundsToWin { get => _roundsToWin; set => Set(ref _roundsToWin, Math.Clamp(value, 1, 9)); }
    public DeckMode DeckMode { get => _deckMode; set { if (Set(ref _deckMode, value)) UpdateTotals(); } }
    public EmptyDeckRule EmptyDeck { get => _emptyDeck; set => Set(ref _emptyDeck, value); }
    public bool Leaders { get => _leaders; set { if (Set(ref _leaders, value)) UpdateTotals(); } }
    public bool Warzones { get => _warzones; set { if (Set(ref _warzones, value)) UpdateTotals(); } }

    /// <summary>Blank = random. Any integer replays the same shuffle.</summary>
    public string Seed { get => _seed; set => Set(ref _seed, value); }

    public string Filter
    {
        get => _filter;
        set { if (Set(ref _filter, value)) CardsView.Refresh(); }
    }

    public CardCountRow? SelectedCard { get => _selectedCard; set => Set(ref _selectedCard, value); }

    public string Totals { get => _totals; private set => Set(ref _totals, value); }

    public GameOptions BuildOptions()
    {
        var o = _defaults.Clone();
        o.RoundsToWin = RoundsToWin;
        o.DeckMode = DeckMode;
        o.EmptyDeck = EmptyDeck;
        o.Leaders = Leaders;
        o.Warzones = Warzones;
        o.Seed = int.TryParse(Seed, out int s) ? s : Environment.TickCount;
        foreach (var row in Cards) o.CopyCounts[row.Id] = row.Copies;
        return o;
    }

    private void ResetCounts()
    {
        foreach (var row in Cards)
        {
            _defaults.CopyCounts.TryGetValue(row.Id, out int n);
            row.Copies = n;
        }
    }

    private void UpdateTotals()
    {
        if (Cards.Count == 0) return;
        var o = BuildOptions();
        int units = Cards.Where(r => r.Definition.IsUnit).Sum(r => DeckBuilder.CopiesOf(r.Definition, o));
        int orders = Cards.Where(r => r.Definition.IsOrder).Sum(r => DeckBuilder.CopiesOf(r.Definition, o));
        int warzones = Cards.Where(r => r.Definition.Kind == CardKind.Warzone).Sum(r => DeckBuilder.CopiesOf(r.Definition, o));
        string wz = Warzones ? $" · Warzone deck {warzones}" : " · Warzones off";
        Totals = (DeckMode == DeckMode.RandomDraw
            ? $"Random Draw: {Cards.Count(r => r.Definition.IsUnit && DeckBuilder.CopiesOf(r.Definition, o) > 0)} distinct units, " +
              $"{Cards.Count(r => r.Definition.IsOrder && DeckBuilder.CopiesOf(r.Definition, o) > 0)} distinct orders (decks never run out)"
            : $"Unit deck {units} · Order deck {orders}") + wz;
    }
}
