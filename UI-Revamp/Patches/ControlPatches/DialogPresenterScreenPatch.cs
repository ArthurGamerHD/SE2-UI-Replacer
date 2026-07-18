using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using HarmonyLib;
using Keen.Game2.Client.UI.Library.Dialogs;
using Keen.VRage.Library.Diagnostics;
using Keen.VRage.UI.Shared.Controls.BeveledBorder;

namespace UI_Revamp.Patches.ControlPatches;

[HarmonyPatch(typeof(DialogPresenterScreen))]
[HarmonyPatch(MethodType.Constructor)]
public static class DialogPresenterScreenPatch
{
    private static readonly Uri PaletteUri = new("avares://UI-Revamp/Styles/DarkMode/Palette.axaml");
    private static ResourceDictionary? PaletteResources;

    [HarmonyPostfix]
    public static void Postfix(DialogPresenterScreen __instance)
    {
        if (!Plugin.Settings.UseDarkMode)
        {
            return;
        }

        try
        {
            var shellBorder = (__instance.Content as Grid)?.Children.OfType<BeveledBorder>().FirstOrDefault();
            if (shellBorder == null)
            {
                Log.Default.Warning($"[{Plugin.PluginId}] DialogPresenterScreen dark style patch found no shell border.");
                return;
            }

            shellBorder.Background = ResolveBrush("Border-Background", "#161616");
            shellBorder.BorderBrush = ResolveBrush("Decorative-Border-Brush", "#535353");
            Log.Default.Info($"[{Plugin.PluginId}] Patched DialogPresenterScreen shell background.");
        }
        catch (Exception e)
        {
            Log.Default.Error($"[{Plugin.PluginId}] Failed to patch DialogPresenterScreen dark style: {e}");
        }
    }

    private static IBrush ResolveBrush(string key, string fallbackColor)
    {
        if (TryFindPaletteResource(key, out var value) && value is IBrush brush)
        {
            return brush;
        }

        Log.Default.Warning($"[{Plugin.PluginId}] Failed to resolve dark palette brush '{key}', using fallback {fallbackColor}.");
        return new SolidColorBrush(Color.Parse(fallbackColor));
    }

    private static bool TryFindPaletteResource(string key, out object? value)
    {
        value = null;

        try
        {
            PaletteResources ??= (ResourceDictionary)AvaloniaXamlLoader.Load(PaletteUri, null);
            return PaletteResources.TryGetResource(key, ThemeVariant.Dark, out value)
                   || PaletteResources.TryGetResource(key, null, out value);
        }
        catch (Exception e)
        {
            Log.Default.Error($"[{Plugin.PluginId}] Failed to load dark palette resources: {e}");
            return false;
        }
    }
}
