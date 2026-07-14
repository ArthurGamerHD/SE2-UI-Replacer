using System;
using System.Reflection;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Diagnostics;
using Avalonia.Input;
using HarmonyLib;
using Keen.VRage.UI.Screens;

namespace UI_Revamp.Patches;

[HarmonyPatch]
public class MainWindowPatches
{
    private const string MainWindowTypeName = "Keen.VRage.UI.AvaloniaInterface.Main.MainWindow";

    public static MethodBase TargetMethod()
    {
        return AccessTools.Constructor(AccessTools.TypeByName(MainWindowTypeName), new[] { typeof(ScreenManager) });
    }

    [HarmonyPostfix]
    public static void Postfix(TopLevel __instance)
    {
        __instance.AttachDevTools(new KeyGesture(Key.F12, KeyModifiers.Shift));
#if DEBUG
        __instance.KeyDown += (_, e) =>
        {
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
[HarmonyPatch]
public class DevToolsOpenNativeWindowPatch
{
    private static int _forceVrageWindowDepth;

    public static MethodBase TargetMethod()
    {
        return AccessTools.Method(
            AccessTools.TypeByName("Avalonia.Diagnostics.DevTools"),
            "Open",
            new[] { typeof(TopLevel), typeof(DevToolsOptions) });
    }

    public static void Prefix(out IDisposable __state)
    {
        if (Volatile.Read(ref _forceVrageWindowDepth) > 0)
        {
            __state = NoopDisposable.Instance;
            return;
        }

        __state = NativeDevToolsWindowContext.Enter();
    }

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

    private sealed class ForceVrageWindowLease : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                Interlocked.Decrement(ref _forceVrageWindowDepth);
            }
        }
    }

    private sealed class NoopDisposable : IDisposable
    {
        public static readonly NoopDisposable Instance = new();

        private NoopDisposable()
        {
        }

        public void Dispose()
        {
        }
    }
}
#endif
