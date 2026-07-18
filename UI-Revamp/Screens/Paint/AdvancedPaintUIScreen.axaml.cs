using System.ComponentModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Keen.VRage.Library.Mathematics;
using Keen.VRage.UI.AvaloniaInterface.Services;
using Keen.VRage.UI.Screens;

namespace UI_Revamp.Screens.Paint;

[NeedsWindowStyles]
public partial class AdvancedPaintUIScreen : ScreenView
{
    private readonly AdvancedPaintUIViewModel _viewModel = null!;

    public AdvancedPaintUIScreen()
    {
        InitializeComponent();

        if (Design.IsDesignMode)
        {
            return;
        }

        _viewModel = (AdvancedPaintUIViewModel)DataContext!;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        PART_ItemsRepeater.Loaded += OnItemsRepeaterLoaded;
    }

    protected override void OnDispose()
    {
        base.OnDispose();
        PART_ItemsRepeater.Loaded -= OnItemsRepeaterLoaded;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(AdvancedPaintUIViewModel.SelectedIndex))
        {
            UpdatePalette();
        }
    }

    private void OnItemsRepeaterLoaded(object? sender, RoutedEventArgs routedEventArgs)
    {
        UpdatePalette();
    }

    private void Button_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.DataContext is ColorHSV color)
        {
            var index = _viewModel.Palette.IndexOf(color);
            if (index != -1)
            {
                _viewModel.SelectedIndex = index;
            }
        }
    }

    private void Button_Pressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Button { DataContext: ColorHSV color } ||
            !e.GetCurrentPoint(null).Properties.IsRightButtonPressed)
        {
            return;
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            _viewModel.ResetPalette();
            return;
        }

        var index = _viewModel.Palette.IndexOf(color);
        if (index != -1)
        {
            _viewModel.ResetIndex(index);
        }
    }

    private void UpdatePalette()
    {
        var palette = PART_ItemsRepeater.GetLogicalChildren().ToArray();

        for (var index = 0; index < palette.Length; index++)
        {
            if (palette[index] is Button button)
            {
                button.Classes.Set("Active", index == _viewModel.SelectedIndex);
            }
        }
    }
}
