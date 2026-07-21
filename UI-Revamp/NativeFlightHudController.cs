using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Keen.Game2.Client.UI.HUD.Flight;
using Keen.VRage.Library.Diagnostics;
using UI_Revamp.CurvedHud;

namespace UI_Revamp;

internal static class NativeFlightHudController
{
    const string FlightHudControlTypeName =
        "Keen.Game2.Client.UI.HUD.Flight.FlightHUDControl";

    static int _loggedRefreshFailure;
    static int _loggedVisibilityProbeFailure;
    static readonly ConditionalWeakTable<Control, OpacityOverrideState> OpacityOverrides = new();

    internal static bool IsNativeFlightHudVisible()
    {
        try
        {
            var mainWindow = Plugin.MainWindow;
            return mainWindow != null &&
                   FindFlightHudControls(mainWindow).Any(IsFlightHudControlVisible);
        }
        catch (Exception exception)
        {
            if (Interlocked.Exchange(ref _loggedVisibilityProbeFailure, 1) == 0)
            {
                Log.Default.Error(
                    $"[{Plugin.PluginId}] Failed to probe native flight HUD visibility: {exception}");
            }

            return false;
        }
    }

    internal static void Refresh()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            RefreshOnUiThread();
        }
        else
        {
            Dispatcher.UIThread.Post(RefreshOnUiThread, DispatcherPriority.Loaded);
        }
    }

    static void RefreshOnUiThread()
    {
        try
        {
            var mainWindow = Plugin.MainWindow;
            if (mainWindow == null)
                return;

            bool hideNativeFlightHud = CurvedHudController.IsFlightHudVisible;
            foreach (Control control in FindFlightHudControls(mainWindow))
            {
                ApplyOpacityOverride(control, hideNativeFlightHud);
            }
        }
        catch (Exception exception)
        {
            if (Interlocked.Exchange(ref _loggedRefreshFailure, 1) == 0)
            {
                Log.Default.Error(
                    $"[{Plugin.PluginId}] Failed to refresh native flight HUD visibility: {exception}");
            }
        }
    }

    static IEnumerable<Control> FindFlightHudControls(TopLevel mainWindow)
    {
        var seen = new HashSet<Control>(ReferenceEqualityComparer.Instance);
        foreach (Control control in mainWindow.GetVisualDescendants().OfType<Control>())
        {
            if (control.GetType().FullName == FlightHudControlTypeName &&
                seen.Add(control))
            {
                yield return control;
            }
        }
    }

    static bool IsFlightHudControlVisible(Control control)
    {
        return control.IsEffectivelyVisible &&
               GetNativeOpacity(control) > 0.0 &&
               control.DataContext is FlightHUDViewModel { IsVisible: true };
    }

    static void ApplyOpacityOverride(Control control, bool hide)
    {
        if (hide)
        {
            Hide(control);
            return;
        }

        Restore(control);
    }

    static void Hide(Control control)
    {
        OpacityOverrideState state = OpacityOverrides.GetValue(
            control,
            static controlToHide => new OpacityOverrideState(controlToHide.Opacity));

        if (!state.HiddenByCompactHud)
        {
            state.NativeOpacity = control.Opacity;
            state.HiddenByCompactHud = true;
        }

        if (!Equals(control.Opacity, 0.0))
            control.Opacity = 0.0;
    }

    static void Restore(Control control)
    {
        if (!OpacityOverrides.TryGetValue(control, out OpacityOverrideState? state) ||
            !state.HiddenByCompactHud)
        {
            return;
        }

        state.HiddenByCompactHud = false;
        if (!Equals(control.Opacity, state.NativeOpacity))
            control.Opacity = state.NativeOpacity;
    }

    static double GetNativeOpacity(Control control)
    {
        if (OpacityOverrides.TryGetValue(control, out OpacityOverrideState? state) &&
            state.HiddenByCompactHud)
        {
            return state.NativeOpacity;
        }

        return control.Opacity;
    }

    sealed class OpacityOverrideState(double nativeOpacity)
    {
        public double NativeOpacity = nativeOpacity;
        public bool HiddenByCompactHud;
    }
}
