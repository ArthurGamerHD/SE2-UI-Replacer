﻿﻿using System;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HarmonyLib;
using Keen.Game2.Client.GameSystems.PlayerControl;
using Keen.Game2.Client.UI.InGame;
using Keen.Game2.Client.UI.Library;
using Keen.VRage.Core;
using Keen.VRage.Core.Plugins;
using Keen.VRage.Library.Diagnostics;
using Keen.VRage.Library.Utils;
using Keen.VRage.UI.EngineComponents;
using UI_Revamp.Configuration;
using UI_Revamp.CurvedHud;

namespace UI_Revamp;

public class Plugin : IPlugin
{
    static Version _version = new Version(1, 0);
    public const string PluginId = "SE2-UI-Revamp";
    const string ShowColonizationNotificationResource = "UIRevamp_ShowColonizationNotification";
    const string ShowMissionObjectivesResource = "UIRevamp_ShowMissionObjectives";
    const string ShowTipsResource = "UIRevamp_ShowTips";
    const string ShowControlHintsResource = "UIRevamp_ShowControlHints";
    const string ShowCollectedItemsResource = "UIRevamp_ShowCollectedItems";
    const string CompactFlightHudResource = "UIRevamp_CompactFlightHud";
    Harmony _harmony = new(PluginId);

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string OptionsDirectory => Path.Combine(Singleton<VRageCore>.Instance.AppDataPath, "PluginsOptions", "Settings");
    static string PluginSettings => Path.Combine(OptionsDirectory, "UI-Revamp.json");
    public static UiRevampSettings Settings { get; private set; } = new();
    public static TopLevel? MainWindow { get; internal set; }
    public static double AppliedUiScale { get; private set; } = UiRevampSettings.DEFAULT_SLIDER_VALUE;

    public Plugin(PluginHost host)
    {
        Log.Default.Info($"[{PluginId}] {_version} Initializing.");

        LoadSettings();

        try
        {
            _harmony.PatchAll(Assembly.GetExecutingAssembly());
        }
        catch (Exception e)
        {
            Log.Default.Error($"[{PluginId}] Fail to initialize: {e}");
        }

        Log.Default.Info($"[{PluginId}] Initialized");
    }

    static void LoadSettings()
    {
        try
        {
            Log.Default.Info($"[{PluginId}] Loading Settings");
            if (!Directory.Exists(OptionsDirectory))
            {
                Directory.CreateDirectory(OptionsDirectory);
            }

            if (!File.Exists(PluginSettings) || new FileInfo(PluginSettings).Length == 0)
            {
                Settings = new UiRevampSettings();
                AppliedUiScale = Settings.UiScale;
                Settings.PropertyChanged += OnSettingsPropertyChanged;
                UpdateHudResources();
                SaveSettings();
                return;
            }

            try
            {
                Settings = JsonSerializer.Deserialize<UiRevampSettings>(File.ReadAllText(PluginSettings), JsonOptions) ?? new UiRevampSettings();
                Settings.Normalize();
                AppliedUiScale = Settings.UiScale;
                Settings.PropertyChanged += OnSettingsPropertyChanged;
                UpdateHudResources();
                SaveSettings();
            }
            catch (Exception e)
            {
                Log.Default.Error($"[{PluginId}] Fail to load settings\n" + e.Message);
                Settings = new UiRevampSettings();
                AppliedUiScale = Settings.UiScale;
                Settings.PropertyChanged += OnSettingsPropertyChanged;
                UpdateHudResources();
                SaveSettings();
            }
        }
        catch (Exception e)
        {
            Log.Default.Error(e.Message);
            throw;
        }
    }

    public static void SaveSettings()
    {
        try
        {
            if (!Directory.Exists(OptionsDirectory))
            {
                Directory.CreateDirectory(OptionsDirectory);
            }

            File.WriteAllText(PluginSettings, JsonSerializer.Serialize(Settings, JsonOptions));
        }
        catch (Exception e)
        {
            Log.Default.Error($"[{PluginId}] Fail to save settings\n" + e.Message);
        }
    }

    static void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        SaveSettings();
        UpdateHudResources();

        if (args.PropertyName == nameof(UiRevampSettings.UseDarkMode))
            DarkModeStyleController.ShowRestartRequiredDialog();

        if (args.PropertyName == nameof(UiRevampSettings.CompactFlightHud))
        {
            CurvedHudController.ApplySettings();
            NativeFlightHudController.Refresh();
        }

        if (args.PropertyName == nameof(UiRevampSettings.UiOpacity))
            CurvedHud.CurvedHudController.RefreshComposition();

        InvalidateMainWindow();
    }

    public static void UpdateHudResources()
    {
        var showColonizationNotification = !Settings.HideColonizationProgress;
        var showMissionObjectives = !Settings.HideMissionObjective;
        var showTips = !Settings.HideTips;
        var showControlHints = !Settings.HideControlHints;
        var showCollectedItems = !Settings.HideCollectedItems;
        var compactFlightHud = Settings.CompactFlightHud;

        SetAvaloniaResource(ShowColonizationNotificationResource, showColonizationNotification, typeof(bool));
        SetAvaloniaResource(ShowMissionObjectivesResource, showMissionObjectives, typeof(bool));
        SetAvaloniaResource(ShowTipsResource, showTips, typeof(bool));
        SetAvaloniaResource(ShowControlHintsResource, showControlHints, typeof(bool));
        SetAvaloniaResource(ShowCollectedItemsResource, showCollectedItems, typeof(bool));
        SetAvaloniaResource(CompactFlightHudResource, compactFlightHud, typeof(bool));
    }

    static void SetAvaloniaResource(string name, object value, Type targetType)
    {
        var application = Application.Current;
        if (application != null)
        {
            application.Resources[name] = value;
        }

        var uiEngineComponent = UiEngineComponent;
        if (uiEngineComponent == null)
        {
            return;
        }

        uiEngineComponent.AddAvaloniaDynamicResource(name, value, targetType);
        uiEngineComponent.SetAvaloniaResource(name, value, targetType);
    }

    public static void ApplyUiScale(double scale)
    {
        AppliedUiScale = UiRevampSettings.ClampUiScale(scale);

        Dispatcher.UIThread.Post(() =>
        {
            var mainWindow = MainWindow;
            if (mainWindow == null)
            {
                return;
            }

            RefreshClientSize(mainWindow);
            NotifyScaleChanged(mainWindow);
            NotifyResized(mainWindow);
            mainWindow.InvalidateMeasure();
            mainWindow.InvalidateVisual();

            foreach (var visual in mainWindow.GetVisualDescendants())
            {
                if (visual is Control control)
                {
                    control.InvalidateMeasure();
                    control.InvalidateVisual();
                }
            }

            CurvedHud.CurvedHudController.RefreshForMainWindowMetrics();

            Log.Default.Info($"[{PluginId}] Applied UI scale {AppliedUiScale:P0}. RenderScaling is now {mainWindow.RenderScaling:F3}.");
        });
    }

    static void InvalidateMainWindow()
    {
        Dispatcher.UIThread.Post(() =>
        {
            var mainWindow = MainWindow;
            if (mainWindow == null)
            {
                return;
            }

            mainWindow.InvalidateMeasure();
            mainWindow.InvalidateVisual();

            foreach (var visual in mainWindow.GetVisualDescendants())
            {
                if (visual is Control control)
                {
                    control.InvalidateMeasure();
                    control.InvalidateVisual();
                }
            }
        });
    }

    static Size? RefreshClientSize(TopLevel mainWindow)
    {
        var platformImpl = mainWindow.PlatformImpl;
        if (platformImpl == null)
        {
            return null;
        }

        var platformImplType = platformImpl.GetType();
        platformImplType
            .GetField("_clientSize", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.SetValue(platformImpl, default(Size));

        if (platformImplType.GetProperty("ClientSize", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetValue(platformImpl) is not Size clientSize)
        {
            return null;
        }

        if (platformImplType.GetProperty("Position", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetValue(platformImpl) is PixelPoint position)
        {
            platformImplType
                .GetField("<Bounds>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(platformImpl, new Rect(position.X, position.Y, clientSize.Width, clientSize.Height));
        }

        return clientSize;
    }

    static void NotifyScaleChanged(TopLevel mainWindow)
    {
        var platformImpl = mainWindow.PlatformImpl;
        var scalingChanged = platformImpl
            ?.GetType()
            .GetProperty("ScalingChanged", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?.GetValue(platformImpl) as Action<double>;

        scalingChanged?.Invoke(mainWindow.RenderScaling);
    }

    static void NotifyResized(TopLevel mainWindow)
    {
        var platformImpl = mainWindow.PlatformImpl;
        if (platformImpl == null)
        {
            return;
        }

        var platformImplType = platformImpl.GetType();
        if (platformImplType.GetProperty("ClientSize", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetValue(platformImpl) is not Size clientSize)
        {
            return;
        }

        if (platformImplType
                .GetProperty("Resized", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetValue(platformImpl) is Action<Size, WindowResizeReason> resized)
        {
            resized(clientSize, WindowResizeReason.Application);
        }
    }

    public static SharedUIComponent? SharedUi { get; internal set; }
    public static InGameUI? InGameUi { get; internal set; }
    public static ClientPlayersSessionComponent? ClientPlayers { get; internal set; }
    public static UIEngineComponent? UiEngineComponent { get; internal set; }
}
