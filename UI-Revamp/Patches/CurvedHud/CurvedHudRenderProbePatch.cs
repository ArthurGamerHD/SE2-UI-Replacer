using System;
using System.Collections;
using System.Reflection;
using System.Threading;
using HarmonyLib;
using Keen.VRage.Library.Diagnostics;
using Keen.VRage.Library.Utils;
using Keen.VRage.Render.FrameData;
using UI_Revamp.CurvedHud;

namespace UI_Revamp.Patches.CurvedHud;

/// <summary>
/// Confirms that the render thread actually dequeues and records the curved HUD
/// offscreen target. DrawingContextImpl.Submit only proves that the main thread
/// queued a command buffer; these probes cover the render-side half.
/// </summary>
[HarmonyPatch]
internal static class CurvedHudOffscreenDrawOneProbePatch
{
    [ThreadStatic] static bool _renderingCurvedHud;

    static int _loggedDrawOne;

    internal static bool IsRenderingCurvedHud => _renderingCurvedHud;

    static MethodBase TargetMethod() => AccessTools.Method(
        AccessTools.TypeByName("Keen.VRage.Render12.UIStage.OffscreenUIRenderer")
            ?? throw new MissingMemberException("OffscreenUIRenderer not found."),
        "DrawOne") ?? throw new MissingMethodException(
        "OffscreenUIRenderer.DrawOne not found.");

    // ReSharper disable once InconsistentNaming
    static void Prefix(object __2, out bool __state)
    {
        __state = _renderingCurvedHud;
        if (!IsCurvedHudTarget(__2))
            return;

        _renderingCurvedHud = true;
        CurvedHudController.NotifyOffscreenRenderStarted();
        if (Interlocked.Exchange(ref _loggedDrawOne, 1) == 0)
        {
            object? resolution = AccessTools.Property(__2.GetType(), "Resolution")
                ?.GetValue(__2);
            Log.Default.Info(
                $"[{Plugin.PluginId}] Render thread entered " +
                $"OffscreenUIRenderer.DrawOne for the curved HUD target; " +
                $"resolution={resolution ?? "<unknown>"}.");
        }
    }

    // ReSharper disable once InconsistentNaming
    static Exception? Finalizer(bool __state, Exception? __exception)
    {
        _renderingCurvedHud = __state;
        return __exception;
    }

    static bool IsCurvedHudTarget(object target)
    {
        if (!CurvedHudController.HasValidTarget ||
            !CurvedHudController.Target.IsValid)
        {
            return false;
        }

        object? handle = AccessTools.Property(target.GetType(), "Handle")
            ?.GetValue(target);

        return handle is GeneratedResourceHandle generated &&
               generated == new GeneratedResourceHandle(CurvedHudController.Target.Id);
    }
}

[HarmonyPatch]
internal static class CurvedHudRecordBatchesProbePatch
{
    static int _loggedTargetCommands;

    static MethodBase TargetMethod() => AccessTools.Method(
        AccessTools.TypeByName(
            "Keen.VRage.Render12.SceneSystem.Components.UISystemComponent")
            ?? throw new MissingMemberException("UISystemComponent not found."),
        "RecordBatches") ?? throw new MissingMethodException(
        "UISystemComponent.RecordBatches not found.");

    // ReSharper disable once InconsistentNaming
    static void Prefix(object __instance, GeneratedResourceHandle __1)
    {
        if (!CurvedHudOffscreenDrawOneProbePatch.IsRenderingCurvedHud ||
            Interlocked.Exchange(ref _loggedTargetCommands, 1) != 0)
        {
            return;
        }

        int layerCount = 0;
        int commandBufferCount = 0;

        object? targetCommands = AccessTools.Field(
                __instance.GetType(),
                "_targetCommands")
            ?.GetValue(__instance);

        if (targetCommands is IEnumerable targets)
        {
            foreach (object targetEntry in targets)
            {
                Type entryType = targetEntry.GetType();
                object? key = entryType.GetProperty("Key")?.GetValue(targetEntry);
                if (key is not GeneratedResourceHandle handle || handle != __1)
                    continue;

                object? layers = entryType.GetProperty("Value")?.GetValue(targetEntry);
                if (layers is not IEnumerable layerEntries)
                    break;

                foreach (object layerEntry in layerEntries)
                {
                    layerCount++;
                    object? commandBuffers = layerEntry.GetType()
                        .GetProperty("Value")
                        ?.GetValue(layerEntry);
                    object? count = commandBuffers?.GetType()
                        .GetProperty("Count", BindingFlags.Instance |
                                              BindingFlags.Public |
                                              BindingFlags.NonPublic)
                        ?.GetValue(commandBuffers);
                    if (count is int value)
                        commandBufferCount += value;
                }

                break;
            }
        }

        Log.Default.Info(
            $"[{Plugin.PluginId}] Curved HUD render-side target list contains " +
            $"{layerCount} sort layer(s) and {commandBufferCount} command buffer(s).");
    }
}

[HarmonyPatch]
internal static class CurvedHudOffscreenBeginDrawProbePatch
{
    static int _loggedBeginDraw;

    static MethodBase TargetMethod() => AccessTools.Method(
        AccessTools.TypeByName("Keen.VRage.Render12.UIStage.BatchBase.UIBatcher")
            ?? throw new MissingMemberException("UIBatcher not found."),
        "BeginDraw") ?? throw new MissingMethodException(
        "UIBatcher.BeginDraw not found.");

    // ReSharper disable once InconsistentNaming
    static void Postfix(bool __result)
    {
        if (!CurvedHudOffscreenDrawOneProbePatch.IsRenderingCurvedHud ||
            Interlocked.Exchange(ref _loggedBeginDraw, 1) != 0)
        {
            return;
        }

        Log.Default.Info(
            $"[{Plugin.PluginId}] Curved HUD offscreen UIBatcher.BeginDraw " +
            $"returned {__result}; " +
            (__result
                ? "the target contains at least one GPU UI batch."
                : "the target was cleared but had no drawable UI batches."));
    }
}


/// <summary>
/// Logs each render-thread handoff that should turn an offscreen draw batch into
/// an OffscreenUIRenderer request. This distinguishes a lost UI submission from
/// a pending request that the renderer never services.
/// </summary>
[HarmonyPatch]
internal static class CurvedHudSubmitDrawBatchPipelineProbePatch
{
    static int _submissionCount;

    static MethodBase TargetMethod() => AccessTools.Method(
        AccessTools.TypeByName(
            "Keen.VRage.Render12.SceneSystem.Components.UISystemComponent")
            ?? throw new MissingMemberException("UISystemComponent not found."),
        "SubmitDrawBatch") ?? throw new MissingMethodException(
        "UISystemComponent.SubmitDrawBatch not found.");

    // ReSharper disable once InconsistentNaming
    static void Prefix(
        RenderDrawCommandBuffer __0,
        RenderDrawCommandBuffer? __1,
        int __2,
        bool __3)
    {
        if (!IsCurvedHudTarget(__0.RenderTarget))
            return;

        int count = Interlocked.Increment(ref _submissionCount);
        if (count <= 16)
        {
            Log.Default.Info(
                $"[{Plugin.PluginId}] Render thread received curved HUD " +
                $"UISystem.SubmitDrawBatch #{count}: layer={__2}, " +
                $"hasPrevious={__1 != null}, deletePrevious={__3}, " +
                $"debug='{__0.DebugString}'.");
        }
    }

    internal static bool IsCurvedHudTarget(GeneratedResourceHandle handle)
    {
        return CurvedHudController.HasValidTarget &&
               CurvedHudController.Target.IsValid &&
               handle == new GeneratedResourceHandle(CurvedHudController.Target.Id);
    }
}

[HarmonyPatch]
internal static class CurvedHudRequestRenderPipelineProbePatch
{
    static int _requestCount;

    static MethodBase TargetMethod() => AccessTools.Method(
        AccessTools.TypeByName("Keen.VRage.Render12.Utils.OffscreenTargetManager")
            ?? throw new MissingMemberException(
                "OffscreenTargetManager not found."),
        "RequestRender") ?? throw new MissingMethodException(
        "OffscreenTargetManager.RequestRender not found.");

    // ReSharper disable once InconsistentNaming
    static void Prefix(object __instance, GeneratedResourceHandle __0)
    {
        if (!CurvedHudSubmitDrawBatchPipelineProbePatch.IsCurvedHudTarget(__0))
            return;

        int count = Interlocked.Increment(ref _requestCount);
        if (count > 16)
            return;

        int registered = GetCollectionCount(__instance, "_registeredTextures");
        int pending = GetCollectionCount(__instance, "_pendingRenderList");
        Log.Default.Info(
            $"[{Plugin.PluginId}] OffscreenTargetManager.RequestRender #{count} " +
            $"for curved HUD; registeredTargets={registered}, " +
            $"pendingBefore={pending}.");
    }

    static int GetCollectionCount(object instance, string fieldName)
    {
        object? value = AccessTools.Field(instance.GetType(), fieldName)
            ?.GetValue(instance);
        return value is ICollection collection ? collection.Count : -1;
    }
}

[HarmonyPatch]
internal static class CurvedHudTargetInitializePipelineProbePatch
{
    static int _loggedInitialize;

    static MethodBase TargetMethod() => AccessTools.Method(
        AccessTools.TypeByName(
            "Keen.VRage.Render12.SceneSystem.Components.OffscreenRenderTargetComponent")
            ?? throw new MissingMemberException(
                "OffscreenRenderTargetComponent not found."),
        "Initialize") ?? throw new MissingMethodException(
        "OffscreenRenderTargetComponent.Initialize not found.");

    // ReSharper disable once InconsistentNaming
    static void Postfix(object __instance)
    {
        if (!CurvedHudController.HasValidTarget ||
            !CurvedHudController.Target.IsValid)
        {
            return;
        }

        object? entityId = AccessTools.Property(__instance.GetType(), "EntityId")
            ?.GetValue(__instance);
        if (entityId is not RenderId renderId ||
            !renderId.Equals(CurvedHudController.Target.Id))
        {
            return;
        }

        if (Interlocked.Exchange(ref _loggedInitialize, 1) != 0)
            return;

        object? name = AccessTools.Property(__instance.GetType(), "Name")
            ?.GetValue(__instance);
        object? resolution = AccessTools.Property(__instance.GetType(), "Resolution")
            ?.GetValue(__instance);
        Log.Default.Info(
            $"[{Plugin.PluginId}] Curved HUD OffscreenRenderTargetComponent " +
            $"initialized and registered; id={renderId}, name='{name}', " +
            $"resolution={resolution}.");
    }
}
