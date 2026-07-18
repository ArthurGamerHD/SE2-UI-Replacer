using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Avalonia.Platform;
using HarmonyLib;
using Keen.VRage.Library.Diagnostics;

namespace UI_Revamp.Patches;

[HarmonyPatch(typeof(StandardAssetLoader))]
[HarmonyPatch(nameof(StandardAssetLoader.GetAssembly))]
public class StandardAssetLoaderPatches
{
    private static readonly Uri GameSharedResourcesUri = new("avares://Game2.Client/UI/Library/Styles/SharedResources.axaml");
    private static readonly Uri DarkModeSharedResourcesUri = new("avares://UI-Revamp/Styles/DarkMode/GameSharedResources.axaml");
    private static bool _loggedRedirectDisabled;
    private static bool _loggedGetAssemblyRedirect;
    private static bool _loggedOpenRedirect;
    private static readonly HashSet<string> LoggedSharedResourceUris = new();

    public static bool Prefix(Uri uri, Uri? baseUri, ref Assembly? __result)
    {
        LogSharedResourcesUri(uri, baseUri, nameof(StandardAssetLoader.GetAssembly));

        if (ShouldRedirect(uri, baseUri))
        {
            if (!_loggedGetAssemblyRedirect)
            {
                _loggedGetAssemblyRedirect = true;
                Log.Default.Info($"[{Plugin.PluginId}] Redirecting compiled XAML lookup for {GameSharedResourcesUri} to runtime asset load.");
            }

            __result = null;
            return false;
        }

        return true;
    }

    public static void Postfix(Uri uri, Uri? baseUri, ref Assembly? __result)
    {
        if (__result == null && string.Equals(uri.Authority, "UI-Revamp", StringComparison.OrdinalIgnoreCase))
        {
            // SE2 uses an old AssetLoader that cannot load files from plugins,
            // so redirect plugin assets to this assembly.
            __result = Assembly.GetExecutingAssembly();
        }
    }

    internal static bool ShouldRedirect(Uri uri, Uri? baseUri)
    {
        return false;
    }

    internal static Uri Normalize(Uri uri, Uri? baseUri)
    {
        return uri.IsAbsoluteUri || baseUri == null ? uri : new Uri(baseUri, uri);
    }

    internal static Uri Redirect(Uri uri, Uri? baseUri)
    {
        if (!ShouldRedirect(uri, baseUri))
        {
            return uri;
        }

        LogRedirectOnce();
        return DarkModeSharedResourcesUri;
    }

    internal static void LogRedirectOnce()
    {
        if (_loggedOpenRedirect)
        {
            return;
        }

        _loggedOpenRedirect = true;
        Log.Default.Info($"[{Plugin.PluginId}] Redirecting {GameSharedResourcesUri} to {DarkModeSharedResourcesUri}.");
    }

    internal static void LogSharedResourcesUri(Uri uri, Uri? baseUri, string method)
    {
        var normalizedUri = Normalize(uri, baseUri);
        if (!normalizedUri.ToString().Contains("SharedResources.axaml", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var key = $"{method}:{normalizedUri}";
        if (!LoggedSharedResourceUris.Add(key))
        {
            return;
        }

        Log.Default.Info($"[{Plugin.PluginId}] Asset loader saw {method} for {normalizedUri}. Dark mode: {Plugin.Settings.UseDarkMode}.");
    }
}

[HarmonyPatch(typeof(StandardAssetLoader))]
[HarmonyPatch(nameof(StandardAssetLoader.Exists))]
public class StandardAssetLoaderExistsPatches
{
    public static void Prefix(ref Uri uri, Uri? baseUri)
    {
        StandardAssetLoaderPatches.LogSharedResourcesUri(uri, baseUri, nameof(StandardAssetLoader.Exists));
        uri = StandardAssetLoaderPatches.Redirect(uri, baseUri);
    }
}

[HarmonyPatch(typeof(StandardAssetLoader))]
[HarmonyPatch(nameof(StandardAssetLoader.Open))]
public class StandardAssetLoaderOpenPatches
{
    public static void Prefix(ref Uri uri, Uri? baseUri)
    {
        StandardAssetLoaderPatches.LogSharedResourcesUri(uri, baseUri, nameof(StandardAssetLoader.Open));
        uri = StandardAssetLoaderPatches.Redirect(uri, baseUri);
    }
}

[HarmonyPatch(typeof(StandardAssetLoader))]
[HarmonyPatch(nameof(StandardAssetLoader.OpenAndGetAssembly))]
public class StandardAssetLoaderOpenAndGetAssemblyPatches
{
    public static bool Prefix(StandardAssetLoader __instance, Uri uri, Uri? baseUri, ref (Stream stream, Assembly assembly) __result)
    {
        StandardAssetLoaderPatches.LogSharedResourcesUri(uri, baseUri, nameof(StandardAssetLoader.OpenAndGetAssembly));

        if (!StandardAssetLoaderPatches.ShouldRedirect(uri, baseUri))
        {
            return true;
        }

        StandardAssetLoaderPatches.LogRedirectOnce();
        __result = __instance.OpenAndGetAssembly(new Uri("avares://UI-Revamp/Styles/DarkMode/GameSharedResources.axaml"), null);
        return false;
    }
}
