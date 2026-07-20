using Keen.VRage.Library.Diagnostics;

namespace UI_Revamp.CurvedHud;

/// <summary>
/// Production curved-HUD configuration.
///
/// The earlier diagnostic environment switches are intentionally collapsed to
/// the working runtime settings:
///   - persistent offscreen batches
///   - immediate composition in the main view
///   - no visible probes or capture readbacks
///   - curvature shader enabled with out-of-range pixels discarded
/// </summary>
internal static class CurvedHudDiagnostics
{
    internal enum ShaderMode
    {
        StockPso,
        PassThrough,
        WarpClamp,
        WarpDiscard
    }

    internal enum BatchMode
    {
        Persistent,
        Immediate
    }

    internal enum ProbeMode
    {
        None,
        MainView,
        Offscreen,
        All
    }

    internal static ShaderMode Mode => Constants.Diagnostics.ShaderMode;
    internal static BatchMode OffscreenBatchMode =>
        Constants.Diagnostics.OffscreenBatchMode;
    internal static ProbeMode Probes => Constants.Diagnostics.ProbeMode;

    internal static bool UseImmediateOffscreenBatch =>
        Constants.Diagnostics.UseImmediateOffscreenBatch;

    // Create the main-view composition sprite immediately. The offscreen
    // render path is now started only after a world/session is loaded, so the
    // menu-only race that motivated the gated diagnostic path is no longer the
    // default behavior.
    internal static bool WaitForFirstOffscreenSubmit =>
        Constants.Diagnostics.WaitForFirstOffscreenSubmit;

    internal static bool DrawMainViewProbe =>
        Constants.Diagnostics.DrawMainViewProbe;
    internal static bool DrawOffscreenProbe =>
        Constants.Diagnostics.DrawOffscreenProbe;
    internal static bool CaptureOffscreenTarget =>
        Constants.Diagnostics.CaptureOffscreenTarget;

    internal static void LogConfiguration()
    {
        Log.Default.Info(
            $"[{Plugin.PluginId}] Curved HUD configured: shader=warp-discard, batch=persistent, composition=early.");
    }

    internal static string FormatMode(ShaderMode mode)
    {
        return mode switch
        {
            ShaderMode.StockPso => "stock-pso",
            ShaderMode.PassThrough => "pass-through",
            ShaderMode.WarpClamp => "warp-clamp",
            ShaderMode.WarpDiscard => "warp-discard",
            _ => mode.ToString()
        };
    }
}
