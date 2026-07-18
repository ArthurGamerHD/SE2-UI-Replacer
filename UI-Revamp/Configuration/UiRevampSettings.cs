using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace UI_Revamp.Configuration;

public sealed class UiRevampSettings : INotifyPropertyChanged
{
    public const double MinimumSliderValue = 0.25;
    public const double DefaultSliderValue = 1.0;
    public const double MaximumScaleValue = 1.0;
    public const double MinimumHudWobbleMultiplier = 0.0;
    public const double DefaultHudWobbleMultiplier = 1.0;
    public const double MaximumHudWobbleMultiplier = 2.0;

    private bool _useDarkMode = true;
    private bool _hideColonizationProgress = true;
    private bool _hideMissionObjective = true;
    private bool _hideCollectedItems = true;
    private bool _hideTips = true;
    private bool _hideControlHints = true;
    private bool _compactFlightHud = true;
    private bool _advancedColorPicker;
    private double _uiScale = DefaultSliderValue;
    private double _hudWobbleMultiplier = DefaultHudWobbleMultiplier;

    public event PropertyChangedEventHandler? PropertyChanged;

    [JsonPropertyName("useDarkMode")]
    public bool UseDarkMode
    {
        get => _useDarkMode;
        set => SetField(ref _useDarkMode, value);
    }

    [JsonPropertyName("hideColonizationProgress")]
    public bool HideColonizationProgress
    {
        get => _hideColonizationProgress;
        set => SetField(ref _hideColonizationProgress, value);
    }

    [JsonPropertyName("hideMissionObjective")]
    public bool HideMissionObjective
    {
        get => _hideMissionObjective;
        set => SetField(ref _hideMissionObjective, value);
    }

    [JsonPropertyName("hideCollectedItems")]
    public bool HideCollectedItems
    {
        get => _hideCollectedItems;
        set => SetField(ref _hideCollectedItems, value);
    }

    [JsonPropertyName("hideTips")]
    public bool HideTips
    {
        get => _hideTips;
        set => SetField(ref _hideTips, value);
    }

    [JsonPropertyName("hideControlHints")]
    public bool HideControlHints
    {
        get => _hideControlHints;
        set => SetField(ref _hideControlHints, value);
    }

    [JsonPropertyName("uiScale")]
    public double UiScale
    {
        get => _uiScale;
        set => SetField(ref _uiScale, Clamp(value, MinimumSliderValue, MaximumScaleValue));
    }

    [JsonPropertyName("compactFlightHud")]
    public bool CompactFlightHud
    {
        get => _compactFlightHud;
        set => SetField(ref _compactFlightHud, value);
    }

    [JsonPropertyName("advancedColorPicker")]
    public bool AdvancedColorPicker
    {
        get => _advancedColorPicker;
        set => SetField(ref _advancedColorPicker, value);
    }

    [JsonPropertyName("hudWobbleMultiplier")]
    public double HudWobbleMultiplier
    {
        get => _hudWobbleMultiplier;
        set => SetField(ref _hudWobbleMultiplier, Clamp(value, MinimumHudWobbleMultiplier, MaximumHudWobbleMultiplier));
    }

    public void Normalize()
    {
        UiScale = _uiScale;
        HudWobbleMultiplier = _hudWobbleMultiplier;
    }

    public static double ClampUiScale(double value)
    {
        return Clamp(value, MinimumSliderValue, MaximumScaleValue);
    }

    private static double Clamp(double value, double minimum, double maximum)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return DefaultSliderValue;
        }

        return Math.Clamp(value, minimum, maximum);
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
