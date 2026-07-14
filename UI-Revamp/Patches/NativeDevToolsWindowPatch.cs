#if DEBUG
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Threading;
using Avalonia;
using Avalonia.Controls.Platform;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using HarmonyLib;
using Keen.VRage.Library.Diagnostics;
using SE2PluginLoader;
using SkiaSharp;

namespace UI_Revamp.Patches;

internal static class NativeDevToolsWindowContext
{
    private static int _openDepth;
    private static IDisposable? _nativeScope;
    private static bool _nativeWindowCreated;
    private static bool _patchedWin32CreateWindowOverride;
    private static bool _patchedWin32FullInvalidation;
    private static bool _patchedNativeFullRenderDirtyRect;
    private static bool _patchedSkiaGlyphRunFallback;
    private static bool _patchedSkiaGeometryFallback;
    private static int _nativeRenderDepth;
    private static int _nativeResizeMoveDepth;
    private static int _pumpLogCount;
    private static int _wrongThreadPumpLogCount;
    private static int _renderPumpLogCount;
    private static uint _nativeWindowOwnerThreadId;
    private static IntPtr _nativeWindowHwnd;
    private static object? _win32CompositorServer;
    private static MethodInfo? _win32CompositorServerRender;
    private static object? _nativeRenderInterface;
    private static object? _nativeFontManager;
    private static object? _nativeTextShaper;

    public static bool IsOpening => Volatile.Read(ref _openDepth) > 0;
    public static bool IsRenderingNative => Volatile.Read(ref _nativeRenderDepth) > 0;
    public static bool IsNativeHwnd(IntPtr hwnd) => hwnd != IntPtr.Zero && hwnd == _nativeWindowHwnd;

    public static bool IsNativeCompositionTarget(object compositionTarget)
    {
        try
        {
            var surfacesDelegate = compositionTarget.GetType()
                .GetField("_surfaces", BindingFlags.Instance | BindingFlags.NonPublic)?
                .GetValue(compositionTarget) as Delegate;
            if (surfacesDelegate?.DynamicInvoke() is not IEnumerable<object> surfaces)
            {
                return false;
            }

            foreach (var surface in surfaces)
            {
                var handle = surface as IPlatformHandle;
                if (handle?.HandleDescriptor == "HWND" && IsNativeHwnd(handle.Handle))
                {
                    return true;
                }
            }
        }
        catch (Exception e)
        {
            Log.Default.Error($"[{Plugin.PluginId}] Failed to identify native DevTools composition target: {e}");
        }

        return false;
    }

    public static IDisposable EnterNativeRenderScope()
    {
        var scope = EnterAvaloniaScope();
        BindCachedNativeRenderServices();
        return scope;
    }

    public static IDisposable Enter()
    {
        Interlocked.Increment(ref _openDepth);
        return new ScopeLease();
    }

    public static bool TryCreateNativeWindow(out IWindowImpl windowImpl)
    {
        windowImpl = null!;

        if (_nativeWindowCreated)
        {
            Log.Default.Info($"[{Plugin.PluginId}] Native DevTools window already exists, falling back to VRage window.");
            return false;
        }

        IDisposable? scope = null;
        try
        {
            Log.Default.Info($"[{Plugin.PluginId}] Creating native Win32/Skia DevTools window.");

            LoadDesktopAssemblies();

            scope = EnterAvaloniaScope();
            InitializeSkia();
            CaptureNativeRenderServices();
            InitializeWin32();

            var platform = GetCurrentWindowingPlatform();
            windowImpl = CreateWindowImpl(platform);

            AddClosedHandler(windowImpl, OnNativeWindowClosed);

            _nativeScope = scope;
            _nativeWindowCreated = true;
            scope = null;

            Log.Default.Info($"[{Plugin.PluginId}] Native Win32/Skia DevTools window implementation created: {windowImpl.GetType().FullName}.");
            return true;
        }
        catch (Exception e)
        {
            scope?.Dispose();
            Log.Default.Error($"[{Plugin.PluginId}] Failed to create native Win32/Skia DevTools window, falling back to VRage window: {e}");
            return false;
        }
    }

    public static void RestoreGameAvaloniaServicesAfterOpen()
    {
        RestoreGameAvaloniaServices("DevTools.Open completed");
    }

    public static void PumpWin32Messages()
    {
        if (!_nativeWindowCreated)
        {
            return;
        }

        var currentThreadId = GetCurrentThreadId();
        var ownerThreadId = Volatile.Read(ref _nativeWindowOwnerThreadId);
        if (ownerThreadId != 0 && currentThreadId != ownerThreadId)
        {
            if (Interlocked.Increment(ref _wrongThreadPumpLogCount) <= 10)
            {
                Log.Default.Info(
                    $"[{Plugin.PluginId}] Skipping native DevTools Win32 pump on wrong thread. " +
                    $"current={currentThreadId}, owner={ownerThreadId}.");
            }

            return;
        }

        const uint removeMessage = 0x0001;
        var pumped = 0;
        while (pumped < 64 && PeekMessageW(out var message, IntPtr.Zero, 0, 0, removeMessage))
        {
            TranslateMessage(ref message);
            DispatchMessageW(ref message);
            pumped++;
        }

        if (pumped > 0 && Interlocked.Increment(ref _pumpLogCount) <= 5)
        {
            Log.Default.Info(
                $"[{Plugin.PluginId}] Pumped {pumped} Win32 messages for native DevTools window " +
                $"on thread {currentThreadId}.");
        }

        if (Volatile.Read(ref _nativeResizeMoveDepth) > 0)
        {
            return;
        }

        PumpNativeRender(currentThreadId);
    }

    public static void TrackNativeWindowMessage(IntPtr hwnd, uint msg)
    {
        if (!IsNativeHwnd(hwnd))
        {
            return;
        }

        switch (msg)
        {
            case 0x0231: // WM_ENTERSIZEMOVE
                Interlocked.Exchange(ref _nativeResizeMoveDepth, 1);
                Log.Default.Info($"[{Plugin.PluginId}] Suspending native DevTools render during live resize/move.");
                break;
            case 0x0232: // WM_EXITSIZEMOVE
                Interlocked.Exchange(ref _nativeResizeMoveDepth, 0);
                Log.Default.Info($"[{Plugin.PluginId}] Resuming native DevTools render after live resize/move.");
                break;
        }
    }

    public static void MarkNativeHwndCreated(IntPtr hwnd)
    {
        _nativeWindowHwnd = hwnd;
        _nativeWindowOwnerThreadId = GetCurrentThreadId();
        _pumpLogCount = 0;
        _wrongThreadPumpLogCount = 0;
        _renderPumpLogCount = 0;
        Log.Default.Info(
            $"[{Plugin.PluginId}] Native DevTools HWND owner thread recorded. " +
            $"HWND=0x{hwnd.ToInt64():X}, thread={_nativeWindowOwnerThreadId}.");
    }

    private static void PumpNativeRender(uint currentThreadId)
    {
        IDisposable? scope = null;
        try
        {
            Dispatcher.UIThread.RunJobs();

            scope = EnterAvaloniaScope();
            BindCachedNativeRenderServices();

            RenderWin32Compositor();

            if (Interlocked.Increment(ref _renderPumpLogCount) <= 5)
            {
                Log.Default.Info($"[{Plugin.PluginId}] Pumped native DevTools Avalonia render jobs on thread {currentThreadId}.");
            }
        }
        catch (Exception e)
        {
            if (Interlocked.Increment(ref _renderPumpLogCount) <= 10)
            {
                Log.Default.Error($"[{Plugin.PluginId}] Native DevTools render pump failed on thread {currentThreadId}: {e}");
            }
        }
        finally
        {
            scope?.Dispose();
        }
    }

    private static void RenderWin32Compositor()
    {
        var server = _win32CompositorServer;
        var render = _win32CompositorServerRender;
        if (server == null || render == null)
        {
            var win32Platform = Type.GetType("Avalonia.Win32.Win32Platform, Avalonia.Win32", throwOnError: true)!;
            var compositor = win32Platform.GetProperty("Compositor", BindingFlags.Static | BindingFlags.NonPublic)!
                .GetValue(null)
                ?? throw new InvalidOperationException("Avalonia Win32 compositor was not initialized.");
            server = compositor.GetType().GetProperty("Server", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(compositor)
                ?? throw new InvalidOperationException("Avalonia Win32 compositor server was not initialized.");
            render = server.GetType().GetMethod("Render", BindingFlags.Instance | BindingFlags.Public)
                ?? throw new MissingMethodException(server.GetType().FullName, "Render");
            _win32CompositorServer = server;
            _win32CompositorServerRender = render;
        }

        Interlocked.Increment(ref _nativeRenderDepth);
        try
        {
            render.Invoke(server, null);
        }
        finally
        {
            Interlocked.Decrement(ref _nativeRenderDepth);
        }
    }

    private static void CaptureNativeRenderServices()
    {
        _nativeRenderInterface = CreateNativeSkiaService("Avalonia.Skia.PlatformRenderInterface", typeof(IPlatformRenderInterface), new object?[] { null });
        _nativeFontManager = CreateNativeSkiaService("Avalonia.Skia.FontManagerImpl", typeof(IFontManagerImpl), null);
        _nativeTextShaper = CreateNativeSkiaService("Avalonia.Skia.TextShaperImpl", typeof(ITextShaperImpl), null);
        Log.Default.Info(
            $"[{Plugin.PluginId}] Captured native DevTools render services: " +
            $"{_nativeRenderInterface.GetType().FullName}, {_nativeFontManager.GetType().FullName}, {_nativeTextShaper.GetType().FullName}.");
    }

    private static void BindCachedNativeRenderServices()
    {
        if (_nativeRenderInterface == null || _nativeFontManager == null || _nativeTextShaper == null)
        {
            InitializeSkia();
            CaptureNativeRenderServices();
        }

        BindAvaloniaService(typeof(IPlatformRenderInterface), _nativeRenderInterface!);
        BindAvaloniaService(typeof(IFontManagerImpl), _nativeFontManager!);
        BindAvaloniaService(typeof(ITextShaperImpl), _nativeTextShaper!);
    }

    private static object CreateNativeSkiaService(string typeName, Type serviceType, object?[]? args)
    {
        var type = Type.GetType($"{typeName}, Avalonia.Skia", throwOnError: true)!;
        var service = Activator.CreateInstance(type, args ?? Array.Empty<object>())
            ?? throw new InvalidOperationException($"Unable to create {typeName}.");
        if (!serviceType.IsInstanceOfType(service))
        {
            throw new InvalidOperationException($"{type.FullName} is not assignable to {serviceType.FullName}.");
        }

        return service;
    }

    public static bool TryCreateNativeRenderTarget(IEnumerable<object> surfaces, out IRenderTarget renderTarget)
    {
        renderTarget = null!;

        try
        {
            if (_nativeRenderInterface == null)
            {
                CaptureNativeRenderServices();
            }

            var createBackendContext = typeof(IPlatformRenderInterface).GetMethod("CreateBackendContext", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(typeof(IPlatformRenderInterface).FullName, "CreateBackendContext");
            var context = createBackendContext.Invoke(_nativeRenderInterface, new object?[] { null })
                ?? throw new InvalidOperationException("Native Skia did not create an IPlatformRenderInterfaceContext.");
            var createRenderTarget = typeof(IPlatformRenderInterfaceContext).GetMethod("CreateRenderTarget", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(typeof(IPlatformRenderInterfaceContext).FullName, "CreateRenderTarget");
            renderTarget = (IRenderTarget)createRenderTarget.Invoke(context, new object[] { surfaces })!;
            return true;
        }
        catch (Exception e)
        {
            Log.Default.Error($"[{Plugin.PluginId}] Failed to create native DevTools Skia render target: {e}");
            return false;
        }
    }

    private static void BindAvaloniaService(Type serviceType, object service)
    {
        var currentMutable = typeof(AvaloniaLocator).GetProperty("CurrentMutable", BindingFlags.Public | BindingFlags.Static)?
            .GetValue(null)
            ?? throw new MissingMemberException(typeof(AvaloniaLocator).FullName, "CurrentMutable");
        var bind = currentMutable.GetType().GetMethod("Bind", BindingFlags.Public | BindingFlags.Instance)?
            .MakeGenericMethod(serviceType)
            .Invoke(currentMutable, null)
            ?? throw new MissingMethodException(currentMutable.GetType().FullName, "Bind");
        bind.GetType().GetMethod("ToConstant", BindingFlags.Public | BindingFlags.Instance)?
            .MakeGenericMethod(serviceType)
            .Invoke(bind, new[] { service });
    }

    private static void OnNativeWindowClosed()
    {
        _nativeWindowCreated = false;
        _nativeWindowHwnd = IntPtr.Zero;
        Interlocked.Exchange(ref _nativeResizeMoveDepth, 0);
        RestoreGameAvaloniaServices("native DevTools window closed");
    }

    private static void RestoreGameAvaloniaServices(string reason)
    {
        var scope = Interlocked.Exchange(ref _nativeScope, null);
        if (scope == null)
        {
            return;
        }

        scope.Dispose();
        Log.Default.Info($"[{Plugin.PluginId}] Restored VRage Avalonia platform services ({reason}).");
    }

    private static void LoadDesktopAssemblies()
    {
        var pluginDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        if (string.IsNullOrWhiteSpace(pluginDirectory))
        {
            throw new InvalidOperationException("Unable to resolve plugin assembly directory.");
        }

        LoadAssembly(pluginDirectory, "Avalonia.MicroCom.dll");
        LoadAssembly(pluginDirectory, "Avalonia.Metal.dll");
        LoadAssembly(pluginDirectory, "Avalonia.Skia.dll");
        LoadAssembly(pluginDirectory, "Avalonia.OpenGL.dll");
        LoadAssembly(pluginDirectory, "Avalonia.Win32.dll");
        PatchWin32CreateWindowOverride();
        PatchWin32FullInvalidation();
        PatchNativeFullRenderDirtyRect();
        PatchSkiaGlyphRunFallback();
        PatchSkiaGeometryFallback();
    }

    private static void LoadAssembly(string directory, string fileName)
    {
        if (AppDomain.CurrentDomain.GetAssemblies().Any(a =>
                string.Equals(a.GetName().Name, Path.GetFileNameWithoutExtension(fileName), StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var path = Path.Combine(directory, fileName);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Required native DevTools assembly was not found in plugin directory: {path}", path);
        }

        AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
        Log.Default.Info($"[{Plugin.PluginId}] Loaded native DevTools assembly: {path}");
    }

    private static IDisposable EnterAvaloniaScope()
    {
        var enterScope = typeof(AvaloniaLocator).GetMethod("EnterScope", BindingFlags.Public | BindingFlags.Static)
            ?? throw new MissingMethodException(typeof(AvaloniaLocator).FullName, "EnterScope");
        return (IDisposable)enterScope.Invoke(null, null)!;
    }

    private static object GetCurrentWindowingPlatform()
    {
        var current = typeof(AvaloniaLocator).GetProperty("Current", BindingFlags.Public | BindingFlags.Static)?
            .GetValue(null)
            ?? throw new MissingMemberException(typeof(AvaloniaLocator).FullName, "Current");
        var getService = current.GetType().GetMethod("GetService", BindingFlags.Public | BindingFlags.Instance)
            ?? throw new MissingMethodException(current.GetType().FullName, "GetService");
        return getService.Invoke(current, new object[] { typeof(IWindowingPlatform) })
            ?? throw new InvalidOperationException("Current Avalonia locator returned no IWindowingPlatform.");
    }

    private static IWindowImpl CreateWindowImpl(object platform)
    {
        var createWindow = platform.GetType().GetMethod("CreateWindow", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new MissingMethodException(platform.GetType().FullName, "CreateWindow");
        return (IWindowImpl)createWindow.Invoke(platform, null)!;
    }

    private static void AddClosedHandler(IWindowImpl windowImpl, Action handler)
    {
        var closed = windowImpl.GetType().GetProperty("Closed", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new MissingMemberException(windowImpl.GetType().FullName, "Closed");
        var previous = (Action?)closed.GetValue(windowImpl);
        closed.SetValue(windowImpl, (Action?)Delegate.Combine(previous, handler));
    }

    private static void InitializeSkia(bool log = true)
    {
        var skiaPlatform = Type.GetType("Avalonia.Skia.SkiaPlatform, Avalonia.Skia", throwOnError: true)!;
        skiaPlatform.GetMethod("Initialize", BindingFlags.Public | BindingFlags.Static, Type.EmptyTypes)!
            .Invoke(null, null);
        if (log)
        {
            Log.Default.Info($"[{Plugin.PluginId}] Initialized Avalonia Skia platform services for native DevTools window.");
        }
    }

    private static void InitializeWin32()
    {
        var win32Platform = Type.GetType("Avalonia.Win32.Win32Platform, Avalonia.Win32", throwOnError: true)!;
        var options = CreateWin32PlatformOptions();
        win32Platform.GetMethod("Initialize", BindingFlags.Public | BindingFlags.Static, new[] { options.GetType() })!
            .Invoke(null, new[] { options });
        Log.Default.Info($"[{Plugin.PluginId}] Initialized Avalonia Win32 platform services for native DevTools window with software/redirection options.");
    }

    private static object CreateWin32PlatformOptions()
    {
        var optionsType = Type.GetType("Avalonia.Win32PlatformOptions, Avalonia.Win32", throwOnError: true)!;
        var renderingModeType = Type.GetType("Avalonia.Win32RenderingMode, Avalonia.Win32", throwOnError: true)!;
        var compositionModeType = Type.GetType("Avalonia.Win32CompositionMode, Avalonia.Win32", throwOnError: true)!;

        var options = Activator.CreateInstance(optionsType)!;
        optionsType.GetProperty("RenderingMode")!.SetValue(options, CreateEnumArray(renderingModeType, "Software"));
        optionsType.GetProperty("CompositionMode")!.SetValue(options, CreateEnumArray(compositionModeType, "RedirectionSurface"));
        optionsType.GetProperty("ShouldRenderOnUIThread")!.SetValue(options, true);
        return options;
    }

    private static Array CreateEnumArray(Type enumType, string value)
    {
        var array = Array.CreateInstance(enumType, 1);
        array.SetValue(Enum.Parse(enumType, value), 0);
        return array;
    }

    private static void PatchWin32CreateWindowOverride()
    {
        if (_patchedWin32CreateWindowOverride)
        {
            return;
        }

        var windowImpl = Type.GetType("Avalonia.Win32.WindowImpl, Avalonia.Win32", throwOnError: true)!;
        var method = AccessTools.Method(windowImpl, "CreateWindowOverride")
            ?? throw new MissingMethodException(windowImpl.FullName, "CreateWindowOverride");
        var wndProc = AccessTools.Method(windowImpl, "WndProc")
            ?? throw new MissingMethodException(windowImpl.FullName, "WndProc");
        var prefix = new HarmonyMethod(typeof(Win32WindowCreateOverridePatch), nameof(Win32WindowCreateOverridePatch.Prefix));
        var wndProcPrefixMethod = AccessTools.Method(
                typeof(Win32WindowCreateOverridePatch),
                nameof(Win32WindowCreateOverridePatch.WndProcPrefix),
                new[] { typeof(IntPtr), typeof(uint) })
            ?? throw new MissingMethodException(typeof(Win32WindowCreateOverridePatch).FullName, nameof(Win32WindowCreateOverridePatch.WndProcPrefix));
        var wndProcPrefix = new HarmonyMethod(wndProcPrefixMethod);
        var finalizer = new HarmonyMethod(typeof(Win32WindowCreateOverridePatch), nameof(Win32WindowCreateOverridePatch.WndProcFinalizer));
        var harmony = new Harmony($"{Plugin.PluginId}.NativeDevToolsWin32");
        harmony.Patch(method, prefix);
        harmony.Patch(wndProc, wndProcPrefix, finalizer: finalizer);
        _patchedWin32CreateWindowOverride = true;
        Log.Default.Info($"[{Plugin.PluginId}] Patched Avalonia.Win32 WindowImpl.CreateWindowOverride and WndProc for native DevTools window.");
    }

    private static void PatchWin32FullInvalidation()
    {
        if (_patchedWin32FullInvalidation)
        {
            return;
        }

        var windowImpl = Type.GetType("Avalonia.Win32.WindowImpl, Avalonia.Win32", throwOnError: true)!;
        var method = AccessTools.Method(windowImpl, "Invalidate", new[] { typeof(Rect) })
            ?? throw new MissingMethodException(windowImpl.FullName, "Invalidate");
        var prefix = new HarmonyMethod(typeof(Win32WindowFullInvalidationPatch), nameof(Win32WindowFullInvalidationPatch.Prefix));
        var harmony = new Harmony($"{Plugin.PluginId}.NativeDevToolsWin32Invalidate");
        harmony.Patch(method, prefix);
        _patchedWin32FullInvalidation = true;
        Log.Default.Info($"[{Plugin.PluginId}] Patched Avalonia.Win32 WindowImpl.Invalidate for full native DevTools repaint.");
    }

    private static void PatchNativeFullRenderDirtyRect()
    {
        if (_patchedNativeFullRenderDirtyRect)
        {
            return;
        }

        var serverCompositionTarget = AccessTools.TypeByName("Avalonia.Rendering.Composition.Server.ServerCompositionTarget")
            ?? throw new MissingMemberException("Avalonia.Rendering.Composition.Server.ServerCompositionTarget");
        var render = AccessTools.Method(serverCompositionTarget, "Render")
            ?? throw new MissingMethodException(serverCompositionTarget.FullName, "Render");
        var prefix = new HarmonyMethod(typeof(NativeDevToolsFullRenderDirtyRectPatch), nameof(NativeDevToolsFullRenderDirtyRectPatch.Prefix));
        var harmony = new Harmony($"{Plugin.PluginId}.NativeDevToolsFullRenderDirtyRect");
        harmony.Patch(render, prefix);
        _patchedNativeFullRenderDirtyRect = true;
        Log.Default.Info($"[{Plugin.PluginId}] Patched ServerCompositionTarget.Render for full native DevTools repaint.");
    }

    private static void PatchSkiaGlyphRunFallback()
    {
        if (_patchedSkiaGlyphRunFallback)
        {
            return;
        }

        var drawingContext = Type.GetType("Avalonia.Skia.DrawingContextImpl, Avalonia.Skia", throwOnError: true)!;
        var method = AccessTools.Method(
                drawingContext,
                "DrawGlyphRun",
                new[] { typeof(IBrush), typeof(IGlyphRunImpl) })
            ?? throw new MissingMethodException(drawingContext.FullName, "DrawGlyphRun");
        var prefix = new HarmonyMethod(
            typeof(NativeDevToolsSkiaGlyphRunFallbackPatch),
            nameof(NativeDevToolsSkiaGlyphRunFallbackPatch.Prefix));
        var harmony = new Harmony($"{Plugin.PluginId}.NativeDevToolsSkiaGlyph");
        harmony.Patch(method, prefix);
        _patchedSkiaGlyphRunFallback = true;
        Log.Default.Info($"[{Plugin.PluginId}] Patched Avalonia.Skia DrawingContextImpl.DrawGlyphRun for Keen glyph fallback.");
    }

    private static void PatchSkiaGeometryFallback()
    {
        if (_patchedSkiaGeometryFallback)
        {
            return;
        }

        var drawingContext = Type.GetType("Avalonia.Skia.DrawingContextImpl, Avalonia.Skia", throwOnError: true)!;
        var method = AccessTools.Method(
                drawingContext,
                "DrawGeometry",
                new[] { typeof(IBrush), typeof(IPen), typeof(IGeometryImpl) })
            ?? throw new MissingMethodException(drawingContext.FullName, "DrawGeometry");
        var prefix = new HarmonyMethod(
            typeof(NativeDevToolsSkiaGeometryFallbackPatch),
            nameof(NativeDevToolsSkiaGeometryFallbackPatch.Prefix));
        var harmony = new Harmony($"{Plugin.PluginId}.NativeDevToolsSkiaGeometry");
        harmony.Patch(method, prefix);
        _patchedSkiaGeometryFallback = true;
        Log.Default.Info($"[{Plugin.PluginId}] Patched Avalonia.Skia DrawingContextImpl.DrawGeometry for Keen geometry fallback.");
    }

    private sealed class ScopeLease : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                Interlocked.Decrement(ref _openDepth);
            }
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PeekMessageW(out Msg lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref Msg lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessageW(ref Msg lpMsg);

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public IntPtr HWnd;
        public uint Message;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public int PointX;
        public int PointY;
    }
}

internal static class Win32WindowCreateOverridePatch
{
    private const uint WsCaption = 0x00C00000;
    private const uint WsSysMenu = 0x00080000;
    private const uint WsMinimizeBox = 0x00020000;
    private const uint WsClipChildren = 0x02000000;
    private const uint NativeDevToolsWindowStyle = WsCaption | WsSysMenu | WsMinimizeBox | WsClipChildren;
    private static int _calls;

    public static bool Prefix(object __instance, ushort atom, ref IntPtr __result)
    {
        var call = Interlocked.Increment(ref _calls);
        var className = (string?)__instance.GetType()
            .GetField("_className", BindingFlags.Instance | BindingFlags.NonPublic)?
            .GetValue(__instance);
        var moduleHandle = GetModuleHandleW(null);

        Log.Default.Info(
            $"[{Plugin.PluginId}] Native DevTools CreateWindowOverride patch hit #{call}, atom={atom}, " +
            $"className={className ?? "<null>"}, hInstance=0x{moduleHandle.ToInt64():X}.");

        if (!string.IsNullOrWhiteSpace(className))
        {
            __result = CreateWindowExW(
                0,
                className,
                null,
                NativeDevToolsWindowStyle,
                100,
                100,
                1280,
                720,
                IntPtr.Zero,
                IntPtr.Zero,
                moduleHandle,
                IntPtr.Zero);
        }

        if (__result == IntPtr.Zero)
        {
            var stringError = Marshal.GetLastWin32Error();
            Log.Default.Error($"[{Plugin.PluginId}] Native DevTools CreateWindowExW by class name failed. LastWin32Error={stringError}.");

            __result = CreateWindowExW(
                0,
                new IntPtr(atom),
                null,
                NativeDevToolsWindowStyle,
                100,
                100,
                1280,
                720,
                IntPtr.Zero,
                IntPtr.Zero,
                moduleHandle,
                IntPtr.Zero);
        }

        if (__result == IntPtr.Zero)
        {
            Log.Default.Error($"[{Plugin.PluginId}] Native DevTools CreateWindowExW by atom failed. LastWin32Error={Marshal.GetLastWin32Error()}.");
        }
        else
        {
            Log.Default.Info($"[{Plugin.PluginId}] Native DevTools CreateWindowExW override created HWND=0x{__result.ToInt64():X}.");
            NativeDevToolsWindowContext.MarkNativeHwndCreated(__result);
        }

        return false;
    }

    public static void WndProcPrefix(IntPtr hWnd, uint msg)
    {
        NativeDevToolsWindowContext.TrackNativeWindowMessage(hWnd, msg);
    }

    public static Exception? WndProcFinalizer(object __instance, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, Exception? __exception)
    {
        if (__exception != null)
        {
            Log.Default.Error(
                $"[{Plugin.PluginId}] Native DevTools Avalonia.Win32 WndProc threw during message {msg} " +
                $"for HWND=0x{hWnd.ToInt64():X}: {__exception}");
        }

        return __exception;
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CreateWindowExW")]
    private static extern IntPtr CreateWindowExW(
        uint dwExStyle,
        string? lpClassName,
        string? lpWindowName,
        uint dwStyle,
        int x,
        int y,
        int nWidth,
        int nHeight,
        IntPtr hWndParent,
        IntPtr hMenu,
        IntPtr hInstance,
        IntPtr lpParam);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CreateWindowExW")]
    private static extern IntPtr CreateWindowExW(
        uint dwExStyle,
        IntPtr lpClassName,
        string? lpWindowName,
        uint dwStyle,
        int x,
        int y,
        int nWidth,
        int nHeight,
        IntPtr hWndParent,
        IntPtr hMenu,
        IntPtr hInstance,
        IntPtr lpParam);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(string? lpModuleName);

}

[HarmonyPatch]
public static class PlatformManagerNativeDevToolsPatch
{
    public static MethodBase TargetMethod()
    {
        return AccessTools.Method(typeof(PlatformManager), nameof(PlatformManager.CreateWindow));
    }

    public static bool Prefix(ref IWindowImpl __result)
    {
        if (!NativeDevToolsWindowContext.IsOpening)
        {
            return true;
        }

        if (!NativeDevToolsWindowContext.TryCreateNativeWindow(out var nativeWindow))
        {
            return true;
        }

        __result = nativeWindow;
        return false;
    }
}

[HarmonyPatch]
public static class NativeDevToolsGameUpdateMessagePumpPatch
{
    public static MethodBase TargetMethod()
    {
        var gameAppComponent = AccessTools.TypeByName("Keen.Game2.GameAppComponent")
            ?? throw new MissingMemberException("Keen.Game2.GameAppComponent");
        return AccessTools.Method(gameAppComponent, "GameUpdate")
            ?? throw new MissingMethodException(gameAppComponent.FullName, "GameUpdate");
    }

    public static void Postfix()
    {
        NativeDevToolsWindowContext.PumpWin32Messages();
    }
}

internal static class Win32WindowFullInvalidationPatch
{
    private static int _loggedInvalidations;

    public static bool Prefix(object __instance)
    {
        var hwnd = (IntPtr)(__instance.GetType()
            .GetField("_hwnd", BindingFlags.Instance | BindingFlags.NonPublic)?
            .GetValue(__instance) ?? IntPtr.Zero);
        if (!NativeDevToolsWindowContext.IsNativeHwnd(hwnd))
        {
            return true;
        }

        InvalidateRect(hwnd, IntPtr.Zero, false);
        if (Interlocked.Increment(ref _loggedInvalidations) <= 3)
        {
            Log.Default.Info($"[{Plugin.PluginId}] Expanded native DevTools invalidation to full client area.");
        }

        return false;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool InvalidateRect(IntPtr hWnd, IntPtr lpRect, bool bErase);
}

internal static class NativeDevToolsFullRenderDirtyRectPatch
{
    private static int _loggedExpansions;

    public static void Prefix(object __instance)
    {
        if (!NativeDevToolsWindowContext.IsNativeCompositionTarget(__instance))
        {
            return;
        }

        if (__instance.GetType()
                .GetProperty("Size", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?
                .GetValue(__instance) is not Size size ||
            size.Width <= 0.0 ||
            size.Height <= 0.0)
        {
            return;
        }

        var addDirtyRect = __instance.GetType()
            .GetMethod("AddDirtyRect", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, new[] { typeof(Rect) });
        addDirtyRect?.Invoke(__instance, new object[] { new Rect(0, 0, size.Width, size.Height) });

        if (Interlocked.Increment(ref _loggedExpansions) <= 5)
        {
            Log.Default.Info($"[{Plugin.PluginId}] Forced full native DevTools render dirty rect {size.Width:0.##}x{size.Height:0.##}.");
        }
    }
}

[HarmonyPatch]
public static class NativeDevToolsServerCompositionTargetRenderPatch
{
    private static int _loggedExceptions;

    public static MethodBase TargetMethod()
    {
        var serverCompositionTarget = AccessTools.TypeByName("Avalonia.Rendering.Composition.Server.ServerCompositionTarget")
            ?? throw new MissingMemberException("Avalonia.Rendering.Composition.Server.ServerCompositionTarget");
        return AccessTools.Method(serverCompositionTarget, "Render")
            ?? throw new MissingMethodException(serverCompositionTarget.FullName, "Render");
    }

    public static Exception? Finalizer(object __instance, Exception? __exception)
    {
        if (__exception != null && Interlocked.Increment(ref _loggedExceptions) <= 10)
        {
            Log.Default.Error(
                $"[{Plugin.PluginId}] Native DevTools ServerCompositionTarget.Render failed " +
                $"for {__instance.GetType().FullName}: {__exception}");
        }

        return __exception;
    }
}

[HarmonyPatch]
public static class NativeDevToolsFullDirtyRectPatch
{
    private static int _loggedExpansions;

    public static MethodBase TargetMethod()
    {
        var serverCompositionTarget = AccessTools.TypeByName("Avalonia.Rendering.Composition.Server.ServerCompositionTarget")
            ?? throw new MissingMemberException("Avalonia.Rendering.Composition.Server.ServerCompositionTarget");
        return AccessTools.Method(serverCompositionTarget, "AddDirtyRect", new[] { typeof(Rect) })
            ?? throw new MissingMethodException(serverCompositionTarget.FullName, "AddDirtyRect");
    }

    public static void Prefix(object __instance, ref Rect rect)
    {
        if ((rect.Width == 0.0 && rect.Height == 0.0) || !NativeDevToolsWindowContext.IsNativeCompositionTarget(__instance))
        {
            return;
        }

        if (__instance.GetType()
                .GetProperty("Size", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?
                .GetValue(__instance) is not Size size ||
            size.Width <= 0.0 ||
            size.Height <= 0.0)
        {
            return;
        }

        rect = new Rect(0, 0, size.Width, size.Height);
        if (Interlocked.Increment(ref _loggedExpansions) <= 5)
        {
            Log.Default.Info($"[{Plugin.PluginId}] Expanded native DevTools composition dirty rect to full target {size.Width:0.##}x{size.Height:0.##}.");
        }
    }
}

[HarmonyPatch]
public static class NativeDevToolsVrageRenderTargetRedirectPatch
{
    public static MethodBase TargetMethod()
    {
        var avaloniaApp = AccessTools.TypeByName("Keen.VRage.UI.AvaloniaInterface.AvaloniaApp")
            ?? throw new MissingMemberException("Keen.VRage.UI.AvaloniaInterface.AvaloniaApp");
        return AccessTools.Method(avaloniaApp, "CreateRenderTarget")
            ?? throw new MissingMethodException(avaloniaApp.FullName, "CreateRenderTarget");
    }

    public static bool Prefix(IEnumerable<object> surfaces, ref IRenderTarget __result)
    {
        if (!NativeDevToolsWindowContext.IsRenderingNative)
        {
            return true;
        }

        if (!NativeDevToolsWindowContext.TryCreateNativeRenderTarget(surfaces, out var renderTarget))
        {
            return true;
        }

        __result = renderTarget;
        return false;
    }
}

public static class NativeDevToolsSkiaGlyphRunFallbackPatch
{
    private static int _loggedFallbacks;

    public static bool Prefix(object __instance, object[] __args)
    {
        var foreground = __args.Length > 0 ? __args[0] as IBrush : null;
        var glyphRun = __args.Length > 1 ? __args[1] as IGlyphRunImpl : null;
        if (glyphRun == null)
        {
            return true;
        }

        if (glyphRun.GetType().FullName?.StartsWith("Keen.VRage.UI.AvaloniaInterface.Text.", StringComparison.Ordinal) != true)
        {
            return true;
        }

        var text = glyphRun.ToString();
        if (string.IsNullOrEmpty(text) || foreground == null)
        {
            return false;
        }

        try
        {
            var canvas = (SKCanvas?)__instance.GetType()
                .GetProperty("Canvas", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?
                .GetValue(__instance);
            if (canvas == null)
            {
                return false;
            }

            using var paint = new SKPaint
            {
                IsAntialias = true,
                TextSize = Math.Max(1f, (float)GetGlyphDouble(glyphRun, "FontRenderingEmSize", 14.0)),
                Color = GetTextColor(foreground)
            };

            var baseline = GetGlyphPoint(glyphRun, "BaselineOrigin");
            canvas.DrawText(text, (float)baseline.X, (float)baseline.Y, paint);

            if (Interlocked.Increment(ref _loggedFallbacks) <= 5)
            {
                Log.Default.Info($"[{Plugin.PluginId}] Rendered Keen glyph run via Skia text fallback: \"{text}\".");
            }
        }
        catch (Exception e)
        {
            if (Interlocked.Increment(ref _loggedFallbacks) <= 10)
            {
                Log.Default.Error($"[{Plugin.PluginId}] Failed to render Keen glyph run via Skia text fallback: {e}");
            }
        }

        return false;
    }

    private static double GetGlyphDouble(IGlyphRunImpl glyphRun, string propertyName, double fallback)
    {
        return glyphRun.GetType()
            .GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?
            .GetValue(glyphRun) is double value
            ? value
            : fallback;
    }

    private static Point GetGlyphPoint(IGlyphRunImpl glyphRun, string propertyName)
    {
        return glyphRun.GetType()
            .GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?
            .GetValue(glyphRun) is Point value
            ? value
            : default;
    }

    private static SKColor GetTextColor(IBrush brush)
    {
        if (brush is ISolidColorBrush solid)
        {
            var color = solid.Color;
            var alpha = Math.Clamp(solid.Opacity, 0.0, 1.0);
            return new SKColor(color.R, color.G, color.B, (byte)(color.A * alpha));
        }

        return SKColors.Black;
    }
}

public static class NativeDevToolsSkiaGeometryFallbackPatch
{
    private static int _loggedFallbacks;

    public static bool Prefix(object __instance, object[] __args)
    {
        var brush = __args.Length > 0 ? __args[0] as IBrush : null;
        var pen = __args.Length > 1 ? __args[1] as IPen : null;
        var geometry = __args.Length > 2 ? __args[2] as IGeometryImpl : null;
        if (geometry == null)
        {
            return true;
        }

        if (geometry.GetType().FullName?.StartsWith("Keen.VRage.UI.AvaloniaInterface.Rendering.", StringComparison.Ordinal) != true)
        {
            return true;
        }

        try
        {
            var canvas = (SKCanvas?)__instance.GetType()
                .GetProperty("Canvas", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?
                .GetValue(__instance);
            if (canvas == null)
            {
                return false;
            }

            using var path = BuildPath(geometry);
            if (path.IsEmpty)
            {
                return false;
            }

            if (brush != null)
            {
                using var fill = new SKPaint
                {
                    IsAntialias = true,
                    Style = SKPaintStyle.Fill,
                    Color = NativeDevToolsSkiaFallbackHelpers.GetTextColor(brush)
                };
                canvas.DrawPath(path, fill);
            }

            if (pen?.Brush != null)
            {
                using var stroke = new SKPaint
                {
                    IsAntialias = true,
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = Math.Max(1f, (float)pen.Thickness),
                    Color = NativeDevToolsSkiaFallbackHelpers.GetTextColor(pen.Brush)
                };
                canvas.DrawPath(path, stroke);
            }

            if (Interlocked.Increment(ref _loggedFallbacks) <= 5)
            {
                Log.Default.Info($"[{Plugin.PluginId}] Rendered Keen geometry via Skia path fallback: {geometry.GetType().Name}.");
            }
        }
        catch (Exception e)
        {
            if (Interlocked.Increment(ref _loggedFallbacks) <= 10)
            {
                Log.Default.Error($"[{Plugin.PluginId}] Failed to render Keen geometry via Skia path fallback: {e}");
            }
        }

        return false;
    }

    private static SKPath BuildPath(IGeometryImpl geometry)
    {
        var path = new SKPath();
        var mutablePath = geometry.GetType()
            .GetField("MutablePath", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?
            .GetValue(geometry) as System.Collections.IEnumerable;
        if (mutablePath == null)
        {
            return path;
        }

        var hasCurrentPoint = false;
        SKPoint current = default;
        foreach (var segment in mutablePath)
        {
            if (!TryGetPoint(segment, "From", out var from) ||
                !TryGetPoint(segment, "Control", out var control) ||
                !TryGetPoint(segment, "To", out var to))
            {
                continue;
            }

            if (!hasCurrentPoint || DistanceSquared(current, from) > 0.001f)
            {
                path.MoveTo(from);
            }

            path.QuadTo(control, to);
            current = to;
            hasCurrentPoint = true;
        }

        return path;
    }

    private static bool TryGetPoint(object segment, string fieldName, out SKPoint point)
    {
        point = default;
        var value = segment.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(segment);
        if (value == null)
        {
            return false;
        }

        var x = value.GetType().GetField("X", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(value);
        var y = value.GetType().GetField("Y", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(value);
        if (x == null || y == null)
        {
            return false;
        }

        point = new SKPoint(Convert.ToSingle(x), Convert.ToSingle(y));
        return true;
    }

    private static float DistanceSquared(SKPoint a, SKPoint b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return dx * dx + dy * dy;
    }
}

internal static class NativeDevToolsSkiaFallbackHelpers
{
    public static SKColor GetTextColor(IBrush brush)
    {
        if (brush is ISolidColorBrush solid)
        {
            var color = solid.Color;
            var alpha = Math.Clamp(solid.Opacity, 0.0, 1.0);
            return new SKColor(color.R, color.G, color.B, (byte)(color.A * alpha));
        }

        return SKColors.Black;
    }
}
#endif
