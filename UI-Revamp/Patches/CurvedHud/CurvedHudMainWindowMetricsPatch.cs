using System;
using System.Reflection;
using Avalonia.Threading;
using HarmonyLib;
using UI_Revamp.CurvedHud;

namespace UI_Revamp.Patches.CurvedHud;

/// <summary>
/// Rebuilds the offscreen surface when the real display resolution changes.
/// UI-scale changes are forwarded directly by Plugin.ApplyUiScale after the
/// main Avalonia window has resolved its new logical ClientSize.
/// </summary>
[HarmonyPatch]
internal static class CurvedHudMainWindowResolutionPatch
{
    static MethodBase TargetMethod()
    {
        Type windowImpl = AccessTools.TypeByName(
                "Keen.VRage.UI.AvaloniaInterface.Main.WindowImpl")
            ?? throw new MissingMemberException("WindowImpl not found.");

        return AccessTools.Method(windowImpl, "OnResolutionChanged")
            ?? throw new MissingMethodException(
                windowImpl.FullName,
                "OnResolutionChanged");
    }

    // ReSharper disable once InconsistentNaming
    static void Postfix(object __instance)
    {
        object? mainPlatformWindow = Plugin.MainWindow == null
            ? null
            : typeof(Avalonia.Controls.TopLevel)
                .GetProperty(
                    "PlatformImpl",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetValue(Plugin.MainWindow);

        if (ReferenceEquals(__instance, mainPlatformWindow))
        {
            // DisplaySettingsChanged invokes every WindowImpl subscriber. Defer
            // target recreation until the event has finished updating both the
            // main window and the render-only curved-HUD window.
            Dispatcher.UIThread.Post(
                CurvedHudController.RefreshForMainWindowMetrics,
                DispatcherPriority.Loaded);
        }
    }
}
