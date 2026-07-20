using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Keen.VRage.Library.Diagnostics;
using Keen.VRage.Library.Utils;
using Keen.VRage.UI.Screens;

namespace UI_Revamp.Patches;

[HarmonyPatch(typeof(ScreenManager))]
[HarmonyPatch(nameof(ScreenManager.CreateAdvancedScreen))]
public class ScreenManagerPatches
{
    static Dictionary<string, Type>? _typeCache;
    static readonly HashSet<SubclassOf<ScreenView>> ReplacedTypes = new();
    
    [HarmonyPrefix]
    public static bool Prefix(
        ref SubclassOf<ScreenView> screenType)
    {
        _typeCache ??= Assembly.GetExecutingAssembly().GetTypes().Where(t => t.IsClass && typeof(ScreenView).IsAssignableFrom(t)).ToDictionary(a => a.Name);

        if (_typeCache.TryGetValue(screenType.Type.Name, out var type))
        {
            if (ReplacedTypes.Add(screenType)) 
                Log.Default.Info($"Replaced {screenType.Type.FullName} with {type.FullName}");
                
            screenType = type;
        }

        return true;
    }

}
