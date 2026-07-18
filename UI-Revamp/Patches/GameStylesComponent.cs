using System;
using System.Reflection;
using HarmonyLib;
using Keen.Game2.Client.UI.Library;
using Keen.VRage.Library.Diagnostics;
using Keen.VRage.Library.Memory;

namespace UI_Revamp.Patches;

//[HarmonyPatch(typeof(GameStylesComponent))]
[HarmonyPatch("Keen.Game2.Client.UI.Library.GameStylesComponent, Game2.Client", "GetCoreStyles")]
public class GameStylesComponentGetCoreStylesPatches
{
    private static readonly Uri GameSharedStyles = new("avares://Game2.Client/UI/Library/Styles/SharedStyles.axaml");
    private static readonly Uri DarkModeSharedStyles = new("avares://UI-Revamp/Styles/DarkMode/GameSharedStyles.axaml");

    [HarmonyPostfix]
    public static void Postfix(ref BufferReference<Uri> pathOut)
    {
        Assembly.LoadFile(Assembly.GetExecutingAssembly().Location);

        Log.Default.Info($"[{Plugin.PluginId}] Game core styles registering. Dark mode: {Plugin.Settings.UseDarkMode}.");
        if (Plugin.Settings.UseDarkMode)
        {
            for (var i = 0; i < pathOut.Count; i++)
            {
                if (pathOut[i] != GameSharedStyles)
                {
                    continue;
                }

                pathOut[i] = DarkModeSharedStyles;
                Log.Default.Info($"[{Plugin.PluginId}] Replaced Game2 shared styles with dark shared styles: {DarkModeSharedStyles}");
                break;
            }
        }

        Log.Default.Info($"[{Plugin.PluginId}] Override Global Styles");
        pathOut.Add(new Uri("avares://UI-Revamp/Styles/CoreStyles.axaml"));
    }
}

//[HarmonyPatch(typeof(GameStylesComponent))]
[HarmonyPatch("Keen.Game2.Client.UI.Library.GameStylesComponent, Game2.Client", "GetInWorldStyles")]
public class GameStylesComponentGetInWorldStylesPatches
{
    [HarmonyPostfix]
    public static void Postfix(ref BufferReference<Uri> pathOut)
    {
        Assembly.LoadFile(Assembly.GetExecutingAssembly().Location);

        Log.Default.Info($"[{Plugin.PluginId}] Game in-world styles registering. Dark mode: {Plugin.Settings.UseDarkMode}.");
        Log.Default.Info($"[{Plugin.PluginId}] Override In-World Styles");
        pathOut.Add(new Uri("avares://UI-Revamp/Styles/WorldStyles.axaml"));
    }
}
