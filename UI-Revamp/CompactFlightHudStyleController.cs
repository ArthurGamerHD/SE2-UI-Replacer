using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Keen.VRage.Library.Diagnostics;

namespace UI_Revamp;

internal static class CompactFlightHudStyleController
{
    static readonly Uri CompactFlightHudStyleUri = new("avares://UI-Revamp/Styles/CompactFlightHud.axaml");
    const string FlightHudControlTypeName = "Keen.Game2.Client.UI.HUD.Flight.FlightHUDControl";
    const string HudSpeedometerTypeName = "Keen.Game2.Client.UI.HUD.Movement.HUDSpeedometer";
    const string HudAltimeterTypeName = "Keen.Game2.Client.UI.HUD.Flight.HUDAltimeter";
    const string HudHorizonIndicatorTypeName = "Keen.Game2.Client.UI.HUD.Flight.HUDHorizonIndicator";
    static StyleInclude? CompactFlightHudStyle;

    static readonly MethodInfo? InvalidateStylesMethod = typeof(StyledElement).GetMethod(
        "InvalidateStyles",
        BindingFlags.Instance | BindingFlags.NonPublic);

    public static void Reload()
    {
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                var styleRoot = GetStyleRoot();
                if (styleRoot == null)
                {
                    return;
                }

                if (CompactFlightHudStyle != null)
                {
                    styleRoot.Styles.Remove(CompactFlightHudStyle);
                    CompactFlightHudStyle = null;
                }

                if (!Plugin.Settings.CompactFlightHud)
                {
                    Log.Default.Info($"[{Plugin.PluginId}] Compact flight HUD styles unloaded.");
                    RefreshExistingFlightHudControls(styleRoot, compact: false);
                    return;
                }

                CompactFlightHudStyle = new StyleInclude(CompactFlightHudStyleUri)
                {
                    Source = CompactFlightHudStyleUri
                };
                styleRoot.Styles.Add(CompactFlightHudStyle);

                Log.Default.Info($"[{Plugin.PluginId}] Compact flight HUD styles loaded.");
                RefreshExistingFlightHudControls(styleRoot, compact: true);
            }
            catch (Exception e)
            {
                CompactFlightHudStyle = null;
                Log.Default.Error($"[{Plugin.PluginId}] Failed to reload compact flight HUD styles: {e}");
            }
        });
    }

    static Control? GetStyleRoot()
    {
        var mainWindow = Plugin.MainWindow;
        if (mainWindow == null)
        {
            return null;
        }

        return mainWindow
            .GetType()
            .GetField("StyleRoot", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?.GetValue(mainWindow) as Control;
    }

    static void RefreshExistingFlightHudControls(Control styleRoot, bool compact)
    {
        var controls = EnumerateControls(styleRoot).ToArray();
        foreach (var flightHud in controls.Where(IsFlightHudControl))
        {
            InvalidateStyles(flightHud, recurse: true);
            if (flightHud is TemplatedControl templatedControl)
            {
                templatedControl.ApplyTemplate();
            }

            flightHud.InvalidateMeasure();
            flightHud.InvalidateVisual();
        }

        foreach (var horizon in controls.Where(IsHudHorizonIndicator).OfType<TemplatedControl>())
        {
            InvalidateStyles(horizon, recurse: true);
            horizon.ApplyTemplate();
            horizon.InvalidateMeasure();
            horizon.InvalidateVisual();
        }

        foreach (var indicator in controls.Where(IsFlightSideIndicator))
        {
            indicator.IsVisible = !compact;
            indicator.InvalidateMeasure();
            indicator.InvalidateVisual();
        }

        styleRoot.InvalidateMeasure();
        styleRoot.InvalidateVisual();
    }

    static IEnumerable<Control> EnumerateControls(Control root)
    {
        yield return root;

        foreach (var control in root.GetVisualDescendants().OfType<Control>())
        {
            yield return control;
        }
    }

    static bool IsFlightHudControl(Control control)
    {
        return control.GetType().FullName == FlightHudControlTypeName;
    }

    static bool IsHudHorizonIndicator(Control control)
    {
        return control.GetType().FullName == HudHorizonIndicatorTypeName;
    }

    static bool IsFlightSideIndicator(Control control)
    {
        var typeName = control.GetType().FullName;
        if (typeName == HudAltimeterTypeName)
        {
            return true;
        }

        return typeName == HudSpeedometerTypeName && control.Classes.Contains("Flight");
    }

    static void InvalidateStyles(StyledElement element, bool recurse)
    {
        InvalidateStylesMethod?.Invoke(element, new object[] { recurse });
    }
}
