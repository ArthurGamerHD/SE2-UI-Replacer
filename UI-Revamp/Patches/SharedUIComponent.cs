using HarmonyLib;
using Keen.Game2.Client.UI.Library;
using Keen.VRage.Library.Diagnostics;
using SE2PluginLoader;

namespace UI_Revamp.Patches;

[HarmonyPatch(typeof(SharedUIComponent))]
[HarmonyPatch("PostInit")]
public class SharedUiComponentPatches  
{
    [HarmonyPostfix]
    public static void Postfix(SharedUIComponent instance)
    {
        Plugin.SharedUi = instance;
        Log.Default.Info($"[{Plugin.PluginId}] SharedUI captured");
    }
}