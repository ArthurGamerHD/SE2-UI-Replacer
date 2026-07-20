using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using HarmonyLib;
using Keen.VRage.Library.Diagnostics;
using Keen.VRage.Library.Utils;
using Keen.VRage.Render.Contracts;
using UI_Revamp.CurvedHud;

namespace UI_Revamp.Patches.CurvedHud;

[HarmonyPatch]
internal static class CurvedHudDrawingContextInitPatch
{
    // Avalonia creates multiple drawing contexts for one window, including
    // temporary contexts for layers. Track every context initialized for the
    // curved HUD window instead of remembering only the latest one.
    //
    // DrawingContextImpl instances come from a pool, so the marker must be
    // removed on Dispose before the object can be reused for another window.
    static readonly ConditionalWeakTable<object, object> CurvedHudContexts = new();
    static readonly object CurvedHudContextMarker = new();

    static MethodBase TargetMethod() => AccessTools.Method(
        AccessTools.TypeByName("Keen.VRage.UI.AvaloniaInterface.Rendering.DrawingContextImpl")
            ?? throw new MissingMemberException("DrawingContextImpl not found."),
        "Init") ?? throw new MissingMethodException("DrawingContextImpl.Init not found.");

    // ReSharper disable once InconsistentNaming
    static void Prefix(object __instance, object renderingWindow)
    {
        // A pooled DrawingContextImpl may previously have belonged to another
        // window. Remove stale ownership before assigning the new one.
        CurvedHudContexts.Remove(__instance);

        if (!CurvedHudController.TryCapturePlatformWindow(renderingWindow))
            return;

        CurvedHudContexts.Add(__instance, CurvedHudContextMarker);
    }

    internal static bool IsCurvedHudContext(object drawingContext)
    {
        return CurvedHudContexts.TryGetValue(drawingContext, out _);
    }

    internal static void ReleaseContext(object drawingContext)
    {
        CurvedHudContexts.Remove(drawingContext);
    }
}

[HarmonyPatch]
internal static class CurvedHudDrawingContextDisposePatch
{
    static MethodBase TargetMethod() => AccessTools.Method(
        AccessTools.TypeByName("Keen.VRage.UI.AvaloniaInterface.Rendering.DrawingContextImpl")
            ?? throw new MissingMemberException("DrawingContextImpl not found."),
        "Dispose") ?? throw new MissingMethodException("DrawingContextImpl.Dispose not found.");

    // ReSharper disable once InconsistentNaming
    static void Prefix(object __instance)
    {
        // DrawingContextImpl returns itself to a pool during Dispose. Clear the
        // curved-window marker before that pooled object can be initialized for
        // a different Avalonia window.
        CurvedHudDrawingContextInitPatch.ReleaseContext(__instance);
    }
}

[HarmonyPatch(typeof(ImmediateDrawBatch), nameof(ImmediateDrawBatch.Dispose))]
internal static class CurvedHudImmediateBatchDisposePatch
{
    // ReSharper disable once InconsistentNaming
    static void Prefix(ImmediateDrawBatch __instance, out bool __state)
    {
        __state = false;
        if (!CurvedHudDiagnostics.UseImmediateOffscreenBatch ||
            !CurvedHudController.HasValidTarget ||
            !CurvedHudController.Target.IsValid)
        {
            return;
        }

        var commandBuffer = __instance.CommandBuffer;
        if (commandBuffer == null)
            return;

        var target = new GeneratedResourceHandle(CurvedHudController.Target.Id);
        __state = commandBuffer.RenderTarget == target;
    }

    // ReSharper disable once InconsistentNaming
    static void Postfix(bool __state)
    {
        // ImmediateDrawBatch.Submit() is intentionally a no-op. Dispose() is
        // the exact queue point, whether Avalonia reaches it through Reset() or
        // through DrawingContextImpl.Dispose().
        if (__state)
            CurvedHudController.NotifyOffscreenSubmitted("ImmediateDrawBatch.Dispose");
    }
}

[HarmonyPatch]
internal static class CurvedHudCreateBatchPatch
{
    static int _loggedCrossTargetPredecessor;

    static MethodBase TargetMethod() => AccessTools.Method(
        AccessTools.TypeByName("Keen.VRage.UI.AvaloniaInterface.Rendering.DrawingContextImpl")
            ?? throw new MissingMemberException("DrawingContextImpl not found."),
        "CreateBatch") ?? throw new MissingMethodException("DrawingContextImpl.CreateBatch not found.");

    // ReSharper disable once InconsistentNaming
    static bool Prefix(
        object __instance,
        ref IDrawBatch? previousBatch,
        ref bool deletePrevious,
        ref IDrawBatch __result)
    {
        bool isCurvedHudContext =
            CurvedHudDrawingContextInitPatch.IsCurvedHudContext(__instance);

        // DrawingContextImpl.Reset uses another Avalonia context's batch as an
        // ordering predecessor. That assumption is valid only while every
        // context renders into the main view. Once one window is redirected to
        // an offscreen target, Avalonia can hand either target a predecessor
        // from the other target. UISystemComponent keeps an independent batch
        // list per RenderTarget, so such a predecessor can never be found in
        // the destination list.
        //
        // Remove only impossible cross-target ordering. Same-target
        // predecessors retain the stock replacement/layering semantics. This
        // check also handles the reverse transition (main-view context after a
        // curved-HUD context), for which this prefix returns to the stock batch
        // factory after clearing the invalid predecessor.
        if (previousBatch != null)
        {
            GeneratedResourceHandle curvedHudTarget =
                CurvedHudController.HasValidTarget &&
                CurvedHudController.Target.IsValid
                    ? new GeneratedResourceHandle(CurvedHudController.Target.Id)
                    : default;

            bool previousTargetsCurvedHud =
                previousBatch.CommandBuffer.RenderTarget == curvedHudTarget &&
                curvedHudTarget != default;

            if (previousTargetsCurvedHud != isCurvedHudContext)
            {
                previousBatch = null;
                deletePrevious = false;
                if (Interlocked.Exchange(
                        ref _loggedCrossTargetPredecessor,
                        1) == 0)
                {
                    Log.Default.Info(
                        $"[{Plugin.PluginId}] Detached an invalid cross-target " +
                        "Avalonia drawing-context predecessor.");
                }
            }
        }

        if (!isCurvedHudContext ||
            !CurvedHudController.HasValidTarget ||
            !CurvedHudController.Target.IsValid)
        {
            return true;
        }

        var uiSystem = AccessTools
            .Field(__instance.GetType(), "_uiSystem")
            ?.GetValue(__instance) as UISystem
            ?? throw new MissingFieldException(
                "DrawingContextImpl._uiSystem not found.");

        // Do not let the stock CreateBatch create a main-view batch first.
        // Test both lifetime models because an immediate batch is queued only
        // when Avalonia disposes the drawing context, while a persistent batch
        // is queued by DrawingContextImpl.Submit().
        if (CurvedHudDiagnostics.UseImmediateOffscreenBatch)
        {
            previousBatch = null;
            deletePrevious = false;
            __result = uiSystem.CreateImmediateBatchFor(
                CurvedHudController.Target,
                Constants.Composition.DrawingContextSortLayer,
                "UIRevamp.CurvedHud.Avalonia");
            return false;
        }

        __result = uiSystem.CreatePersistentBatchFor(
            CurvedHudController.Target,
            Constants.Composition.DrawingContextSortLayer,
            previousBatch,
            deletePrevious);

        return false;
    }
}

[HarmonyPatch]
internal static class CurvedHudDrawingContextSubmitPatch
{
    static MethodBase TargetMethod() => AccessTools.Method(
        AccessTools.TypeByName("Keen.VRage.UI.AvaloniaInterface.Rendering.DrawingContextImpl")
            ?? throw new MissingMemberException("DrawingContextImpl not found."),
        "Submit") ?? throw new MissingMethodException("DrawingContextImpl.Submit not found.");

    // ReSharper disable once InconsistentNaming
    static void Postfix(object __instance)
    {
        if (!CurvedHudDiagnostics.UseImmediateOffscreenBatch &&
            CurvedHudDrawingContextInitPatch.IsCurvedHudContext(__instance))
        {
            CurvedHudController.NotifyOffscreenSubmitted("DrawingContextImpl.Submit");
        }
    }
}
