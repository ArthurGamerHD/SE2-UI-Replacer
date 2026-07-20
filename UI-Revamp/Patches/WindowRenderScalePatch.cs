using System.Reflection;
using HarmonyLib;
using Keen.VRage.Library.Mathematics;

namespace UI_Revamp.Patches;

[HarmonyPatch]
public static class PlatformDisplayInformationDesignResolutionPatch
{
    const string PlatformDisplayInformationTypeName = "Keen.VRage.UI.AvaloniaInterface.Main.PlatformDisplayInformation";

    public static MethodBase TargetMethod()
    {
        return AccessTools.PropertyGetter(AccessTools.TypeByName(PlatformDisplayInformationTypeName), "DesignResolution");
    }

    [HarmonyPostfix]
    // ReSharper disable once InconsistentNaming
    public static void Postfix(ref Vector2I __result)
    {
        var scale = Plugin.AppliedUiScale;
        __result = new Vector2I(
            MathHelper.RoundToInt(__result.X / scale),
            MathHelper.RoundToInt(__result.Y / scale));
    }
}

[HarmonyPatch]
public static class PlatformDisplayInformationSizeRatioPatch
{
    const string PlatformDisplayInformationTypeName = "Keen.VRage.UI.AvaloniaInterface.Main.PlatformDisplayInformation";

    public static MethodBase TargetMethod()
    {
        return AccessTools.PropertyGetter(AccessTools.TypeByName(PlatformDisplayInformationTypeName), "SizeRatio");
    }

    [HarmonyPostfix]
    // ReSharper disable once InconsistentNaming
    public static void Postfix(ref double __result)
    {
        __result *= Plugin.AppliedUiScale;
    }
}
