using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace UI_Revamp.Configuration;

public sealed class UiRevampSettings : INotifyPropertyChanged
{
    public const double MINIMUM_SLIDER_VALUE = 0.25;
    public const double DEFAULT_SLIDER_VALUE = 1.0;
    public const double MAXIMUM_SCALE_VALUE = 1.0;
    public const double MINIMUM_UI_OPACITY = 0.0;
    public const double DEFAULT_UI_OPACITY = 1.0;
    public const double MAXIMUM_UI_OPACITY = 1.0;
    public const double MINIMUM_HUD_WOBBLE_MULTIPLIER = 0.0;
    public const double DEFAULT_HUD_WOBBLE_MULTIPLIER = 1.0;
    public const double MAXIMUM_HUD_WOBBLE_MULTIPLIER = 2.0;

    bool _useDarkMode = true;
    bool _hideColonizationProgress = true;
    bool _hideMissionObjective = true;
    bool _hideCollectedItems = true;
    bool _hideTips = true;
    bool _hideControlHints = true;
    bool _compactFlightHud = true;
    bool _advancedColorPicker;
    double _uiScale = DEFAULT_SLIDER_VALUE;
    double _uiOpacity = DEFAULT_UI_OPACITY;
    double _hudWobbleMultiplier = DEFAULT_HUD_WOBBLE_MULTIPLIER;

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
        set => SetField(ref _uiScale, Clamp(value, MINIMUM_SLIDER_VALUE, MAXIMUM_SCALE_VALUE));
    }

    [JsonPropertyName("uiOpacity")]
    public double UiOpacity
    {
        get => _uiOpacity;
        set => SetField(ref _uiOpacity, Clamp(value, MINIMUM_UI_OPACITY, MAXIMUM_UI_OPACITY));
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
        set => SetField(ref _hudWobbleMultiplier, Clamp(value, MINIMUM_HUD_WOBBLE_MULTIPLIER, MAXIMUM_HUD_WOBBLE_MULTIPLIER));
    }

    public void Normalize()
    {
        UiScale = _uiScale;
        UiOpacity = _uiOpacity;
        HudWobbleMultiplier = _hudWobbleMultiplier;
    }

    public static double ClampUiScale(double value)
    {
        return Clamp(value, MINIMUM_SLIDER_VALUE, MAXIMUM_SCALE_VALUE);
    }

    public static double ClampUiOpacity(double value)
    {
        return Clamp(value, MINIMUM_UI_OPACITY, MAXIMUM_UI_OPACITY);
    }

    static double Clamp(double value, double minimum, double maximum)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return DEFAULT_SLIDER_VALUE;
        }

        return Math.Clamp(value, minimum, maximum);
    }

    void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
