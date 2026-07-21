using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
#if PREVIEW
using System;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
#endif
#if !PREVIEW && !IS_DESIGN
using Keen.VRage.UI.AvaloniaInterface.Utilities;
#endif

namespace UI_Revamp.CurvedHud;

public partial class CurvedHudWindow : Window
{
    const string SpeedBarValuePropertyName = "SpeedBarValue";
    const string AltitudeBarValuePropertyName = "AltitudeBarValue";
    const string AltitudeValueTextPropertyName = "AltitudeValueText";
    const string ShowPlanetHudPropertyName = "ShowPlanetHud";
    const string AltitudeIconResourceKey = "FlightHUDConfiguration_TerrainIcon";
#if PREVIEW
    static readonly Uri PreviewTerrainIconUri = new(
        "avares://UI-Revamp/Assets/Preview/HUD/Terrain_Icon.png");
    readonly HudPreviewViewModel _previewViewModel = new();
#endif

    public CurvedHudWindow()
    {
        InitializeComponent();
        BindReadouts();
        ConfigureAltitudeIconSource();

#if PREVIEW
        ConfigurePreviewMode();
#else
#if IS_DESIGN
        ShowDesignPreview();
#else
        if (Design.IsDesignMode)
            ShowDesignPreview();
#endif
#endif
    }

    void ShowDesignPreview()
    {
        PART_DesignPreview.IsVisible = true;
        PART_HudPanelHost.IsVisible = true;
        PART_CenterProgressBars.IsVisible = true;
        PART_Compass.IsVisible = true;
        PART_ArtificialHorizon.IsVisible = true;
    }

    void BindReadouts()
    {
        PART_LeftProgressBar.Bind(
            RangeBase.ValueProperty,
            new Binding(SpeedBarValuePropertyName));
        PART_RightProgressBar.Bind(
            RangeBase.ValueProperty,
            new Binding(AltitudeBarValuePropertyName));
        PART_AltitudeValueText.Bind(
            TextBlock.TextProperty,
            new Binding(AltitudeValueTextPropertyName));
        PART_AltitudeProgressColumn.Bind(
            IsVisibleProperty,
            new Binding(ShowPlanetHudPropertyName));
    }

    void ConfigureAltitudeIconSource()
    {
#if PREVIEW
        using var stream = AssetLoader.Open(PreviewTerrainIconUri);
        PART_AltitudeIcon.Source = new Bitmap(stream);
#elif !IS_DESIGN
        PART_AltitudeIcon.Bind(
            Image.SourceProperty,
            new VRageDynamicResource(AltitudeIconResourceKey));
#endif
    }

#if PREVIEW
    void ConfigurePreviewMode()
    {
        DataContext = _previewViewModel;

        MinHeight = 1080;
        MinWidth = 1920;
        Height = 1080;
        Width = 1920;
        PART_Compass.Bind(Compass.BearingProperty, CreatePreviewBinding(nameof(HudPreviewViewModel.Bearing)));
        PART_Compass.Bind(Compass.VisibleDegreesProperty, CreatePreviewBinding(nameof(HudPreviewViewModel.CompassVisibleDegrees)));
        PART_ArtificialHorizon.Bind(
            ArtificialHorizon.PitchProperty,
            CreatePreviewBinding(nameof(HudPreviewViewModel.Pitch)));
        PART_ArtificialHorizon.Bind(
            ArtificialHorizon.RollProperty,
            CreatePreviewBinding(nameof(HudPreviewViewModel.Roll)));

        AddPreviewUtilityOverlay();

        _previewViewModel.PropertyChanged += (_, _) => ApplyPreviewState();
        ApplyPreviewState();
    }

    Binding CreatePreviewBinding(string path)
    {
        return new Binding(path)
        {
            Source = _previewViewModel,
            Mode = BindingMode.TwoWay
        };
    }

    void AddPreviewUtilityOverlay()
    {
        if (PART_SafeArea.Child is not Panel root)
            return;

        var overlay = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto"),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0)
        };

        var utility = new HudPreviewUtility
        {
            DataContext = _previewViewModel,
            Margin = new Thickness(0, 0, 8, 0)
        };
        utility.Bind(IsVisibleProperty, new Binding(nameof(HudPreviewViewModel.ShowUtilityPanel))
        {
            Source = _previewViewModel
        });
        overlay.Children.Add(utility);

        var toggle = new ToggleSwitch
        {
            OnContent = string.Empty,
            OffContent = string.Empty,
            Width = 44,
            MinWidth = 44,
            Height = 32,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Background = SolidColorBrush.Parse("#CC11161C")
        };
        toggle.Bind(ToggleButton.IsCheckedProperty, new Binding(nameof(HudPreviewViewModel.ShowUtilityPanel))
        {
            Source = _previewViewModel,
            Mode = BindingMode.TwoWay
        });
        Grid.SetColumn(toggle, 1);
        overlay.Children.Add(toggle);

        root.Children.Add(overlay);
    }

    void ApplyPreviewState()
    {
        PART_DesignPreview.IsVisible = _previewViewModel.ShowDesignBackground;
        PART_HudPanelHost.IsVisible = true;
        PART_CenterProgressBars.IsVisible = true;

        bool showPlanetHud = _previewViewModel.ShowPlanetHud;
        PART_Compass.IsVisible = showPlanetHud;
        PART_ArtificialHorizon.IsVisible = showPlanetHud;
        PART_SpaceReticle.IsVisible = _previewViewModel.ShowSpaceReticle;
    }
#endif
}
