using System;
using System.Reflection;
using Avalonia.Platform;
using HarmonyLib;

namespace UI_Revamp.Patches;

[HarmonyPatch(typeof(StandardAssetLoader))]
[HarmonyPatch(nameof(StandardAssetLoader.GetAssembly))]
public class StandardAssetLoaderPatches
{
    public static void Postfix(Uri uri, Uri? baseUri, ref Assembly? result)
    {
        if (result == null && uri.Authority == "ui-revamp") // SE2 uses an old version of AssetLoader that can't really load files from plugins,
                                                                  // manually redirect all assets from "se2-ui-revamp" to be loaded from current assembly
            result = Assembly.GetExecutingAssembly();
    }
}