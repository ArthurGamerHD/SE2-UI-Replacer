using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HarmonyLib;
using Keen.Game2.Client.UI.Menu.Options;
using Keen.VRage.Library.Diagnostics;
using UI_Revamp.Screens.Options;

namespace UI_Revamp.Patches;

[HarmonyPatch]
public static class OptionsGuiViewPatch
{
    private const int PatchAttempts = 8;
    private const string GuiViewTypeName = "Keen.Game2.Client.UI.Menu.Options.GUIView";
    private const string ScrollableGuiClass = "ui-revamp-scrollable-gui";
    private static readonly FuncControlTemplate<ContentControl> ScrollableGuiTemplate = new((control, _) => new ScrollViewer
    {
        HorizontalAlignment = HorizontalAlignment.Stretch,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        Content = new ContentPresenter
        {
            Name = "PART_ContentPresenter",
            Content = control.Content,
            ContentTemplate = control.ContentTemplate,
            HorizontalContentAlignment = control.HorizontalContentAlignment,
            VerticalContentAlignment = control.VerticalContentAlignment
        }
    });

    public static MethodBase[] TargetMethods()
    {
        return new MethodBase[]
        {
            AccessTools.Constructor(typeof(OptionsScreen), Type.EmptyTypes),
            AccessTools.Method(typeof(OptionsScreen), "PART_TabControl_OnSelectionChanged")
        };
    }

    [HarmonyPostfix]
    public static void Postfix(OptionsScreen __instance)
    {
        try
        {
            SchedulePatch(__instance, PatchAttempts);
        }
        catch (Exception e)
        {
            Log.Default.Error($"[{Plugin.PluginId}] Failed to schedule GUI settings patch: {e}");
        }
    }

    private static void SchedulePatch(OptionsScreen screen, int attemptsLeft)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (PatchGuiView(screen, attemptsLeft))
            {
                return;
            }

            if (attemptsLeft > 1)
            {
                Task.Delay(100).ContinueWith(_ => SchedulePatch(screen, attemptsLeft - 1));
            }
        });
    }

    private static bool PatchGuiView(OptionsScreen screen, int attemptsLeft)
    {
        try
        {
            var guiViews = screen.GetVisualDescendants()
                .Where(visual => visual.GetType().FullName == GuiViewTypeName)
                .OfType<ContentControl>()
                .ToArray();

            foreach (var guiView in guiViews)
            {
                if (!EnsureScrollableGuiTemplate(guiView))
                {
                    Log.Default.Error($"[{Plugin.PluginId}] Found GUIView but content is {guiView.Content?.GetType().FullName ?? "null"}.");
                    continue;
                }

                var stackPanel = GetGuiContentStackPanel(guiView);
                if (stackPanel == null)
                {
                    Log.Default.Error($"[{Plugin.PluginId}] Found GUIView but scroll content is {guiView.Content?.GetType().FullName ?? "null"}.");
                    continue;
                }

                if (stackPanel.Children.OfType<CustomGuiSettingsSection>().Any())
                {
                    return true;
                }

                stackPanel.Children.Add(new CustomGuiSettingsSection());
                Log.Default.Info($"[{Plugin.PluginId}] Added custom controls section to GUI settings. GUIView children: {stackPanel.Children.Count}");
                return true;
            }

            if (attemptsLeft <= 1)
            {
                var descendants = screen.GetVisualDescendants().ToArray();
                var interestingTypes = descendants
                    .Select(visual => visual.GetType().FullName)
                    .Where(name => name != null && (name.Contains("Options") || name.Contains("GUI") || name.Contains("DescriptionControl")))
                    .Distinct()
                    .Take(12);

                Log.Default.Info($"[{Plugin.PluginId}] GUI settings view not found. Visual descendants: {descendants.Length}. Interesting types: {string.Join(", ", interestingTypes)}");
            }

            return false;
        }
        catch (Exception e)
        {
            Log.Default.Error($"[{Plugin.PluginId}] Failed to patch GUI settings: {e}");
            return true;
        }
    }

    private static bool EnsureScrollableGuiTemplate(ContentControl guiView)
    {
        var stackPanel = GetGuiContentStackPanel(guiView);
        if (stackPanel == null)
        {
            return false;
        }

        stackPanel.Margin = new Avalonia.Thickness(0, 0, 11, 0);

        if (!guiView.Classes.Contains(ScrollableGuiClass))
        {
            guiView.Classes.Add(ScrollableGuiClass);
        }

        if (!ReferenceEquals(guiView.Template, ScrollableGuiTemplate))
        {
            guiView.Template = ScrollableGuiTemplate;
            Log.Default.Info($"[{Plugin.PluginId}] Applied scrollable GUI settings template.");
        }

        return true;
    }

    private static StackPanel? GetGuiContentStackPanel(ContentControl guiView)
    {
        return guiView.Content switch
        {
            StackPanel stackPanel => stackPanel,
            ScrollViewer { Content: StackPanel stackPanel } => stackPanel,
            _ => null
        };
    }
}
