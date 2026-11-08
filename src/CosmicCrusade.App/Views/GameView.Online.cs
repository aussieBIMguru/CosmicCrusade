using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CosmicCrusade.App.Board;
using CosmicCrusade.App.Controls;
using CosmicCrusade.App.ViewModels;
using CosmicCrusade.Core.Engine;
using CosmicCrusade.Core.Model;
using CosmicCrusade.Core.Net;

namespace CosmicCrusade.App.Views;

/// <summary>An online game handed from the setup screen to the table: the live connection and what both players agreed.</summary>
public sealed record OnlineSession(NetConnection Net, NetGameSetup Setup);

/// <summary>
/// Online play at the table: the two seats (you through <see cref="LocalSeat"/>, the opponent through
/// <see cref="RemoteSeat"/>), the chat box, the connection line, and what happens when someone drops or leaves.
/// Also the perspective helpers every partial uses (<see cref="Me"/>, <see cref="Opp"/>, <see cref="OppName"/>...).
/// </summary>
public partial class GameView
{
    private readonly OnlineSession? _online;
    private bool _onlineEnded;
    private DateTime _opponentAwaySince = DateTime.MaxValue;

    /// <summary>A short drop isn't worth covering the table: the waiting panel only appears after this long.</summary>
    private static readonly TimeSpan AwayGrace = TimeSpan.FromSeconds(3);

    // =================================================================== perspective (see Board/Perspective.cs)

    private static int Me => Perspective.Me;
    private static int Opp => Perspective.Opp;
    private static string OppName => Perspective.OppName;
    private static string TheOpp => Perspective.TheOpp;
    private static string TheOppCap => Perspective.TheOppCap;
    private static string OppPossessive => Perspective.OppPossessive;
    private static string OppPossessiveCap => Perspective.OppPossessiveCap;

    // =================================================================== setup

    private GameEngine CreateOnlineGame(OnlineSession s)
    {
        var net = s.Net;
        var local = new LocalSeat(_human, net);
        var remote = new RemoteSeat(net);
        remote.WaitingChanged += SetAiThinking; // the holographic eye pulses while the opponent decides

        net.ChatReceived += OnChatReceived;
        net.OpponentPresenceChanged += OnOpponentPresenceChanged;
        net.StateChanged += OnNetStateChanged;
        net.OpponentQuit += OnOpponentQuit;
        net.Failed += OnNetFailed;

        ChatPanel.Visibility = Visibility.Visible;
        PlayAgainButton.Visibility = Visibility.Collapsed;
        _hud.Chat.Add(new ChatLine("", $"You're playing {OppName} online (room {net.RoomCode}). Say hi!", false, Notice: true));
        _hud.AddLog($"Online game against {OppName}. You are {(Me == 0 ? "the host" : "the guest")}.");
        UpdateNetStatus();

        return Me == 0
            ? new GameEngine(State, local, remote, this)
            : new GameEngine(State, remote, local, this);
    }

    /// <summary>Leaving the table (Menu, closing the window): tell the opponent and close the connection.</summary>
    private void LeaveOnlineGame()
    {
        if (_online is null) return;
        var net = _online.Net;
        net.ChatReceived -= OnChatReceived;
        net.OpponentPresenceChanged -= OnOpponentPresenceChanged;
        net.StateChanged -= OnNetStateChanged;
        net.OpponentQuit -= OnOpponentQuit;
        net.Failed -= OnNetFailed;
        _ = net.LeaveAsync();
    }

    /// <summary>The window is closing: send the goodbye before the process ends (off the UI thread, so it can't deadlock).</summary>
    public void LeaveOnlineForShutdown()
    {
        if (_online is null) return;
        var net = _online.Net;
        try { Task.Run(() => net.LeaveAsync("closed the game")).Wait(TimeSpan.FromSeconds(1.5)); }
        catch { /* best effort */ }
    }

    // =================================================================== connection events (UI thread)

    private void OnNetStateChanged(NetLinkState state) => UpdateNetStatus();

    private void OnOpponentPresenceChanged(bool here)
    {
        if (_online is null || _onlineEnded) return;
        if (here)
        {
            if (_opponentAwaySince != DateTime.MaxValue)
                _hud.Chat.Add(new ChatLine("", $"{OppName} is back.", false, Notice: true));
            _opponentAwaySince = DateTime.MaxValue;
        }
        else
        {
            _opponentAwaySince = DateTime.UtcNow;
            if (!IsGameOver) _hud.Chat.Add(new ChatLine("", $"{OppPossessiveCap} connection dropped – waiting for them…", false, Notice: true));
            _ = RecheckAfterGraceAsync();
        }
        ScrollChat();
        UpdateNetStatus();
    }

    private async Task RecheckAfterGraceAsync()
    {
        await Task.Delay(AwayGrace + TimeSpan.FromMilliseconds(100));
        UpdateNetStatus();
    }

    private void OnOpponentQuit(string reason)
    {
        if (_onlineEnded) return;
        if (IsGameOver)
        {
            _hud.Chat.Add(new ChatLine("", $"{OppName} left.", false, Notice: true));
            _hud.NetStatus = $"{OppName} left";
            _hud.NetOk = false;
            ScrollChat();
            return;
        }
        OnlineEnded($"{OppName.ToUpperInvariant()} LEFT", $"{OppName} {reason}. The game can't continue.");
    }

    private void OnNetFailed(string reason)
    {
        if (IsGameOver)
        {
            _hud.NetStatus = "Disconnected";
            _hud.NetOk = false;
            return;
        }
        OnlineEnded("CONNECTION LOST", reason);
    }

    /// <summary>The status line, and the waiting panel over the table while someone is disconnected.</summary>
    private void UpdateNetStatus()
    {
        if (_online is null || _onlineEnded) return;
        var net = _online.Net;
        bool reconnecting = net.State is NetLinkState.Reconnecting or NetLinkState.Connecting;
        bool away = !reconnecting && !net.OpponentConnected;

        if (reconnecting)
        {
            _hud.NetStatus = "Reconnecting to the server…";
            _hud.NetOk = false;
        }
        else if (away)
        {
            _hud.NetStatus = $"{OppPossessiveCap} connection lost – waiting…";
            _hud.NetOk = false;
        }
        else
        {
            _hud.NetStatus = $"● Connected to {OppName}";
            _hud.NetOk = true;
        }

        bool showPanel = !IsGameOver
                         && (reconnecting || (away && DateTime.UtcNow - _opponentAwaySince >= AwayGrace));
        if (showPanel)
        {
            OnlineWaitTitle.Text = reconnecting ? "RECONNECTING…" : $"WAITING FOR {OppName.ToUpperInvariant()}";
            OnlineWaitText.Text = reconnecting
                ? "Your connection to the server dropped. Getting it back… (nothing is lost: any moves in flight are sent again)."
                : $"{OppPossessiveCap} connection dropped. The game carries on as soon as they're back – their app reconnects by itself. You can keep chatting, or leave.";
            OnlineWaitLeave.Content = "LEAVE GAME";
            OnlineWaitPanel.Visibility = Visibility.Visible;
        }
        else
        {
            OnlineWaitPanel.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>The online game is over early (opponent left, connection gone, games out of sync).</summary>
    private void OnlineEnded(string title, string text)
    {
        if (_online is null || _onlineEnded || IsGameOver) return;
        _onlineEnded = true;
        _cts.Cancel(); // stop the engine; the table stays up so you can read what happened
        _hud.CanPass = false;
        _hud.Status = title;
        _hud.NetStatus = "Online game ended";
        _hud.NetOk = false;
        _hud.AddLog($"{title}: {text}");
        SetAiThinking(false);
        OnlineWaitTitle.Text = title;
        OnlineWaitText.Text = text;
        OnlineWaitLeave.Content = "BACK TO MENU";
        OnlineWaitPanel.Visibility = Visibility.Visible;
        _ = _online.Net.LeaveAsync("left the game");
    }

    /// <summary>Called by ShowGameOver: online, the chat stays open until someone leaves.</summary>
    private void OnlineGameOver()
    {
        if (_online is null) return;
        OnlineWaitPanel.Visibility = Visibility.Collapsed;
        _hud.Chat.Add(new ChatLine("", "Game over – the chat stays open until one of you leaves. Host a new game from the menu for a rematch.", false, Notice: true));
        ScrollChat();
    }

    private bool IsGameOver => _state?.Phase == GamePhase.GameOver;

    // =================================================================== chat

    private void OnChatReceived(string name, string text)
    {
        _hud.AddChat(name, text, mine: false);
        Sfx.Play(SfxKind.Holo, 0.35, minGapMs: 400);
        ScrollChat();
    }

    private void ChatSend_Click(object sender, RoutedEventArgs e) => SendChat();

    private void ChatInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            SendChat();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            Focus(); // back to the table: Space passes again
            e.Handled = true;
        }
    }

    private void SendChat()
    {
        if (_online is null) return;
        string text = ChatInput.Text.Trim();
        if (text.Length == 0) return;
        _online.Net.SendChat(_online.Setup.MyName, text);
        _hud.AddChat(_online.Setup.MyName, text, mine: true);
        ChatInput.Clear();
        ScrollChat();
    }

    private void ScrollChat()
    {
        if (_hud.Chat.Count > 0) ChatList.ScrollIntoView(_hud.Chat[^1]);
    }
}
