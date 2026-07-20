using Avalonia;
using System;
#if !IS_DESIGN
using System.Reflection;
using System.Runtime.InteropServices;
using HarmonyLib;
using PreviewHelper.PreviewPatches;
#endif

namespace PreviewHelper;

internal sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
    {
#if !IS_DESIGN
        var harmony = new Harmony("PreviewPatches");

        var assembly = Assembly.GetExecutingAssembly();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            harmony.PatchAll(assembly);
            ManualPatches.ApplyPatch(harmony);
        }
#endif

        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
    }
}
