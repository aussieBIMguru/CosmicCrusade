using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CosmicCrusade.App.Controls;
using CosmicCrusade.App.ViewModels;
using CosmicCrusade.Core.Model;
using CosmicCrusade.Core.Net;

namespace CosmicCrusade.App.Views;

public partial class SetupView : UserControl
{
    private readonly SetupViewModel _vm;
    private CancellationTokenSource? _onlineCts;

    /// <summary>How long a joining player waits for the host's answer before giving up.</summary>
    private static readonly TimeSpan JoinTimeout = TimeSpan.FromSeconds(30);

    public SetupView(SetupViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        vm.PropertyChanged += OnVmChanged;
        UpdatePreview();
        // Leaving the menu while hosting/joining (e.g. closing the window) cancels it.
        Unloaded += (_, _) => _onlineCts?.Cancel();
    }

    /// <summary>Raised with the chosen options when PLAY VS AI is clicked.</summary>
    public event Action<GameOptions>? StartRequested;

    /// <summary>Raised when an online game is agreed (both players connected and set up).</summary>
    public event Action<OnlineSession>? OnlineStartRequested;

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SetupViewModel.SelectedCard)) UpdatePreview();
    }

    private void UpdatePreview()
    {
        var def = _vm.SelectedCard?.Definition;
        if (def is null)
        {
            PreviewHost.Child = null;
            return;
        }
        // A throwaway instance just for display.
        PreviewHost.Child = new CardView(new CardInstance(0, def), faceDown: false, 216, vector: true);
    }

    private void Start_Click(object sender, RoutedEventArgs e)
    {
        _vm.SaveSettings();
        StartRequested?.Invoke(_vm.BuildOptions());
    }

    // =================================================================== player guides

    /// <summary>Opens a player guide (docs next to the exe once installed; docs\players in the source tree).</summary>
    private void Doc_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Documents.Hyperlink { Tag: string file }) return;
        string? path = AppPaths.PlayerDoc(file);
        if (path is null)
        {
            MessageBox.Show($"Couldn't find {file}. It's in the docs folder next to the game, or docs\\players in the source.",
                "Cosmic Crusade", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch
        {
            // No app for .md files: open it in Notepad instead.
            System.Diagnostics.Process.Start("notepad.exe", $"\"{path}\"");
        }
    }

    // =================================================================== online

    /// <summary>Checks the name and server boxes; explains what's missing.</summary>
    private bool ReadyForOnline(out string name, out string server)
    {
        name = NetLobby.Display(_vm.PlayerName);
        server = _vm.ServerAddress.Trim();
        if (string.IsNullOrWhiteSpace(_vm.PlayerName))
        {
            _vm.OnlineStatus = "Type your name first – your opponent sees it.";
            return false;
        }
        if (server.Length == 0)
        {
            _vm.OnlineStatus = "Fill in the Server box: the online server's address (ask whoever gave you the game, or see the Playing online guide), or ws://localhost:5080 if this PC runs the LAN Server.";
            return false;
        }
        _vm.PlayerName = name;
        _vm.SaveSettings();
        return true;
    }

    private async void Host_Click(object sender, RoutedEventArgs e)
    {
        if (!ReadyForOnline(out string name, out string server)) return;
        var options = _vm.BuildOptions(); // the setup on the left, seed included: the guest gets exactly this
        BeginOnline("Connecting to the server…");
        var ct = _onlineCts!.Token;

        NetConnection? net = null;
        try
        {
            // A fresh code clashing with a room someone else has open is very unlikely; try a couple of times anyway.
            for (int attempt = 1; ; attempt++)
            {
                string code = NetProtocol.NewRoomCode();
                net = NetConnection.ForServer(server, "host", code);
                try
                {
                    await net.StartAsync(ct);
                    _vm.HostCode = code;
                    break;
                }
                catch (NetRefusedException ex) when (attempt < 3 && ex.Message.Contains("in use"))
                {
                    await net.DisposeAsync();
                }
            }

            _vm.OnlineStatus = "Waiting for your opponent. Give them this code (click it to copy) – they type it next to JOIN.";
            var setup = await NetLobby.HostAsync(net!, options, name, AppPaths.DataFingerprint, ct);
            FinishOnline(new OnlineSession(net!, setup));
        }
        catch (Exception ex)
        {
            if (net is not null) await net.DisposeAsync();
            OnlineFailed(ex);
        }
    }

    private async void Join_Click(object sender, RoutedEventArgs e)
    {
        if (!ReadyForOnline(out string name, out string server)) return;
        string code = NetProtocol.NormaliseCode(_vm.JoinCode);
        if (code.Length < 4)
        {
            _vm.OnlineStatus = "Type the code your opponent got when they hosted.";
            return;
        }
        BeginOnline($"Joining {code}…");
        var ct = _onlineCts!.Token;

        var net = NetConnection.ForServer(server, "guest", code);
        try
        {
            await net.StartAsync(ct);
            _vm.OnlineStatus = "Connected. Getting the game from the host…";
            using var wait = CancellationTokenSource.CreateLinkedTokenSource(ct);
            wait.CancelAfter(JoinTimeout);
            NetGameSetup setup;
            try
            {
                setup = await NetLobby.JoinAsync(net, name, AppPaths.DataFingerprint, wait.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new NetException("The host didn't answer. Check the code, and that their game is still waiting.");
            }
            FinishOnline(new OnlineSession(net, setup));
        }
        catch (Exception ex)
        {
            await net.DisposeAsync();
            OnlineFailed(ex);
        }
    }

    private void JoinCode_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && _vm.IsIdle) Join_Click(sender, e);
    }

    private void CancelOnline_Click(object sender, RoutedEventArgs e) => _onlineCts?.Cancel();

    private void HostCode_Click(object sender, MouseButtonEventArgs e)
    {
        try
        {
            Clipboard.SetText(_vm.HostCode);
            _vm.OnlineStatus = $"Code {_vm.HostCode} copied. Waiting for your opponent…";
        }
        catch
        {
            // The clipboard can be busy; reading the code out works too.
        }
    }

    private void BeginOnline(string status)
    {
        _onlineCts?.Dispose();
        _onlineCts = new CancellationTokenSource();
        _vm.OnlineBusy = true;
        _vm.HostCode = "";
        _vm.OnlineStatus = status;
    }

    private void FinishOnline(OnlineSession session)
    {
        _vm.OnlineBusy = false;
        _vm.HostCode = "";
        _vm.OnlineStatus = "";
        OnlineStartRequested?.Invoke(session);
    }

    private void OnlineFailed(Exception ex)
    {
        _vm.OnlineBusy = false;
        _vm.HostCode = "";
        _vm.OnlineStatus = ex switch
        {
            OperationCanceledException => "Cancelled.",
            NetRefusedException or NetException => ex.Message,
            _ => "Something went wrong: " + ex.Message,
        };
    }
}
