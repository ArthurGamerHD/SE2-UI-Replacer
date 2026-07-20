using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using HarmonyLib;
using Keen.VRage.Library.Diagnostics;
using Keen.VRage.Library.Mathematics;
using Keen.VRage.Library.Utils;
using UI_Revamp.CurvedHud;

namespace UI_Revamp.Patches.CurvedHud;

/// <summary>
/// Space Engineers' normal UI image path asks every ResourceHandle for graphics
/// metadata. That works for GUID-backed content assets, but an offscreen target
/// is deliberately backed by a GeneratedResourceHandle and therefore has no
/// GUID metadata to query.
///
/// Do not patch UISystemComponent.TryExtractGraphicsType: the method contains an
/// exception filter that Harmony/MonoMod cannot safely rewrite on the current
/// SE2 runtime. Instead, intercept only the DrawImage call carrying our
/// offscreen handle and submit it directly to UIBatcher. The offscreen target
/// stores premultiplied RGB and matching source-over alpha, so the stock
/// non-PREMULTIPLY_ALPHA sprite variant must sample it without multiplying the
/// texture by alpha a second time. ManagedTextureManager already knows this
/// generated handle because OffscreenRenderTargetComponent registers it when
/// the target is created.
/// </summary>
[HarmonyPatch]
internal static class CurvedHudGeneratedTextureDrawPatch
{
    static readonly object ReflectionSync = new();

    static FieldInfo? _uiBatcherField;
    static object? _managedTextures;
    static MethodInfo? _getTexture;
    static MethodInfo? _getSpriteBatch;
    static MethodInfo? _addSprite;
    static int _loggedFirstDraw;

    static MethodBase TargetMethod()
    {
        Type recorderType = AccessTools.TypeByName(
            "Keen.VRage.Render12.SceneSystem.Components.UISystemComponent+UIBatchRecorder")
            ?? throw new MissingMemberException(
                "UISystemComponent.UIBatchRecorder not found.");

        return AccessTools.GetDeclaredMethods(recorderType).Single(method =>
            method.Name == "DrawImage" &&
            method.GetParameters().Length == 6);
    }

    /// <returns>
    /// False for our generated composition texture so the GUID-only original
    /// method is skipped; true for every normal game image.
    /// </returns>
    // ReSharper disable once InconsistentNaming
    static bool Prefix(object __instance, object[] __args)
    {
        // __args avoids relying on Harmony's handling of the original method's
        // readonly-ref (`in`) value-type parameters.
        ResourceHandle image = (ResourceHandle)__args[0];

        if (!CurvedHudController.HasValidTarget ||
            image != CurvedHudController.CompositionTextureHandle)
        {
            return true;
        }

        BoundingBox2 destination = (BoundingBox2)__args[1];
        ColorSRGB color = (ColorSRGB)__args[2];
        bool ignoreBounds = (bool)__args[3];
        ResourceHandle? maskTexture =
            __args[4] is ResourceHandle maskHandle ? maskHandle : null;
        BoundingBox2I? sourceRectangle =
            __args[5] is BoundingBox2I source ? source : null;

        DrawGeneratedTexture(
            __instance,
            image,
            in destination,
            color,
            ignoreBounds,
            maskTexture,
            in sourceRectangle);

        if (Interlocked.Exchange(ref _loggedFirstDraw, 1) == 0)
        {
            Log.Default.Info(
                $"[{Plugin.PluginId}] Drawing curved HUD generated texture " +
                "without GUID metadata lookup using the stock premultiplied-texture " +
                "composition path.");
        }

        return false;
    }

    static void DrawGeneratedTexture(
        object recorder,
        ResourceHandle image,
        in BoundingBox2 destination,
        ColorSRGB color,
        bool ignoreBounds,
        ResourceHandle? maskTexture,
        in BoundingBox2I? sourceRectangle)
    {
        EnsureReflection(recorder);

        object uiBatcher = _uiBatcherField!.GetValue(recorder)
            ?? throw new InvalidOperationException(
                "UIBatchRecorder._uiBatcher is not active.");

        object texture = _getTexture!.Invoke(
            _managedTextures,
            new object?[] { image })
            ?? throw new InvalidOperationException(
                "ManagedTextures.GetTexture returned null for the curved HUD target.");

        object? mask = null;
        if (maskTexture.HasValue)
        {
            mask = _getTexture.Invoke(
                _managedTextures,
                new object?[] { maskTexture.Value });
        }

        // The generated target now contains premultiplied RGB with matching
        // source-over alpha. Select the stock shader variant that consumes an
        // already-premultiplied texture; the PREMULTIPLY_ALPHA variant is for
        // straight-alpha file textures and would multiply this layer twice.
        object spriteBatch = _getSpriteBatch!.Invoke(
            uiBatcher,
            new object?[] { texture, ignoreBounds, false, mask })
            ?? throw new InvalidOperationException(
                "UIBatcher.GetSpriteBatch returned null.");

        // This is the same data UISystemComponent.DrawImageExt supplies for an
        // unrotated image: center pivot, UnitX tangent, optional source rect,
        // and the requested destination rectangle.
        object?[] addArguments =
        {
            color,
            destination.Center,
            Vector2.UnitX,
            sourceRectangle,
            destination
        };

        _addSprite!.Invoke(spriteBatch, addArguments);
    }

    static void EnsureReflection(object recorder)
    {
        if (_addSprite != null)
            return;

        lock (ReflectionSync)
        {
            if (_addSprite != null)
                return;

            _uiBatcherField = AccessTools.Field(
                recorder.GetType(),
                "_uiBatcher")
                ?? throw new MissingFieldException(
                    "UIBatchRecorder._uiBatcher not found.");

            Type coreSystems =
                AccessTools.TypeByName("Keen.VRage.Render12.Core.CoreSystems")
                ?? AccessTools.TypeByName(
                    "Keen.VRage.Render12.Core.Systems.CoreSystems")
                ?? throw new MissingMemberException("CoreSystems not found.");

            _managedTextures =
                AccessTools.Field(coreSystems, "ManagedTextures")?.GetValue(null)
                ?? AccessTools.Property(coreSystems, "ManagedTextures")?.GetValue(null)
                ?? throw new MissingMemberException(
                    "CoreSystems.ManagedTextures not found.");

            _getTexture = AccessTools.GetDeclaredMethods(
                    _managedTextures.GetType())
                .Single(method =>
                {
                    ParameterInfo[] parameters = method.GetParameters();
                    return method.Name == "GetTexture" &&
                           parameters.Length == 1 &&
                           parameters[0].ParameterType == typeof(ResourceHandle);
                });

            object uiBatcher = _uiBatcherField.GetValue(recorder)
                ?? throw new InvalidOperationException(
                    "UIBatchRecorder._uiBatcher is not active while initializing curved HUD reflection.");

            _getSpriteBatch = AccessTools.GetDeclaredMethods(uiBatcher.GetType())
                .Single(method =>
                    method.Name == "GetSpriteBatch" &&
                    method.GetParameters().Length == 4);

            Type spriteBatchType = _getSpriteBatch.ReturnType;
            _addSprite = AccessTools.GetDeclaredMethods(spriteBatchType)
                .Single(method =>
                {
                    ParameterInfo[] parameters = method.GetParameters();
                    return method.Name == "Add" &&
                           parameters.Length == 5 &&
                           parameters[0].ParameterType == typeof(ColorSRGB);
                });
        }
    }
}