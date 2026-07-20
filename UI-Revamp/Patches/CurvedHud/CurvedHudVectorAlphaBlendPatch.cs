using System;
using System.Reflection;
using HarmonyLib;
using Keen.VRage.Library.Diagnostics;
using Vortice.Direct3D12;

namespace UI_Revamp.Patches.CurvedHud;

/// <summary>
/// SE2's Slug/vector blend state intentionally preserves destination alpha.
/// That is fine when vectors are drawn directly over the already-opaque game
/// image, but it is incorrect for a transparent offscreen layer: RGB receives
/// source-alpha blending while alpha remains at the clear value of zero.
///
/// Make the alpha channel use normal source-over accumulation. The RGB blend
/// equation is left exactly as SE2 defines it, so direct game-UI rendering is
/// visually unchanged. Offscreen vector output then contains premultiplied RGB
/// plus matching coverage alpha and can be composed like an ordinary layer.
/// </summary>
[HarmonyPatch]
internal static class CurvedHudVectorAlphaBlendPatch
{
    static MethodBase TargetMethod()
    {
        Type managerType = AccessTools.TypeByName(
            "Keen.VRage.Render12.Resources.PipelineStates.BlendStateManager")
            ?? throw new MissingMemberException("BlendStateManager not found.");

        return AccessTools.Method(managerType, "CreateSlugBlendDescription")
            ?? throw new MissingMethodException(
                managerType.FullName,
                "CreateSlugBlendDescription");
    }

    // ReSharper disable once InconsistentNaming
    static void Postfix(ref BlendDescription __result)
    {
        RenderTargetBlendDescription target = __result.RenderTarget[0];

        // Keep SE2's RGB equation:
        //   rgb = source.rgb * source.a + destination.rgb * (1 - source.a)
        //
        // Replace only the old alpha equation (0 + destination.a), with:
        //   a = source.a + destination.a * (1 - source.a)
        target.SourceBlendAlpha = Blend.One;
        target.DestinationBlendAlpha = Blend.InverseSourceAlpha;
        target.BlendOperationAlpha = BlendOperation.Add;
        target.RenderTargetWriteMask = ColorWriteEnable.All;

        __result.RenderTarget[0] = target;

        Log.Default.Info(
            $"[{Plugin.PluginId}] Enabled source-over alpha accumulation for " +
            "vector UI rendering so translucent brushes retain their opacity " +
            "inside the curved HUD offscreen texture.");
    }
}
