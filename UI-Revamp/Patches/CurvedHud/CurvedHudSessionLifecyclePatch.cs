using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Keen.Game2.Client.UI.Library;
using Keen.VRage.Library.Diagnostics;
using UI_Revamp.CurvedHud;

namespace UI_Revamp.Patches.CurvedHud;

/// <summary>
/// Starts the curved HUD only after SE2 has completed a successful transition
/// into a world. The renderer services offscreen UI targets from the active 3D
/// scene-draw path, which is not executed while the game is sitting in the
/// startup/main-menu scene.
/// </summary>
[HarmonyPatch]
internal static class CurvedHudWorldLoadedPatch
{
    static MethodBase TargetMethod()
    {
        return FindExplicitInterfaceMethod(
            typeof(SharedUIComponent),
            "OnTransitionToGameEnd",
            parameterCount: 1);
    }

    // ReSharper disable once InconsistentNaming
    static void Postfix(object[] __args)
    {
        Exception? loadingError = __args.Length > 0 ? __args[0] as Exception : null;
        if (loadingError != null)
        {
            Log.Default.Warning(
                $"[{Plugin.PluginId}] World loading failed; curved HUD startup remains deferred.");
            CurvedHudController.Stop();
            return;
        }

        CurvedHudController.StartAfterSessionLoaded();
    }

    internal static MethodBase FindExplicitInterfaceMethod(
        Type declaringType,
        string methodName,
        int parameterCount)
    {
        return AccessTools.GetDeclaredMethods(declaringType)
                   .SingleOrDefault(method =>
                       method.Name.EndsWith('.' + methodName, StringComparison.Ordinal) &&
                       method.GetParameters().Length == parameterCount)
               ?? throw new MissingMethodException(
                   declaringType.FullName,
                   $"explicit interface method *.{methodName}");
    }
}

/// <summary>
/// Tears down the curved HUD before SE2 disables the 3D scene and transitions
/// back to the main menu. A later world load will create a fresh target/window.
/// </summary>
[HarmonyPatch]
internal static class CurvedHudReturnToMenuPatch
{
    static MethodBase TargetMethod()
    {
        return CurvedHudWorldLoadedPatch.FindExplicitInterfaceMethod(
            typeof(SharedUIComponent),
            "OnTransitionToMainMenuBegin",
            parameterCount: 0);
    }

    static void Prefix()
    {
        CurvedHudController.Stop();
    }
}

/// <summary>
/// Backup teardown point that runs before the transition listener list opens
/// the unloading screen.
/// </summary>
[HarmonyPatch]
internal static class CurvedHudGameAppReturnToMenuPatch
{
    static MethodBase TargetMethod()
    {
        Type gameAppType = AccessTools.TypeByName("Keen.Game2.GameAppComponent")
                           ?? throw new MissingMemberException(
                               "Keen.Game2.GameAppComponent");

        return AccessTools.Method(gameAppType, "TransitionToMainMenu")
               ?? throw new MissingMethodException(
                   gameAppType.FullName,
                   "TransitionToMainMenu");
    }

    static void Prefix()
    {
        CurvedHudController.Stop();
    }
}
