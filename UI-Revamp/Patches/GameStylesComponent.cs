using System;
using System.Reflection;
using HarmonyLib;
using Keen.Game2.Client.UI.Library;
using Keen.VRage.Library.Diagnostics;
using Keen.VRage.Library.Memory;
using SE2PluginLoader;

namespace UI_Revamp.Patches;

//[HarmonyPatch(typeof(GameStylesComponent))]
[HarmonyPatch("Keen.Game2.Client.UI.Library.GameStylesComponent, Game2.Client", "GetCoreStyles")]
public class GameStylesComponentGetCoreStylesPatches
{

    [HarmonyPostfix]
    public static void Postfix(ref BufferReference<Uri> pathOut)
    {
        Assembly.LoadFile(Assembly.GetExecutingAssembly().Location);
        
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
        
        Log.Default.Info($"[{Plugin.PluginId}] Override In-World Styles");
        pathOut.Add(new Uri("avares://UI-Revamp/Styles/WorldStyles.axaml"));
    }
}