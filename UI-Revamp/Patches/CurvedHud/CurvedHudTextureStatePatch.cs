using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using HarmonyLib;
using Keen.VRage.Library.Diagnostics;
using Keen.VRage.Library.Utils;
using UI_Revamp.CurvedHud;
using Vortice.Direct3D12;

namespace UI_Revamp.Patches.CurvedHud;

/// <summary>
/// OffscreenUIRenderer finishes by copying the scratch render target into the
/// persistent offscreen texture. CopyResource leaves that texture in COPY_DEST.
/// Normal file textures are already shader-readable, so SpriteRenderer does not
/// explicitly transition textures before binding their managed descriptor.
///
/// Generated offscreen textures therefore need an explicit transition before
/// the sprite renderer samples them. Restrict the transition to the curved-HUD
/// target so the normal UI rendering path is unchanged.
/// </summary>
[HarmonyPatch]
internal static class CurvedHudGeneratedTextureStatePatch
{
    const string OffscreenTargetTypeName =
        "Keen.VRage.Render12.SceneSystem.Components.OffscreenRenderTargetComponent";

    static readonly object ReflectionSync = new();

    static Type? _offscreenTargetType;
    static PropertyInfo? _batchTextureProperty;
    static PropertyInfo? _resourceHandleProperty;
    static PropertyInfo? _offscreenTextureProperty;
    static PropertyInfo? _autoResourceStateProperty;
    static MethodInfo? _explicitStateTransitionMethod;
    static int _loggedTransition;

    static MethodBase TargetMethod()
    {
        Type spriteRenderer = AccessTools.TypeByName(
            "Keen.VRage.Render12.UIStage.Sprites.SpriteRenderer")
            ?? throw new MissingMemberException("SpriteRenderer not found.");

        return AccessTools.GetDeclaredMethods(spriteRenderer).Single(method =>
        {
            ParameterInfo[] parameters = method.GetParameters();
            return method.Name == "Draw" &&
                   parameters.Length == 4 &&
                   parameters[3].ParameterType.Name == "SpriteBatch";
        });
    }

    // ReSharper disable once InconsistentNaming
    static void Prefix(object[] __args)
    {
        if (!CurvedHudController.HasValidTarget ||
            !CurvedHudController.Target.IsValid ||
            __args.Length < 4)
        {
            return;
        }

        object commandList = __args[0];
        object spriteBatch = __args[3];
        object managedTexture = GetBatchTexture(spriteBatch)
            ?? throw new InvalidOperationException(
                "SpriteBatch.Texture is null while the curved HUD target is active.");

        // SpriteRenderer.Draw is called for every UI sprite. The old patch
        // cached ResourceHandle from the first OffscreenRenderTargetComponent
        // and then invoked that PropertyInfo on a later FileTexture, which is
        // the TargetException seen in the crash log. Filter by the declaring
        // runtime type before touching any offscreen-component properties.
        Type offscreenTargetType = GetOffscreenTargetType();
        if (!offscreenTargetType.IsInstanceOfType(managedTexture))
            return;

        EnsureReflection(commandList, offscreenTargetType, managedTexture);

        object resourceHandleValue = _resourceHandleProperty!.GetValue(managedTexture)
            ?? throw new InvalidOperationException(
                "Curved HUD OffscreenRenderTargetComponent.ResourceHandle is null.");

        if (resourceHandleValue is not ResourceHandle resourceHandle)
        {
            throw new InvalidCastException(
                $"OffscreenRenderTargetComponent.ResourceHandle returned " +
                $"'{resourceHandleValue.GetType().FullName}', expected " +
                $"'{typeof(ResourceHandle).FullName}'.");
        }

        if (resourceHandle != CurvedHudController.CompositionTextureHandle)
            return;

        object offscreenTexture = _offscreenTextureProperty!.GetValue(managedTexture)
            ?? throw new InvalidOperationException(
                "Curved HUD OffscreenRenderTargetComponent.Texture is null.");

        object autoResourceState = _autoResourceStateProperty!.GetValue(offscreenTexture)
            ?? throw new InvalidOperationException(
                "Curved HUD ROTexture.AutoResourceState is null.");

        _explicitStateTransitionMethod!.Invoke(
            commandList,
            new[]
            {
                autoResourceState,
                (object)ResourceStates.PixelShaderResource,
                false
            });

        if (Interlocked.Exchange(ref _loggedTransition, 1) == 0)
        {
            Log.Default.Info(
                $"[{Plugin.PluginId}] Transitioned curved HUD offscreen texture " +
                "from its copy state to PIXEL_SHADER_RESOURCE before SpriteRenderer.Draw.");
        }
    }

    static Type GetOffscreenTargetType()
    {
        return _offscreenTargetType ??= AccessTools.TypeByName(OffscreenTargetTypeName)
            ?? throw new MissingMemberException(
                $"{OffscreenTargetTypeName} not found.");
    }

    static object? GetBatchTexture(object spriteBatch)
    {
        _batchTextureProperty ??= AccessTools.Property(
            spriteBatch.GetType(),
            "Texture")
            ?? throw new MissingMemberException(
                $"{spriteBatch.GetType().FullName}.Texture not found.");

        return _batchTextureProperty.GetValue(spriteBatch);
    }

    static void EnsureReflection(
        object commandList,
        Type offscreenTargetType,
        object managedTexture)
    {
        if (_explicitStateTransitionMethod != null)
            return;

        lock (ReflectionSync)
        {
            if (_explicitStateTransitionMethod != null)
                return;

            _resourceHandleProperty = AccessTools.Property(
                offscreenTargetType,
                "ResourceHandle")
                ?? throw new MissingMemberException(
                    "OffscreenRenderTargetComponent.ResourceHandle not found.");

            _offscreenTextureProperty = AccessTools.Property(
                offscreenTargetType,
                "Texture")
                ?? throw new MissingMemberException(
                    "OffscreenRenderTargetComponent.Texture not found.");

            object offscreenTexture = _offscreenTextureProperty.GetValue(managedTexture)
                ?? throw new InvalidOperationException(
                    "Curved HUD OffscreenRenderTargetComponent.Texture is null.");

            _autoResourceStateProperty = AccessTools.Property(
                offscreenTexture.GetType(),
                "AutoResourceState")
                ?? throw new MissingMemberException(
                    "ROTexture.AutoResourceState not found.");

            _explicitStateTransitionMethod = commandList.GetType()
                .GetMethods(BindingFlags.Instance |
                            BindingFlags.Public |
                            BindingFlags.NonPublic)
                .Single(method =>
                {
                    ParameterInfo[] parameters = method.GetParameters();
                    return method.Name == "ExplicitStateTransition" &&
                           parameters.Length == 3 &&
                           parameters[1].ParameterType == typeof(ResourceStates) &&
                           parameters[2].ParameterType == typeof(bool);
                });
        }
    }
}
