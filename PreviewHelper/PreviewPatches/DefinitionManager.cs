using HarmonyLib;
using Keen.Game2.Client.UI.Menu.News;
using Keen.VRage.Library.Definitions;

namespace PreviewHelper.PreviewPatches;

public class DefinitionManagerPatches
{
    public static bool GetNewsConfiguration(ref NewsConfiguration __result)
    {
        __result = new NewsConfiguration();
        return false;
    }

    public static void Apply(Harmony harmony)
    {
        var original = AccessTools.Method(typeof(DefinitionManager), "GetConfiguration").MakeGenericMethod(typeof(NewsConfiguration));
        var prefix = AccessTools.Method(typeof(DefinitionManagerPatches), nameof(GetNewsConfiguration));
        harmony.Patch(original, prefix: new HarmonyMethod(prefix));
    }
}

