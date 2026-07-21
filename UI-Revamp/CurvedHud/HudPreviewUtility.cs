#if PREVIEW
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;

namespace UI_Revamp.CurvedHud;

public sealed class HudPreviewUtility : Border
{
    static readonly IBrush PanelBackground = SolidColorBrush.Parse("#E611161C");
    static readonly IBrush PanelBorder = SolidColorBrush.Parse("#4D71CFEB");
    static readonly IBrush PrimaryText = SolidColorBrush.Parse("#EAF7FB");
    static readonly IBrush SecondaryText = SolidColorBrush.Parse("#AEB7C2");

    public HudPreviewUtility()
    {
        Width = 330;
        MaxHeight = 620;
        Padding = new Thickness(12);
        CornerRadius = new CornerRadius(6);
        Background = PanelBackground;
        BorderBrush = PanelBorder;
        BorderThickness = new Thickness(1);

        var panel = new StackPanel
        {
            Spacing = 10
        };

        panel.Children.Add(new TextBlock
        {
            Text = "HUD Preview Utility",
            Foreground = PrimaryText,
            FontSize = 14,
            FontWeight = FontWeight.Bold
        });

        panel.Children.Add(CreateSwitch("Planet HUD", nameof(HudPreviewViewModel.HasNearestPlanet)));
        panel.Children.Add(CreateSwitch("In space", nameof(HudPreviewViewModel.IsInSpace)));
        panel.Children.Add(CreateSwitch("Background", nameof(HudPreviewViewModel.ShowDesignBackground)));
        panel.Children.Add(CreateDivider());
        panel.Children.Add(CreateSlider("Bearing", nameof(HudPreviewViewModel.Bearing), 0, 360, 15));
        panel.Children.Add(CreateSlider("FOV", nameof(HudPreviewViewModel.CompassVisibleDegrees), 20, 180, 5));
        panel.Children.Add(CreateDivider());
        panel.Children.Add(CreateSlider("Pitch", nameof(HudPreviewViewModel.Pitch), -90, 90, 1));
        panel.Children.Add(CreateSlider("Roll", nameof(HudPreviewViewModel.Roll), -180, 180, 5));
        panel.Children.Add(CreateDivider());
        panel.Children.Add(CreateSectionTitle("Speed"));
        panel.Children.Add(CreateSlider(
            "Current m/s",
            nameof(HudPreviewViewModel.Speed),
            0,
            1300,
            10,
            "{0:0.0}",
            nameof(HudPreviewViewModel.MaxSpeed)));
        panel.Children.Add(CreateSlider("Maximum m/s", nameof(HudPreviewViewModel.MaxSpeed), 0.1, 1300, 10, "{0:0.0}"));
        panel.Children.Add(CreateDivider());
        panel.Children.Add(CreateSectionTitle("Altitude"));
        panel.Children.Add(CreateSlider(
            "Current m",
            nameof(HudPreviewViewModel.Altitude),
            0,
            100000,
            100,
            "{0:0}",
            nameof(HudPreviewViewModel.MaxAltitude)));
        panel.Children.Add(CreateSlider("Maximum m", nameof(HudPreviewViewModel.MaxAltitude), 1, 100000, 1000, "{0:0}"));

        var resetButton = new Button
        {
            Content = "Reset",
            HorizontalAlignment = HorizontalAlignment.Right,
            Padding = new Thickness(14, 4)
        };
        resetButton.Click += (_, _) =>
        {
            if (DataContext is HudPreviewViewModel viewModel)
                viewModel.Reset();
        };
        panel.Children.Add(resetButton);

        Child = new ScrollViewer
        {
            Content = panel
        };
    }

    static Control CreateSwitch(string label, string propertyName)
    {
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto")
        };

        row.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = PrimaryText,
            VerticalAlignment = VerticalAlignment.Center
        });

        var toggle = new ToggleSwitch
        {
            OnContent = string.Empty,
            OffContent = string.Empty,
            HorizontalAlignment = HorizontalAlignment.Right,
            MinWidth = 44
        };
        toggle.Bind(ToggleButton.IsCheckedProperty, new Binding(propertyName)
        {
            Mode = BindingMode.TwoWay
        });
        Grid.SetColumn(toggle, 1);
        row.Children.Add(toggle);

        return row;
    }

    static Control CreateSlider(
        string label,
        string propertyName,
        double minimum,
        double maximum,
        double tickFrequency,
        string stringFormat = "{0:0.#}",
        string? maximumPropertyName = null)
    {
        var panel = new StackPanel
        {
            Spacing = 4
        };

        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto")
        };
        header.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = SecondaryText,
            FontSize = 11
        });

        var valueText = new TextBlock
        {
            Foreground = PrimaryText,
            FontSize = 11,
            MinWidth = 42,
            TextAlignment = TextAlignment.Right
        };
        valueText.Bind(TextBlock.TextProperty, new Binding(propertyName)
        {
            StringFormat = stringFormat
        });
        Grid.SetColumn(valueText, 1);
        header.Children.Add(valueText);
        panel.Children.Add(header);

        var slider = new Slider
        {
            Minimum = minimum,
            Maximum = maximum,
            TickFrequency = tickFrequency,
            IsSnapToTickEnabled = false
        };
        slider.Bind(RangeBase.ValueProperty, new Binding(propertyName)
        {
            Mode = BindingMode.TwoWay
        });
        if (maximumPropertyName != null)
        {
            slider.Bind(RangeBase.MaximumProperty, new Binding(maximumPropertyName));
        }

        panel.Children.Add(slider);

        return panel;
    }

    static Control CreateSectionTitle(string label)
    {
        return new TextBlock
        {
            Text = label,
            Foreground = PrimaryText,
            FontSize = 12,
            FontWeight = FontWeight.Bold,
            Margin = new Thickness(0, 1, 0, -3)
        };
    }

    static Control CreateDivider()
    {
        return new Border
        {
            Height = 1,
            Background = SolidColorBrush.Parse("#2671CFEB"),
            Margin = new Thickness(0, 2)
        };
    }
}
#endif
