using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Diagnostics;
using Avalonia.Input;
using Avalonia.VisualTree;
using HarmonyLib;
using Keen.VRage.Core;
using Keen.VRage.Library.Diagnostics;
using Keen.VRage.Library.Utils;
using Keen.VRage.UI.Screens;

namespace UI_Revamp.Patches;

[HarmonyPatch]
public class MainWindowPatches
{
    const string MainWindowTypeName = "Keen.VRage.UI.AvaloniaInterface.Main.MainWindow";

    public static MethodBase TargetMethod()
    {
        return AccessTools.Constructor(AccessTools.TypeByName(MainWindowTypeName), new[] { typeof(ScreenManager) });
    }

    [HarmonyPostfix]
    // ReSharper disable once InconsistentNaming
    public static void Postfix(TopLevel __instance)
    {
        Plugin.MainWindow = __instance;
        Plugin.UpdateHudResources();
        DarkModeStyleController.Reload();
        CompactFlightHudStyleController.Reload();
        Plugin.ApplyUiScale(Plugin.Settings.UiScale);

#if DEBUG
        VisualTreeDump.SetRoot(__instance);
        DumpHotkeyInputPatch.Install();
#endif
        __instance.AttachDevTools(new KeyGesture(Key.F12, KeyModifiers.Shift));
#if DEBUG
        __instance.KeyDown += (_, e) =>
        {
            Log.Default.Info($"[{Plugin.PluginId}] KeyDown: {e.Key}");
            
            if (e.Key == Key.F10)
            {
                Log.Default.Error($"[{Plugin.PluginId}] Starting to dump visual tree");
                e.Handled = true;
                VisualTreeDump.Write(__instance);
                return;
            }

            if (e.Key != Key.F12 || e.KeyModifiers != (KeyModifiers.Control | KeyModifiers.Shift))
            {
                return;
            }

            e.Handled = true;
            using (DevToolsOpenNativeWindowPatch.ForceVrageWindow())
            {
                DevToolsOpenNativeWindowPatch.Open(__instance, new DevToolsOptions());
            }
        };
#endif
    }
}

#if DEBUG
internal static class VisualTreeDump
{
    const int MaxVisualTreeDepth = 256;

    static TopLevel? _root;

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        MaxDepth = MaxVisualTreeDepth * 4,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static void SetRoot(TopLevel root)
    {
        _root = root;
    }

    public static void WriteCurrent()
    {
        if (_root == null)
        {
            Log.Default.Error($"[{Plugin.PluginId}] Failed to dump visual tree: no main window has been captured.");
            return;
        }

        Write(_root);
    }

    public static void Write(TopLevel root)
    {
        try
        {
            var dumpDirectory = Plugin.OptionsDirectory;

            Directory.CreateDirectory(dumpDirectory);

            var dumpPath = Path.Combine(
                dumpDirectory,
                $"UI-Revamp.visual-tree-{DateTime.Now:yyyyMMdd-HHmmss}.json");

            var visited = new HashSet<Visual>(ReferenceEqualityComparer.Instance);
            File.WriteAllText(dumpPath, JsonSerializer.Serialize(CreateNode(root, visited, 0), JsonOptions));
            Log.Default.Info($"[{Plugin.PluginId}] Visual tree dumped to {dumpPath}");
        }
        catch (Exception e)
        {
            Log.Default.Error($"[{Plugin.PluginId}] Failed to dump visual tree: {e}");
        }
    }

    static VisualTreeNode CreateNode(Visual visual, HashSet<Visual> visited, int depth)
    {
        var node = CreateShallowNode(visual);

        if (depth >= MaxVisualTreeDepth)
        {
            node.Truncated = $"Max visual tree depth {MaxVisualTreeDepth} reached.";
            return node;
        }

        if (!visited.Add(visual))
        {
            node.Truncated = "Cycle detected.";
            return node;
        }

        try
        {
            var children = visual.GetVisualChildren()
                .Select(child => CreateNode(child, visited, depth + 1))
                .ToArray();

            node.Children = children.Length > 0 ? children : null;
            return node;
        }
        finally
        {
            visited.Remove(visual);
        }
    }

    static VisualTreeNode CreateShallowNode(Visual visual)
    {
        return new VisualTreeNode
        {
            Type = visual.GetType().Name,
            Class = visual is StyledElement styledElement && styledElement.Classes.Count > 0
                ? string.Join(" ", styledElement.Classes)
                : null,
            Name = visual is Control control && !string.IsNullOrWhiteSpace(control.Name)
                ? control.Name
                : null
        };
    }

    sealed class VisualTreeNode
    {
        [JsonPropertyName("type")]
        public required string Type { get; init; }

        [JsonPropertyName("class")]
        public string? Class { get; init; }

        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("children")]
        public VisualTreeNode[]? Children { get; set; }

        [JsonPropertyName("truncated")]
        public string? Truncated { get; set; }
    }
}

[HarmonyPatch]
public class DevToolsOpenNativeWindowPatch
{
    static int _forceVrageWindowDepth;

    public static MethodBase TargetMethod()
    {
        return AccessTools.Method(
            AccessTools.TypeByName("Avalonia.Diagnostics.DevTools"),
            "Open",
            new[] { typeof(TopLevel), typeof(DevToolsOptions) });
    }

    // ReSharper disable once InconsistentNaming
    public static void Prefix(out IDisposable __state)
    {
        if (Volatile.Read(ref _forceVrageWindowDepth) > 0)
        {
            __state = NoopDisposable.Instance;
            return;
        }

        __state = NativeDevToolsWindowContext.Enter();
    }

    // ReSharper disable once InconsistentNaming
    public static Exception? Finalizer(IDisposable __state, Exception? __exception)
    {
        __state.Dispose();
        NativeDevToolsWindowContext.RestoreGameAvaloniaServicesAfterOpen();
        return __exception;
    }

    public static IDisposable ForceVrageWindow()
    {
        Interlocked.Increment(ref _forceVrageWindowDepth);
        return new ForceVrageWindowLease();
    }

    public static void Open(TopLevel root, DevToolsOptions options)
    {
        TargetMethod().Invoke(null, new object[] { root, options });
    }

    sealed class ForceVrageWindowLease : IDisposable
    {
        int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                Interlocked.Decrement(ref _forceVrageWindowDepth);
            }
        }
    }

    sealed class NoopDisposable : IDisposable
    {
        public static readonly NoopDisposable Instance = new();

        NoopDisposable()
        {
        }

        public void Dispose()
        {
        }
    }
}
#endif
