using System.Reflection;
using HarmonyLib;
using Keen.VRage.Library.Mathematics;

namespace UI_Revamp.Patches;

[HarmonyPatch]
public static class PlatformDisplayInformationDesignResolutionPatch
{
    private const string PlatformDisplayInformationTypeName = "Keen.VRage.UI.AvaloniaInterface.Main.PlatformDisplayInformation";

    public static MethodBase TargetMethod()
    {
        return AccessTools.PropertyGetter(AccessTools.TypeByName(PlatformDisplayInformationTypeName), "DesignResolution");
    }

    [HarmonyPostfix]
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
    private const string PlatformDisplayInformationTypeName = "Keen.VRage.UI.AvaloniaInterface.Main.PlatformDisplayInformation";

    public static MethodBase TargetMethod()
    {
        return AccessTools.PropertyGetter(AccessTools.TypeByName(PlatformDisplayInformationTypeName), "SizeRatio");
    }

    [HarmonyPostfix]
    public static void Postfix(ref double __result)
    {
        __result *= Plugin.AppliedUiScale;
    }
}
