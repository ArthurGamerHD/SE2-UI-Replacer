using HarmonyLib;
using Keen.VRage.Library.Diagnostics;
using Keen.VRage.UI.EngineComponents;
using SE2PluginLoader;

namespace UI_Revamp.Patches;

[HarmonyPatch(typeof(UIEngineComponent))]
[HarmonyPatch("PostInit")]
public class UiEngineComponentPatches  
{
    [HarmonyPostfix]
    public static void Postfix(UIEngineComponent instance)
    {
        Plugin.UiEngineComponent = instance;
        Log.Default.Info($"[{Plugin.PluginId}] UIEngineComponent captured");
    }
}