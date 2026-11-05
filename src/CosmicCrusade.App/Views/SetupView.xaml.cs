using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using CosmicCrusade.App.Controls;
using CosmicCrusade.App.ViewModels;
using CosmicCrusade.Core.Model;

namespace CosmicCrusade.App.Views;

public partial class SetupView : UserControl
{
    private readonly SetupViewModel _vm;

    public SetupView(SetupViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        vm.PropertyChanged += OnVmChanged;
        UpdatePreview();
    }

    /// <summary>Raised with the chosen options when START GAME is clicked.</summary>
    public event Action<GameOptions>? StartRequested;

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
        PreviewHost.Child = new CardView(new CardInstance(0, def), faceDown: false, 268, vector: true);
    }

    private void Start_Click(object sender, RoutedEventArgs e) => StartRequested?.Invoke(_vm.BuildOptions());
}
