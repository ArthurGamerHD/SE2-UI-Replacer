using HarmonyLib;
using Keen.Game2.Client.UI.Library;
using Keen.VRage.Library.Diagnostics;
using UI_Revamp.CurvedHud;

namespace UI_Revamp.Patches;

[HarmonyPatch(typeof(SharedUIComponent), nameof(SharedUIComponent.PostInit))]
public class SharedUiComponentPatches  
{
    [HarmonyPostfix]
    // ReSharper disable once InconsistentNaming
    public static void Postfix(SharedUIComponent __instance)
    {
        Plugin.SharedUi = __instance;
        Log.Default.Info($"[{Plugin.PluginId}] SharedUI captured");
        Log.Default.Info(
            $"[{Plugin.PluginId}] Curved HUD startup deferred until the world-loaded transition completes.");
    }
}
