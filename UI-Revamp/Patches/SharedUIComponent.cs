using HarmonyLib;
using Keen.Game2.Client.UI.Library;
using Keen.VRage.Library.Diagnostics;

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
    }
}
