using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using CosmicCrusade.App.ViewModels;
using CosmicCrusade.App.Views;
using CosmicCrusade.Core.Model;

namespace CosmicCrusade.App;

public partial class MainWindow : Window
{
    private SetupViewModel? _setup;

    /// <summary>"1.0.0", from &lt;Version&gt; in Directory.Build.props (online, both players should be on the same one).</summary>
    public static string AppVersion { get; } =
        typeof(MainWindow).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "dev";

    public MainWindow()
    {
        InitializeComponent();
        Title = $"Cosmic Crusade {AppVersion}";
        ShowSetup();
    }

    // Dark title bar to match the game (Windows 10 20H1+ / Windows 11; silently ignored elsewhere).
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            int dark = 1;
            DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int));       // DWMWA_USE_IMMERSIVE_DARK_MODE
            int caption = 0x00180A07;                                     // COLORREF (BGR) of #070A18
            DwmSetWindowAttribute(hwnd, 35, ref caption, sizeof(int));    // DWMWA_CAPTION_COLOR (Windows 11)
        }
        catch (DllNotFoundException)
        {
        }
        catch (EntryPointNotFoundException)
        {
        }
    }

    /// <summary>Closing the window mid-game online: tell the opponent you left (instead of looking like a drop).</summary>
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        (Host.Content as GameView)?.LeaveOnlineForShutdown();
        base.OnClosing(e);
    }

    private void ShowSetup()
    {
        _setup ??= new SetupViewModel(); // Keeps the player's choices between games.
        var view = new SetupView(_setup);
        view.StartRequested += StartGame;
        view.OnlineStartRequested += StartOnlineGame;
        Host.Content = view;
    }

    private void StartGame(GameOptions options)
    {
        var game = new GameView(options);
        game.ExitRequested += ShowSetup;
        game.RestartRequested += () =>
        {
            var again = options.Clone();
            again.Seed = Environment.TickCount;
            StartGame(again);
        };
        Host.Content = game;
    }

    /// <summary>Online: the options came from the host; Menu (or Back to Menu) returns to the setup screen.</summary>
    private void StartOnlineGame(OnlineSession session)
    {
        var game = new GameView(session.Setup.Options, session);
        game.ExitRequested += ShowSetup;
        Host.Content = game;
    }
}
