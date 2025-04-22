using System;
using HarmonyLib;
using Keen.VRage.Library.Collections.Readers;
using Keen.VRage.Library.Localization;

namespace PreviewHelper.PreviewPatches;

[HarmonyPatch(typeof(LocKey))]
public class LocKeyPatches
{
    [HarmonyPatch(nameof(LocKey.EvaluateText), new[] { typeof(DictionaryReader<string, object>) })]
    [HarmonyPrefix]
    public static bool PrefixEvaluateTextWithDictionaryReader(LocKey __instance, ref string __result)
    {
        __result = __instance.TextId;
        return false;
    }

    // Patch the second method
    [HarmonyPatch(nameof(LocKey.EvaluateText), new[] { typeof(Func<string, string>) })]
    [HarmonyPrefix]
    public static bool PrefixEvaluateTextWithFunc(LocKey __instance, ref string __result)
    {
        __result = __instance.TextId;
        return false;
    }
    
    [HarmonyPatch(nameof(LocKey.ToString))]
    [HarmonyPrefix]
    public static bool ToStringPrefix(LocKey __instance, ref string __result)
    {
        __result = __instance.TextId;
        return false;
    }
}