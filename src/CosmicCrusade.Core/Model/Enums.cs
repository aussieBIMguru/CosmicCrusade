namespace CosmicCrusade.Core.Model;

public enum Race
{
    GalacticCouncil,
    SpaceCowboy,
    Necrolite,
    Overmind,
    Blaarg,
    Synthenoid,
    Neutral,
}

public enum CardKind
{
    Unit,
    Order,
    Warzone,
}

public enum UnitClass
{
    Bystander,
    Underling,
    Minion,
    Brute,
    Champion,
    Leader,
}

public enum OrderType
{
    Action,
    React,
    Ongoing,
}

public enum NodeKind
{
    Unit,
    Leader,
    Standby,
    Order,
}

public enum PileKind
{
    UnitDeck,
    OrderDeck,
    RetiredUnits,
    NullifiedUnits,
    IssuedOrders,
    WarzoneDeck,
    /// <summary>The Active Warzone slot (a "pile" of 0..n cards; usually one).</summary>
    ActiveWarzone,
}

public enum GamePhase
{
    NotStarted,
    StartUp,
    Main,
    EndOfMain,
    CleanUp,
    GameOver,
}

public enum EmptyDeckRule
{
    Reshuffle,
    EndAfterRound,
    NoRefill,
}

public enum DeckMode
{
    Standard,
    TrueSingleton,
    RandomDraw,
}
