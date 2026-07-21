using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HarmonyLib;
using Keen.VRage.Library.Diagnostics;
using Keen.VRage.Library.Mathematics;
using Keen.VRage.Library.Utils;
using Keen.VRage.Render.Contracts;
using Keen.VRage.Render.EngineComponents;
using UI_Revamp.Configuration;

namespace UI_Revamp.CurvedHud;

internal static class CurvedHudController
{
    readonly record struct SurfaceMetrics(
        Size LogicalSize,
        Vector2I TextureResolution,
        Vector2I MainViewportResolution,
        double RenderScaling);

    internal static object? PlatformWindow { get; private set; }
    internal static bool HasValidTarget { get; private set; }
    internal static OffscreenRenderTarget Target { get; private set; }
    internal static ResourceHandle CompositionTextureHandle => Target.TextureHandle;
    internal static bool CapturingPlatformWindow { get; private set; }

    static CurvedHudWindow? _window;
    static PersistentDrawBatch? _compositionBatch;
    static PersistentDrawBatch? _mainViewProbeBatch;
    static PersistentDrawBatch? _offscreenProbeBatch;
    static DispatcherTimer? _compositionRetryTimer;
    static DispatcherTimer? _captureTimer;
    static int _offscreenSubmitSeen;
    static int _offscreenRenderSeen;
    static int _loggedWaitingForSubmit;
    static int _loggedViewModelFailure;
    static bool _compositionStartRequested;
    static bool _captureSubscribed;
    static int _sessionActive;
    static CurvedHudViewModel? _viewModel;
    static Size _logicalWindowSize;
    static Vector2I _textureResolution;
    static Vector2I _mainViewportResolution;
    static double _renderScaling;
    static double _wobbleOffsetX;
    static double _wobbleOffsetY;
    static double _wobbleScale = 1.0;
    static uint _submittedWobbleColor;
    static int _loggedRejectedMainWindowCapture;
    static int _flightHudVisible;

    internal static bool IsFlightHudVisible => Volatile.Read(ref _flightHudVisible) != 0;

    internal static void StartAfterSessionLoaded()
    {
        bool firstRequest = Interlocked.Exchange(ref _sessionActive, 1) == 0;
        if (firstRequest)
        {
            Log.Default.Info(
                $"[{Plugin.PluginId}] World-loaded transition completed; curved HUD session is active.");
        }

        Dispatcher.UIThread.Post(() =>
        {
            // A return-to-menu transition may overtake this queued callback.
            if (Volatile.Read(ref _sessionActive) != 0)
            {
                StartOnUiThread();
            }
        }, DispatcherPriority.Loaded);
    }

    internal static void ApplySettings()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            ApplySettingsOnUiThread();
        }
        else
        {
            Dispatcher.UIThread.Post(ApplySettingsOnUiThread, DispatcherPriority.Loaded);
        }
    }

    internal static void Stop()
    {
        Interlocked.Exchange(ref _sessionActive, 0);

        if (Dispatcher.UIThread.CheckAccess())
        {
            StopOnUiThread();
        }
        else
        {
            Dispatcher.UIThread.Post(() => StopOnUiThread(), DispatcherPriority.Loaded);
        }
    }

    internal static void SetWobble(double logicalOffsetX, double logicalOffsetY, double scale)
    {
        if (!double.IsFinite(logicalOffsetX) ||
            !double.IsFinite(logicalOffsetY) ||
            !double.IsFinite(scale) ||
            scale <= 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(scale),
                $"Invalid curved HUD wobble values: x={logicalOffsetX}, y={logicalOffsetY}, scale={scale}.");
        }

        _wobbleOffsetX = logicalOffsetX;
        _wobbleOffsetY = logicalOffsetY;
        _wobbleScale = scale;

        if (!HasValidTarget || _compositionBatch == null)
            return;

        ColorSRGB encoded = EncodeWobbleColor();
        if (encoded.PackedValue == _submittedWobbleColor)
            return;

        CreateCompositionBatch();
    }

    internal static void UiFrame()
    {
        if (Volatile.Read(ref _sessionActive) == 0)
            return;

        if (Dispatcher.UIThread.CheckAccess())
        {
            UpdateViewModelOnUiThread();
        }
        else
        {
            Dispatcher.UIThread.Post(
                UpdateViewModelOnUiThread,
                DispatcherPriority.Background);
        }
    }

    internal static void RefreshForMainWindowMetrics()
    {
        if (Volatile.Read(ref _sessionActive) == 0)
            return;

        if (Dispatcher.UIThread.CheckAccess())
        {
            RefreshForMainWindowMetricsOnUiThread();
        }
        else
        {
            Dispatcher.UIThread.Post(
                RefreshForMainWindowMetricsOnUiThread,
                DispatcherPriority.Loaded);
        }
    }

    internal static void RefreshComposition()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            RefreshCompositionOnUiThread();
        }
        else
        {
            Dispatcher.UIThread.Post(
                RefreshCompositionOnUiThread,
                DispatcherPriority.Loaded);
        }
    }

    static void RefreshForMainWindowMetricsOnUiThread()
    {
        if (Volatile.Read(ref _sessionActive) == 0 ||
            _window == null)
            return;

        SurfaceMetrics metrics = ResolveMainWindowSurfaceMetrics();

        if (metrics.TextureResolution != _textureResolution)
        {
            Log.Default.Info(
                $"[{Plugin.PluginId}] Main UI texture resolution changed from " +
                $"{_textureResolution} to {metrics.TextureResolution}; rebuilding the curved HUD target.");

            StopOnUiThread();
            StartOnUiThread();
            return;
        }

        if (metrics.LogicalSize == _logicalWindowSize &&
            metrics.MainViewportResolution == _mainViewportResolution &&
            Math.Abs(metrics.RenderScaling - _renderScaling) <
            Constants.Layout.RenderScalingEpsilon)
        {
            return;
        }

        _logicalWindowSize = metrics.LogicalSize;
        _mainViewportResolution = metrics.MainViewportResolution;
        _renderScaling = metrics.RenderScaling;
        ApplyPlatformWindowMetrics(metrics);
        if (_compositionBatch != null)
            CreateCompositionBatch();

        Log.Default.Info(
            $"[{Plugin.PluginId}] Curved HUD followed the main Avalonia surface: " +
            $"logical={metrics.LogicalSize}, texture={metrics.TextureResolution}, " +
            $"viewport={metrics.MainViewportResolution}, " +
            $"renderScaling={metrics.RenderScaling:F3}.");
    }

    static void RefreshCompositionOnUiThread()
    {
        if (Volatile.Read(ref _sessionActive) == 0 ||
            _window == null ||
            !HasValidTarget ||
            !Target.IsValid ||
            _compositionBatch == null)
        {
            return;
        }

        CreateCompositionBatch();
    }

    static void StartOnUiThread()
    {
        if (Volatile.Read(ref _sessionActive) == 0 ||
            _window != null)
            return;

        CurvedHudDiagnostics.LogConfiguration();
        Volatile.Write(ref _offscreenSubmitSeen, 0);
        Volatile.Write(ref _offscreenRenderSeen, 0);
        Volatile.Write(ref _loggedWaitingForSubmit, 0);
        Volatile.Write(ref _loggedViewModelFailure, 0);
        _compositionStartRequested = false;
        _submittedWobbleColor = 0;

        SurfaceMetrics metrics = ResolveMainWindowSurfaceMetrics();
        _logicalWindowSize = metrics.LogicalSize;
        _textureResolution = metrics.TextureResolution;
        _mainViewportResolution = metrics.MainViewportResolution;
        _renderScaling = metrics.RenderScaling;

        var renderContracts = RenderEngineComponent.Instance.RenderContracts;
        Target = renderContracts.CreateOffscreenTarget(
            "UIRevamp.CurvedHud.Avalonia",
            _textureResolution);
        HasValidTarget = Target.IsValid;

        if (!HasValidTarget)
        {
            Log.Default.Error(
                $"[{Plugin.PluginId}] Curved HUD could not create its offscreen render target.");
            return;
        }

        _window = new CurvedHudWindow
        {
            Width = _logicalWindowSize.Width,
            Height = _logicalWindowSize.Height
        };
        _viewModel = new CurvedHudViewModel();
        _window.DataContext = _viewModel;

        // Showing the window creates its Avalonia/VRage platform implementation.
        // The DrawingContext patch redirects only this window into Target.
        CapturingPlatformWindow = true;
        try
        {
            _window.Show();
        }
        finally
        {
            CapturingPlatformWindow = false;
        }

        PlatformWindow = GetPlatformImpl(_window);

        if (PlatformWindow == null)
        {
            Log.Default.Error(
                $"[{Plugin.PluginId}] Could not resolve the VRage WindowImpl for CurvedHudWindow after Show().");
            _window.Close();
            _window = null;
            _viewModel = null;
            Target.Dispose();
            HasValidTarget = false;
            return;
        }

        ApplyPlatformWindowMetrics(metrics);
        DetachPlatformWindowFromInput();
        LogPlatformWindowState();
        CreateVisibleProbes();

        _window.InvalidateVisual();
        UpdateViewModelOnUiThread();
        BeginDeferredComposition();
        ScheduleOffscreenCapture();
    }

    static void ApplySettingsOnUiThread()
    {
        if (Volatile.Read(ref _sessionActive) == 0)
            return;

        StartOnUiThread();
        UpdateViewModelOnUiThread();
        NativeFlightHudController.Refresh();
    }

    static void UpdateViewModelOnUiThread()
    {
        CurvedHudWindow? window = _window;
        CurvedHudViewModel? viewModel = _viewModel;
        if (window == null || viewModel == null)
            return;

        try
        {
            viewModel.UpdateFromGame();

            bool showFlightHud = viewModel.ShowFlightHud;
            bool visibilityChanged = SetFlightHudVisible(showFlightHud);
            bool showPlanetHud = viewModel.ShowPlanetHud;
            window.PART_HudPanelHost.IsVisible = showFlightHud;
            window.PART_CenterProgressBars.IsVisible = showFlightHud;
            window.PART_Compass.IsVisible = showPlanetHud;
            window.PART_ArtificialHorizon.IsVisible = showPlanetHud;
            window.PART_SpaceReticle.IsVisible = viewModel.ShowSpaceReticle;
            window.PART_Compass.Bearing = viewModel.Bearing;
            window.PART_Compass.VisibleDegrees = viewModel.CompassVisibleDegrees;
            window.PART_ArtificialHorizon.Pitch = viewModel.Pitch;
            window.PART_ArtificialHorizon.Roll = viewModel.Roll;

            if (visibilityChanged || showFlightHud)
                NativeFlightHudController.Refresh();
        }
        catch (Exception exception)
        {
            if (Interlocked.Exchange(ref _loggedViewModelFailure, 1) == 0)
            {
                Log.Default.Error(
                    $"[{Plugin.PluginId}] Failed to update the curved HUD view model: {exception}");
            }
        }
    }

    static void StopOnUiThread(string reason = "before transition to the main menu.")
    {
        if (SetFlightHudVisible(false))
            NativeFlightHudController.Refresh();

        bool hadResources =
            _window != null ||
            _compositionBatch != null ||
            _mainViewProbeBatch != null ||
            _offscreenProbeBatch != null ||
            HasValidTarget;

        StopCompositionRetry();

        if (_captureTimer != null)
        {
            _captureTimer.Stop();
            _captureTimer.Tick -= OnCaptureTimerTick;
            _captureTimer = null;
        }

        if (_captureSubscribed)
        {
            RenderEngineComponent.Instance.RenderOutputManager.OnScreenshotToMemoryTaken -=
                OnOffscreenScreenshotTaken;
            _captureSubscribed = false;
        }

        // Persistent batch disposal queues deletion while its render target is
        // still registered. Keep the target alive until every batch/window has
        // been released.
        _compositionBatch?.Dispose();
        _compositionBatch = null;
        _mainViewProbeBatch?.Dispose();
        _mainViewProbeBatch = null;
        _offscreenProbeBatch?.Dispose();
        _offscreenProbeBatch = null;

        CurvedHudWindow? window = _window;
        _window = null;
        _viewModel = null;
        if (window != null)
        {
            try
            {
                window.Close();
            }
            catch (Exception exception)
            {
                Log.Default.Warning(
                    $"[{Plugin.PluginId}] Failed to close the curved HUD window cleanly: {exception}");
            }
        }

        PlatformWindow = null;
        CapturingPlatformWindow = false;

        if (HasValidTarget)
        {
            try
            {
                Target.Dispose();
            }
            catch (Exception exception)
            {
                Log.Default.Warning(
                    $"[{Plugin.PluginId}] Failed to dispose the curved HUD offscreen target cleanly: {exception}");
            }
        }

        Target = default;
        HasValidTarget = false;
        InvalidateMainWindowAfterRedirectStop();
        _logicalWindowSize = default;
        _textureResolution = default;
        _mainViewportResolution = default;
        _renderScaling = 0.0;
        _submittedWobbleColor = 0;
        _compositionStartRequested = false;
        Volatile.Write(ref _offscreenSubmitSeen, 0);
        Volatile.Write(ref _offscreenRenderSeen, 0);
        Volatile.Write(ref _loggedWaitingForSubmit, 0);

        if (hadResources)
        {
            Log.Default.Info(
                $"[{Plugin.PluginId}] Curved HUD stopped {reason}");
        }
    }

    static bool SetFlightHudVisible(bool visible)
    {
        return Interlocked.Exchange(ref _flightHudVisible, visible ? 1 : 0) !=
               (visible ? 1 : 0);
    }

    internal static bool TryCapturePlatformWindow(object renderingWindow)
    {
        if (ReferenceEquals(renderingWindow, PlatformWindow))
            return true;

        if (!CapturingPlatformWindow)
            return false;

        // Showing the render-only window can invalidate the real main window,
        // especially while styles are being attached or refreshed. Never infer
        // ownership from a type name here: both windows use the same WindowImpl
        // runtime type, so the broad heuristic can capture MainWindow and route
        // menu/dialog/loading drawing contexts into the offscreen target.
        object? mainPlatformWindow = GetPlatformImpl(Plugin.MainWindow);
        if (mainPlatformWindow != null &&
            ReferenceEquals(renderingWindow, mainPlatformWindow))
        {
            if (Interlocked.Exchange(
                    ref _loggedRejectedMainWindowCapture,
                    1) == 0)
            {
                Log.Default.Warning(
                    $"[{Plugin.PluginId}] Rejected MainWindow platform impl while " +
                    "capturing the curved HUD render window.");
            }

            return false;
        }

        object? expectedPlatformWindow = GetPlatformImpl(_window);
        if (expectedPlatformWindow == null ||
            !ReferenceEquals(renderingWindow, expectedPlatformWindow))
        {
            return false;
        }

        PlatformWindow = expectedPlatformWindow;
        Log.Default.Info(
            $"[{Plugin.PluginId}] Captured the exact CurvedHudWindow platform impl during DrawingContext.Init.");
        return true;
    }

    static object? GetPlatformImpl(TopLevel? window)
    {
        if (window == null)
            return null;

        return typeof(TopLevel)
            .GetProperty(
                "PlatformImpl",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?.GetValue(window);
    }

    static void InvalidateMainWindowAfterRedirectStop()
    {
        TopLevel? mainWindow = Plugin.MainWindow;
        if (mainWindow == null)
            return;

        // A main-window drawing context that was accidentally redirected in a
        // previous build may leave its persistent batch in the offscreen target.
        // Force every visible main-window control to submit fresh main-view
        // batches after PlatformWindow and HasValidTarget have been cleared.
        mainWindow.InvalidateMeasure();
        mainWindow.InvalidateVisual();

        foreach (Control control in mainWindow
                     .GetVisualDescendants()
                     .OfType<Control>())
        {
            control.InvalidateMeasure();
            control.InvalidateVisual();
        }
    }

    internal static void NotifyOffscreenSubmitted(
        string submissionPoint = "DrawingContextImpl.Submit")
    {
        if (Interlocked.Exchange(ref _offscreenSubmitSeen, 1) == 0)
        {
            Log.Default.Info(
                $"[{Plugin.PluginId}] Curved HUD offscreen Avalonia batch " +
                $"was queued by {submissionPoint}().");
        }

        if (!_compositionStartRequested)
            return;

        // Submission only queues the offscreen batch for the render thread.
        // Restart the retry timer so composition is created on a later UI tick.
        if (Dispatcher.UIThread.CheckAccess())
        {
            RestartCompositionRetryTimer();
        }
        else
        {
            Dispatcher.UIThread.Post(
                RestartCompositionRetryTimer,
                DispatcherPriority.Loaded);
        }
    }

    internal static void NotifyOffscreenRenderStarted()
    {
        if (Interlocked.Exchange(ref _offscreenRenderSeen, 1) == 0)
        {
            Log.Default.Info(
                $"[{Plugin.PluginId}] Curved HUD offscreen target reached " +
                "OffscreenUIRenderer.DrawOne.");
        }
    }

    static SurfaceMetrics ResolveMainWindowSurfaceMetrics()
    {
        TopLevel mainWindow = Plugin.MainWindow
            ?? throw new InvalidOperationException(
                "The main Avalonia window is unavailable while sizing the curved HUD.");

        object platformWindow = typeof(TopLevel)
            .GetProperty(
                "PlatformImpl",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?.GetValue(mainWindow)
            ?? throw new InvalidOperationException(
                "The main Avalonia platform window is unavailable while sizing the curved HUD.");

        Type platformType = platformWindow.GetType();
        Size logicalSize = AccessProperty(platformType, platformWindow, "ClientSize") is Size size
            ? size
            : throw new MissingMemberException(
                platformType.FullName,
                "ClientSize");

        double renderScaling = AccessProperty(
                platformType,
                platformWindow,
                "RenderScaling") is double scaling
            ? scaling
            : throw new MissingMemberException(
                platformType.FullName,
                "RenderScaling");

        if (!double.IsFinite(logicalSize.Width) ||
            !double.IsFinite(logicalSize.Height) ||
            logicalSize.Width <= 0.0 ||
            logicalSize.Height <= 0.0)
        {
            throw new InvalidOperationException(
                $"The main Avalonia window reported invalid ClientSize '{logicalSize}'.");
        }

        if (!double.IsFinite(renderScaling) || renderScaling <= 0.0)
        {
            throw new InvalidOperationException(
                $"The main Avalonia window reported invalid RenderScaling '{renderScaling}'.");
        }

        var textureResolution = new Vector2I(
            Math.Max(1, (int)Math.Round(
                logicalSize.Width * renderScaling,
                MidpointRounding.AwayFromZero)),
            Math.Max(1, (int)Math.Round(
                logicalSize.Height * renderScaling,
                MidpointRounding.AwayFromZero)));

        Vector2I mainViewportResolution =
            ResolveMainViewportResolution(textureResolution);

        return new SurfaceMetrics(
            logicalSize,
            textureResolution,
            mainViewportResolution,
            renderScaling);
    }

    static Vector2I ResolveMainViewportResolution(Vector2I fallback)
    {
        Vector2I resolution = RenderEngineComponent
            .Instance
            .RenderContracts
            .GetRenderSystem()
            .DisplaySettings
            .Resolution;

        return resolution.X > 0 && resolution.Y > 0
            ? resolution
            : fallback;
    }

    static void ApplyPlatformWindowMetrics(SurfaceMetrics metrics)
    {
        CurvedHudWindow window = _window
            ?? throw new InvalidOperationException(
                "The curved HUD window is unavailable while applying its surface metrics.");
        object platformWindow = PlatformWindow
            ?? throw new InvalidOperationException(
                "The curved HUD platform window is unavailable while applying its surface metrics.");

        window.Width = metrics.LogicalSize.Width;
        window.Height = metrics.LogicalSize.Height;

        // Keep all offscreen content inside a resolution-aware safe area. The
        // shader can translate by up to 16 logical pixels and scale to 101%,
        // while curvature consumes additional room near the corners. Insetting
        // the outer border gives those transforms transparent overscan instead
        // of clipping the HUD against the physical render-target edge.
        window.PART_SafeArea.Margin = CalculateSafeMargin(metrics.LogicalSize);

        MethodInfo resize = platformWindow.GetType().GetMethod(
                "Resize",
                BindingFlags.Instance | BindingFlags.Public,
                binder: null,
                types: new[] { typeof(Size), typeof(WindowResizeReason) },
                modifiers: null)
            ?? throw new MissingMethodException(
                platformWindow.GetType().FullName,
                "Resize(Size, WindowResizeReason)");

        resize.Invoke(
            platformWindow,
            new object[] { metrics.LogicalSize, WindowResizeReason.Application });

        Action<double>? scalingChanged = AccessProperty(
                platformWindow.GetType(),
                platformWindow,
                "ScalingChanged") as Action<double>;
        scalingChanged?.Invoke(metrics.RenderScaling);

        window.InvalidateMeasure();
        window.InvalidateVisual();
    }

    static Thickness CalculateSafeMargin(Size logicalSize)
    {
        double shortestSide = Math.Min(logicalSize.Width, logicalSize.Height);
        double horizontal = Math.Clamp(
            shortestSide * Constants.Layout.SafeMarginXRatio,
            Constants.Layout.MinSafeMarginX,
            Constants.Layout.MaxSafeMarginX);
        double vertical = Math.Clamp(
            shortestSide * Constants.Layout.SafeMarginYRatio,
            Constants.Layout.MinSafeMarginY,
            Constants.Layout.MaxSafeMarginY);

        return new Thickness(horizontal, vertical);
    }

    static void DetachPlatformWindowFromInput()
    {
        object platformWindow = PlatformWindow
            ?? throw new InvalidOperationException(
                "Curved HUD platform window is unavailable while detaching input.");

        MethodInfo returnInput = platformWindow.GetType().GetMethod(
                "ReturnInput",
                BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(
                platformWindow.GetType().FullName,
                "ReturnInput");

        // VRage chooses the topmost input owner by the platform window's real
        // Bounds, which remain at (0,0) even though this window is composed at
        // the screen center. Remove this render-only window from WindowManager
        // so pointer events continue to reach the underlying game UI.
        returnInput.Invoke(platformWindow, null);

        Log.Default.Info(
            $"[{Plugin.PluginId}] Detached the curved HUD render-only window from VRage input ownership.");
    }

    static void LogPlatformWindowState()
    {
        object platformWindow = PlatformWindow!;
        Type type = platformWindow.GetType();

        object? position = AccessProperty(type, platformWindow, "Position");
        object? clientSize = AccessProperty(type, platformWindow, "ClientSize");
        object? bounds = AccessProperty(type, platformWindow, "Bounds");
        object? renderScaling = AccessProperty(type, platformWindow, "RenderScaling");

        Log.Default.Info(
            $"[{Plugin.PluginId}] Curved HUD platform window state: " +
            $"type='{type.FullName}', position={position ?? "<null>"}, " +
            $"clientSize={clientSize ?? "<null>"}, bounds={bounds ?? "<null>"}, " +
            $"renderScaling={renderScaling ?? "<null>"}.");
    }

    static object? AccessProperty(Type type, object? instance, string name)
    {
        return type.GetProperty(
                name,
                BindingFlags.Instance |
                BindingFlags.Static |
                BindingFlags.Public |
                BindingFlags.NonPublic)
            ?.GetValue(instance);
    }

    static object? AccessField(Type type, object? instance, string name)
    {
        return type.GetField(
                name,
                BindingFlags.Instance |
                BindingFlags.Static |
                BindingFlags.Public |
                BindingFlags.NonPublic)
            ?.GetValue(instance);
    }

    static void CreateVisibleProbes()
    {
        var ui = RenderEngineComponent.Instance.RenderContracts.GetUISystem();

        if (CurvedHudDiagnostics.DrawMainViewProbe)
        {
            _mainViewProbeBatch = ui.CreatePersistentMainViewBatch(
                Constants.Composition.MainViewProbeSortLayer);
            _mainViewProbeBatch.DrawLine(
                new Vector2(220f, 180f),
                new Vector2(1700f, 900f),
                ColorSRGB.Magenta,
                24f,
                ignoreBounds: true);
            _mainViewProbeBatch.DrawLine(
                new Vector2(1700f, 180f),
                new Vector2(220f, 900f),
                ColorSRGB.Magenta,
                24f,
                ignoreBounds: true);
            _mainViewProbeBatch.Submit();

            Log.Default.Info(
                $"[{Plugin.PluginId}] Submitted magenta main-view X probe at " +
                $"sort layer {Constants.Composition.MainViewProbeSortLayer}.");
        }

        if (CurvedHudDiagnostics.DrawOffscreenProbe)
        {
            _offscreenProbeBatch = ui.CreatePersistentBatchFor(
                Target,
                Constants.Composition.OffscreenProbeSortLayer);
            _offscreenProbeBatch.DrawLine(
                new Vector2(20f, 20f),
                new Vector2(1004f, 492f),
                ColorSRGB.Cyan,
                32f,
                ignoreBounds: true);
            _offscreenProbeBatch.DrawLine(
                new Vector2(1004f, 20f),
                new Vector2(20f, 492f),
                ColorSRGB.Cyan,
                32f,
                ignoreBounds: true);
            _offscreenProbeBatch.DrawLine(
                new Vector2(20f, 256f),
                new Vector2(1004f, 256f),
                ColorSRGB.Yellow,
                16f,
                ignoreBounds: true);
            _offscreenProbeBatch.Submit();

            Log.Default.Info(
                $"[{Plugin.PluginId}] Submitted cyan/yellow direct offscreen " +
                $"probe at sort layer {Constants.Composition.OffscreenProbeSortLayer}.");
        }
    }

    static void ScheduleOffscreenCapture()
    {
        if (!CurvedHudDiagnostics.CaptureOffscreenTarget)
            return;

        if (!_captureSubscribed)
        {
            RenderEngineComponent.Instance.RenderOutputManager.OnScreenshotToMemoryTaken +=
                OnOffscreenScreenshotTaken;
            _captureSubscribed = true;
        }

        _captureTimer?.Stop();
        _captureTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(
                Constants.Composition.CaptureRetryIntervalMs)
        };
        _captureTimer.Tick += OnCaptureTimerTick;
        _captureTimer.Start();
    }

    static void OnCaptureTimerTick(object? sender, EventArgs e)
    {
        if (_captureTimer != null)
        {
            _captureTimer.Stop();
            _captureTimer.Tick -= OnCaptureTimerTick;
            _captureTimer = null;
        }

        if (HasValidTarget && Target.IsValid)
        {
            Log.Default.Info(
                $"[{Plugin.PluginId}] Requesting curved HUD offscreen target GPU readback.");
            Target.TakeScreenshotToMemory(waitUntilFullyLoaded: false);
        }
    }

    static void OnOffscreenScreenshotTaken(
        OffscreenRenderTarget target,
        Vector2I resolution,
        int pitch,
        Memory<byte> data)
    {
        if (!HasValidTarget || !target.Id.Equals(Target.Id))
            return;

        if (_captureSubscribed)
        {
            RenderEngineComponent.Instance.RenderOutputManager.OnScreenshotToMemoryTaken -=
                OnOffscreenScreenshotTaken;
            _captureSubscribed = false;
        }

        ReadOnlySpan<byte> bytes = data.Span;
        int width = Math.Max(0, resolution.X);
        int height = Math.Max(0, resolution.Y);
        long alphaNonZero = 0;
        long rgbNonZero = 0;
        byte maxAlpha = 0;
        int minX = width;
        int minY = height;
        int maxX = -1;
        int maxY = -1;

        for (int y = 0; y < height; y++)
        {
            int rowStart = y * pitch;
            if (rowStart < 0 || rowStart >= bytes.Length)
                break;

            int rowPixels = Math.Min(width, (bytes.Length - rowStart) / 4);
            for (int x = 0; x < rowPixels; x++)
            {
                int index = rowStart + x * 4;
                byte b = bytes[index];
                byte g = bytes[index + 1];
                byte r = bytes[index + 2];
                byte a = bytes[index + 3];

                if ((r | g | b) != 0)
                    rgbNonZero++;

                if (a == 0)
                    continue;

                alphaNonZero++;
                if (a > maxAlpha)
                    maxAlpha = a;
                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
            }
        }

        string alphaBounds = alphaNonZero == 0
            ? "<empty>"
            : $"({minX},{minY})-({maxX},{maxY})";

        Log.Default.Info(
            $"[{Plugin.PluginId}] Curved HUD offscreen capture statistics: " +
            $"resolution={resolution}, pitch={pitch}, bytes={bytes.Length}, " +
            $"alphaNonZero={alphaNonZero}, rgbNonZero={rgbNonZero}, " +
            $"maxAlpha={maxAlpha}, alphaBounds={alphaBounds}.");
    }

    static void BeginDeferredComposition()
    {
        _compositionStartRequested = true;
        _compositionRetryTimer?.Stop();

        _compositionRetryTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(
                Constants.Composition.CompositionRetryIntervalMs)
        };

        _compositionRetryTimer.Tick += OnCompositionRetryTick;
        _compositionRetryTimer.Start();

        if (!CurvedHudDiagnostics.WaitForFirstOffscreenSubmit)
            TryCreateCompositionBatch();
    }

    static void RestartCompositionRetryTimer()
    {
        if (_compositionRetryTimer == null)
            return;

        _compositionRetryTimer.Stop();
        _compositionRetryTimer.Start();
    }

    static void OnCompositionRetryTick(object? sender, EventArgs e)
    {
        TryCreateCompositionBatch();
    }

    static void TryCreateCompositionBatch()
    {
        if (_window == null || !HasValidTarget || !Target.IsValid)
        {
            StopCompositionRetry();
            return;
        }

        if (_compositionBatch != null)
        {
            StopCompositionRetry();
            return;
        }

        if (CurvedHudDiagnostics.WaitForFirstOffscreenSubmit &&
            Volatile.Read(ref _offscreenSubmitSeen) == 0)
        {
            if (Interlocked.Exchange(ref _loggedWaitingForSubmit, 1) == 0)
            {
                Log.Default.Info(
                    $"[{Plugin.PluginId}] Waiting for the first curved HUD " +
                    "offscreen batch submission before composing its texture.");
            }

            return;
        }

        StopCompositionRetry();
        CreateCompositionBatch();
        Log.Default.Info(
            $"[{Plugin.PluginId}] Curved HUD composition batch submitted as one centered sprite.");
    }

    static void StopCompositionRetry()
    {
        if (_compositionRetryTimer == null)
            return;

        _compositionRetryTimer.Stop();
        _compositionRetryTimer.Tick -= OnCompositionRetryTick;
        _compositionRetryTimer = null;
    }

    static void CreateCompositionBatch()
    {
        _compositionBatch?.Dispose();

        var ui = RenderEngineComponent.Instance.RenderContracts.GetUISystem();
        _compositionBatch = ui.CreatePersistentMainViewBatch(
            Constants.Composition.MainViewSortLayer);

        if (_textureResolution.X <= 0 || _textureResolution.Y <= 0)
        {
            throw new InvalidOperationException(
                $"Curved HUD texture resolution is invalid: {_textureResolution}.");
        }

        BoundingBox2 destination = CreateCenteredCompositionDestination();

        ColorSRGB wobbleData = EncodeWobbleColor();
        _compositionBatch.DrawImage(
            CompositionTextureHandle,
            in destination,
            wobbleData,
            ignoreBounds: true,
            maskTexture: null,
            sourceRectangle: null);

        _compositionBatch.Submit();
        _submittedWobbleColor = wobbleData.PackedValue;
    }

    static BoundingBox2 CreateCenteredCompositionDestination()
    {
        float width = _textureResolution.X;
        float height = _textureResolution.Y;
        var halfSize = new Vector2(width * 0.5f, height * 0.5f);
        var viewportCenter = new Vector2(
            _mainViewportResolution.X * 0.5f,
            _mainViewportResolution.Y * 0.5f);
        var center = new Vector2(
            viewportCenter.X + (float)(Constants.Composition.TextureCenterOffsetX * _renderScaling),
            viewportCenter.Y + (float)(Constants.Composition.TextureCenterOffsetY * _renderScaling));

        return new BoundingBox2(center - halfSize, center + halfSize);
    }

    static ColorSRGB EncodeWobbleColor()
    {
        if (!double.IsFinite(_renderScaling) || _renderScaling <= 0.0)
        {
            throw new InvalidOperationException(
                $"Curved HUD render scaling is invalid while encoding wobble: {_renderScaling}.");
        }

        double physicalX = _wobbleOffsetX * _renderScaling;
        double physicalY = _wobbleOffsetY * _renderScaling;
        double scaleDelta = _wobbleScale - 1.0;

        return new ColorSRGB(
            PackSignedByte(physicalX / Constants.Wobble.MaxPhysicalOffsetPixels),
            PackSignedByte(physicalY / Constants.Wobble.MaxPhysicalOffsetPixels),
            PackSignedByte(scaleDelta / Constants.Wobble.MaxScaleDelta),
            PackUiOpacity());
    }

    static byte PackUiOpacity()
    {
        double opacity = UiRevampSettings.ClampUiOpacity(Plugin.Settings.UiOpacity);
        return (byte)Math.Clamp(
            (int)Math.Round(opacity * byte.MaxValue, MidpointRounding.AwayFromZero),
            byte.MinValue,
            byte.MaxValue);
    }

    static byte PackSignedByte(double normalized)
    {
        normalized = Math.Clamp(normalized, -1.0, 1.0);

        // Byte 128 is exact zero. Values 1..255 represent -1..+1, leaving 0
        // unused so the shader can decode neutral without the half-byte bias of
        // the usual value*2-1 mapping.
        return (byte)Math.Clamp(
            (int)Math.Round(
                Constants.Wobble.NeutralByte +
                normalized * Constants.Wobble.SignedByteRange),
            Constants.Wobble.MinPackedByte,
            Constants.Wobble.MaxPackedByte);
    }

}
