using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using CosmicCrusade.App.Rendering;
using CosmicCrusade.Core.Ai;
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
        CardKind.Unit => $"{ClassName} ({(Definition.BaseRankIsX ? "X" : Definition.BaseRank?.ToString())})",
        CardKind.Order => $"{Definition.OrderType} Order",
        _ => "Warzone",
    };
    public string SetName => Definition.Set;

    /// <summary>Spacecraft print as "Spacecraft" / "Leader spacecraft" / "Champion spacecraft".</summary>
    private string ClassName => Definition.IsSpacecraft
        ? (Definition.UnitClass is UnitClass.Craft ? "Spacecraft" : $"{Definition.UnitClass} spacecraft")
        : Definition.UnitClass?.ToString() ?? "";

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
    private AiDifficulty _aiDifficulty;
    private bool _archons;
    private bool _spacecraft;
    private string _seed = "";
    private string _filter = "";
    private CardCountRow? _selectedCard;
    private string _totals = "";
    private string _playerName = AppSettings.Current.PlayerName;
    private string _serverAddress = AppSettings.Current.ServerAddress;
    private string _joinCode = "";
    private string _onlineStatus = "";
    private string _hostCode = "";
    private bool _onlineBusy;
    private double _turnMinutes = Math.Clamp(AppSettings.Current.TurnMinutes, 0, MaxTurnMinutes);

    public const double MaxTurnMinutes = 10;

    public SetupViewModel()
    {
        _defaults = GameOptions.LoadDefaults(AppPaths.DataDir);
        _roundsToWin = _defaults.RoundsToWin;
        _deckMode = _defaults.DeckMode;
        _emptyDeck = _defaults.EmptyDeck;
        _leaders = _defaults.Leaders;
        _warzones = _defaults.Warzones;
        _aiDifficulty = _defaults.AiDifficulty;
        _archons = _defaults.Archons;
        _spacecraft = _defaults.Spacecraft;

        // Every card, Warzones included (their counts only matter while the Warzones expansion is on).
        foreach (var def in AppPaths.Database.All.OrderBy(d => d.Id, StringComparer.Ordinal))
        {
            if (def.IsToken) continue; // the Mothership's Fighter: never in a pool
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
    public IReadOnlyList<AiDifficulty> AiDifficulties { get; } = Enum.GetValues<AiDifficulty>();

    public AiDifficulty AiDifficulty { get => _aiDifficulty; set => Set(ref _aiDifficulty, value); }

    /// <summary>Turn clock, minutes per turn in half-minute steps (0 = off). Saved between runs.</summary>
    public double TurnMinutes
    {
        get => _turnMinutes;
        set
        {
            if (Set(ref _turnMinutes, Math.Clamp(Math.Round(value * 2) / 2, 0, MaxTurnMinutes))) Raise(nameof(TurnClockDisplay));
        }
    }

    /// <summary>"Off", "2 min", "1.5 min".</summary>
    public string TurnClockDisplay => TurnMinutes <= 0 ? "Off" : $"{TurnMinutes:0.#} min";

    public int RoundsToWin { get => _roundsToWin; set => Set(ref _roundsToWin, Math.Clamp(value, 1, 9)); }
    public DeckMode DeckMode { get => _deckMode; set { if (Set(ref _deckMode, value)) UpdateTotals(); } }
    public EmptyDeckRule EmptyDeck { get => _emptyDeck; set => Set(ref _emptyDeck, value); }
    public bool Leaders { get => _leaders; set { if (Set(ref _leaders, value)) UpdateTotals(); } }
    public bool Warzones { get => _warzones; set { if (Set(ref _warzones, value)) UpdateTotals(); } }
    public bool Archons { get => _archons; set { if (Set(ref _archons, value)) UpdateTotals(); } }
    public bool Spacecraft { get => _spacecraft; set { if (Set(ref _spacecraft, value)) UpdateTotals(); } }

    /// <summary>Blank = random. Any integer replays the same shuffle.</summary>
    public string Seed { get => _seed; set => Set(ref _seed, value); }

    public string Filter
    {
        get => _filter;
        set { if (Set(ref _filter, value)) CardsView.Refresh(); }
    }

    public CardCountRow? SelectedCard { get => _selectedCard; set => Set(ref _selectedCard, value); }

    public string Totals { get => _totals; private set => Set(ref _totals, value); }

    // ------------------------------------------------------------------ player + online

    /// <summary>Your name: shown to your opponent online and on the score panel. Saved between runs.</summary>
    public string PlayerName { get => _playerName; set => Set(ref _playerName, value); }

    /// <summary>The relay address (your Cloudflare Worker, or ws://localhost:5080). Saved between runs.</summary>
    public string ServerAddress { get => _serverAddress; set => Set(ref _serverAddress, value); }

    /// <summary>The code typed by a player joining a game.</summary>
    public string JoinCode { get => _joinCode; set => Set(ref _joinCode, value); }

    /// <summary>What the online panel is doing ("Connecting…", "Waiting for your opponent…", an error).</summary>
    public string OnlineStatus { get => _onlineStatus; set => Set(ref _onlineStatus, value); }

    /// <summary>The room code while hosting (shown big, for reading out to your opponent).</summary>
    public string HostCode
    {
        get => _hostCode;
        set { if (Set(ref _hostCode, value)) Raise(nameof(IsHosting)); }
    }

    public bool IsHosting => HostCode.Length > 0;

    /// <summary>True while hosting or joining: the buttons that would start something else are disabled.</summary>
    public bool OnlineBusy
    {
        get => _onlineBusy;
        set { if (Set(ref _onlineBusy, value)) Raise(nameof(IsIdle)); }
    }

    public bool IsIdle => !OnlineBusy;

    /// <summary>Saves the name and server address for next time.</summary>
    public void SaveSettings()
    {
        var s = AppSettings.Current;
        s.PlayerName = PlayerName.Trim();
        s.ServerAddress = ServerAddress.Trim();
        s.TurnMinutes = TurnMinutes;
        s.Save();
    }

    public GameOptions BuildOptions()
    {
        var o = _defaults.Clone();
        o.RoundsToWin = RoundsToWin;
        o.DeckMode = DeckMode;
        o.EmptyDeck = EmptyDeck;
        o.Leaders = Leaders;
        o.Warzones = Warzones;
        o.AiDifficulty = AiDifficulty;
        o.Archons = Archons;
        o.Spacecraft = Spacecraft;
        o.TurnSeconds = (int)Math.Round(TurnMinutes * 60);
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
        int units = Cards.Where(r => r.Definition.IsUnit && !r.Definition.IsArchon).Sum(r => DeckBuilder.CopiesOf(r.Definition, o));
        int orders = Cards.Where(r => r.Definition.IsOrder && !r.Definition.IsArchon).Sum(r => DeckBuilder.CopiesOf(r.Definition, o));
        int warzones = Cards.Where(r => r.Definition.Kind == CardKind.Warzone).Sum(r => DeckBuilder.CopiesOf(r.Definition, o));
        int archons = Cards.Where(r => r.Definition.IsArchon).Sum(r => DeckBuilder.CopiesOf(r.Definition, o));
        string wz = (Warzones ? $" · Warzone deck {warzones}" : " · Warzones off") + (Archons ? $" · Archon deck {archons}" : "");
        Totals = (DeckMode == DeckMode.RandomDraw
            ? $"Random Draw: {Cards.Count(r => r.Definition.IsUnit && !r.Definition.IsArchon && DeckBuilder.CopiesOf(r.Definition, o) > 0)} distinct units, " +
              $"{Cards.Count(r => r.Definition.IsOrder && !r.Definition.IsArchon && DeckBuilder.CopiesOf(r.Definition, o) > 0)} distinct orders (decks never run out)"
            : $"Unit deck {units} · Order deck {orders}") + wz;
    }
}
