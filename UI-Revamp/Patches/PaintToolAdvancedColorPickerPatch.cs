using System;
using System.Reflection;
using HarmonyLib;
using Keen.Game2.Client.WorldObjects.Tools;
using Keen.VRage.Library.Diagnostics;
using Keen.VRage.Library.Utils;
using UI_Revamp.Screens.Paint;

namespace UI_Revamp.Patches;

[HarmonyPatch(typeof(PaintToolControllableComponent), nameof(PaintToolControllableComponent.OpenQuickSelectMenu))]
public static class PaintToolAdvancedColorPickerPatch
{
    private static readonly MethodInfo? OnQuickSelectMenuOpenedMethod =
        AccessTools.Method(typeof(PaintToolControllableComponent), "OnQuickSelectMenuOpened");

    private static readonly MethodInfo? OnQuickSelectMenuClosedMethod =
        AccessTools.Method(typeof(PaintToolControllableComponent), "OnQuickSelectMenuClosed");

    [HarmonyPrefix]
    public static bool Prefix(PaintToolControllableComponent __instance, ref IObservableDisposable? __result)
    {
        if (!Plugin.Settings.AdvancedColorPicker)
        {
            return true;
        }

        var sharedUi = Plugin.SharedUi;
        if (sharedUi == null)
        {
            Log.Default.Warning($"[{Plugin.PluginId}] Advanced color picker requested but SharedUI is unavailable.");
            return true;
        }

        try
        {
            var viewModel = new AdvancedPaintUIViewModel(__instance);
            var screen = sharedUi.CreateScreen<AdvancedPaintUIScreen>(viewModel, showCursor: true);

            OnQuickSelectMenuOpenedMethod?.Invoke(__instance, null);
            screen.OnDisposed += _ => OnQuickSelectMenuClosedMethod?.Invoke(__instance, null);

            __result = screen;
            Log.Default.Info($"[{Plugin.PluginId}] Opened advanced paint color picker.");
            return false;
        }
        catch (Exception e)
        {
            Log.Default.Error($"[{Plugin.PluginId}] Failed to open advanced paint color picker: {e}");
            return true;
        }
    }
}
