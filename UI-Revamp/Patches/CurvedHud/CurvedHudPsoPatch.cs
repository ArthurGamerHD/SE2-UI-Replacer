using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using HarmonyLib;
using Keen.VRage.Core.Render;
using Keen.VRage.Core.Render.Data;
using Keen.VRage.Library.Diagnostics;
using UI_Revamp.CurvedHud;

namespace UI_Revamp.Patches.CurvedHud;

/// <summary>
/// Replaces the stock sprite PSO only while the one sprite backed by the curved
/// HUD offscreen target is drawn. The vertex shader remains stock; the pixel
/// shader points at the plugin's virtual overlay of the stock sprite shader.
/// </summary>
[HarmonyPatch]
internal static class CurvedHudPsoPatch
{
    sealed class CurvedPair
    {
        public required object Standard;
        public required object Premultiplied;
    }

    sealed class SwapState
    {
        public required IDictionary Dictionary;
        public required object Format;
        public required object Original;
    }

    static readonly Dictionary<object, CurvedPair> CurvedByFormat = new();
    static readonly object Sync = new();
    static int _loggedStockPso;

    static MethodBase TargetMethod()
    {
        Type type = AccessTools.TypeByName(
            "Keen.VRage.Render12.UIStage.Sprites.SpriteBatch")
            ?? throw new MissingMemberException("SpriteBatch not found.");

        return AccessTools.GetDeclaredMethods(type).Single(method =>
            method.Name == "Draw" &&
            method.GetParameters().Length == 3);
    }

    // ReSharper disable once InconsistentNaming
    static void Prefix(
        object __instance,
        object targetSetup,
        ref SwapState? __state)
    {
        if (!CurvedHudController.HasValidTarget)
            return;

        object? texture = AccessTools.Property(__instance.GetType(), "Texture")
            ?.GetValue(__instance);
        object? handle = texture == null
            ? null
            : AccessTools.Property(texture.GetType(), "ResourceHandle")
                ?.GetValue(texture);

        if (handle == null ||
            !handle.Equals(CurvedHudController.CompositionTextureHandle))
        {
            return;
        }

        if (CurvedHudDiagnostics.Mode ==
            CurvedHudDiagnostics.ShaderMode.StockPso)
        {
            if (Interlocked.Exchange(ref _loggedStockPso, 1) == 0)
            {
                Log.Default.Info(
                    $"[{Plugin.PluginId}] Curved HUD composition texture is " +
                    "using the stock sprite PSO for this diagnostic run.");
            }

            return;
        }

        // UITargetSetup.RenderTarget is declared as IRenderTargetView. The
        // concrete ResizableRWRenderTargetTexture implements Format explicitly,
        // so reflecting the runtime class cannot see the RTV format property.
        // Invoke the property through the exact declared interface contract.
        FieldInfo renderTargetField = AccessTools.Field(
            targetSetup.GetType(),
            "RenderTarget")
            ?? throw new MissingFieldException(
                targetSetup.GetType().FullName,
                "RenderTarget");

        object renderTarget = renderTargetField.GetValue(targetSetup)
            ?? throw new InvalidOperationException(
                "UITargetSetup.RenderTarget is null.");

        PropertyInfo formatProperty = AccessTools.Property(
            renderTargetField.FieldType,
            "Format")
            ?? throw new MissingMemberException(
                renderTargetField.FieldType.FullName,
                "Format");

        object format = formatProperty.GetValue(renderTarget)
            ?? throw new InvalidOperationException(
                $"{renderTargetField.FieldType.FullName}.Format returned null.");

        Type coreSystems = GetCoreSystemsType();
        object renderer =
            AccessTools.Field(coreSystems, "SpriteRenderer")?.GetValue(null)
            ?? AccessTools.Property(coreSystems, "SpriteRenderer")?.GetValue(null)
            ?? throw new MissingMemberException(
                "CoreSystems.SpriteRenderer not found.");

        IDictionary dictionary =
            AccessTools.Field(renderer.GetType(), "_psoPerFormat")
                ?.GetValue(renderer) as IDictionary
            ?? throw new MissingFieldException(
                "SpriteRenderer._psoPerFormat not found.");

        object original = dictionary[format]
            ?? throw new InvalidOperationException(
                "SpriteRenderer PSO format entry not found.");

        CurvedPair curved = GetOrCreateCurvedPair(format);

        // SpritePipelineStates is a value type. RuntimeHelpers.GetObjectValue
        // creates a separate boxed copy, so restoring Original is reliable.
        object replacement = RuntimeHelpers.GetObjectValue(original);
        Type stateType = replacement.GetType();
        AccessTools.Field(stateType, "StandardPSO")!
            .SetValue(replacement, curved.Standard);
        AccessTools.Field(stateType, "PremultipliedAlphaPSO")!
            .SetValue(replacement, curved.Premultiplied);

        dictionary[format] = replacement;
        __state = new SwapState
        {
            Dictionary = dictionary,
            Format = format,
            Original = original
        };
    }

    // ReSharper disable once InconsistentNaming
    static Exception? Finalizer(
        SwapState? __state,
        Exception? __exception)
    {
        if (__state != null)
            __state.Dictionary[__state.Format] = __state.Original;

        return __exception;
    }

    static CurvedPair GetOrCreateCurvedPair(object format)
    {
        lock (Sync)
        {
            if (CurvedByFormat.TryGetValue(format, out CurvedPair? pair))
                return pair;

            Type coreSystems = GetCoreSystemsType();
            object manager =
                AccessTools.Field(coreSystems, "GraphicsPSOs")?.GetValue(null)
                ?? AccessTools.Property(coreSystems, "GraphicsPSOs")?.GetValue(null)
                ?? throw new MissingMemberException(
                    "CoreSystems.GraphicsPSOs not found.");

            object cache = AccessTools.Field(manager.GetType(), "_dictionary")
                ?.GetValue(manager)
                ?? throw new MissingFieldException(
                    "GraphicsPSOManager._dictionary not found.");

            object? standardKey = null;
            object? premultipliedKey = null;

            foreach (object entry in (IEnumerable)cache)
            {
                Type entryType = entry.GetType();
                object key = AccessTools.Property(entryType, "Key")!
                    .GetValue(entry)!;

                string name = (string?)AccessTools.Field(
                    key.GetType(),
                    "DebugName")?.GetValue(key) ?? string.Empty;

                object description = AccessTools.Field(
                    key.GetType(),
                    "PSODescription")!.GetValue(key)!;

                Array formats = (Array)AccessTools.Property(
                    description.GetType(),
                    "RenderTargetFormats")!.GetValue(description)!;

                if (formats.Length != 1 ||
                    !Equals(formats.GetValue(0), format))
                {
                    continue;
                }

                if (name == "SpriteRenderer.Standard")
                    standardKey = key;
                else if (name == "SpriteRenderer.PremultipliedAlpha")
                    premultipliedKey = key;
            }

            if (standardKey == null || premultipliedKey == null)
            {
                throw new InvalidOperationException(
                    "SpriteRenderer PSO descriptors were not found.");
            }

            pair = new CurvedPair
            {
                Standard = CreateVariant(
                    manager,
                    standardKey,
                    "UIRevamp.CurvedHud.Standard"),
                Premultiplied = CreateVariant(
                    manager,
                    premultipliedKey,
                    "UIRevamp.CurvedHud.Premultiplied")
            };

            CurvedByFormat.Add(format, pair);
            Log.Default.Info(
                $"[{Plugin.PluginId}] Created curved HUD pixel-shader PSOs for format '{format}'.");
            return pair;
        }
    }

    static object CreateVariant(
        object manager,
        object key,
        string debugName)
    {
        Type keyType = key.GetType();
        object root = AccessTools.Field(keyType, "RootSignature")!
            .GetValue(key)!;
        object originalDescription = AccessTools.Field(
            keyType,
            "PSODescription")!.GetValue(key)!;
        object description = RuntimeHelpers.GetObjectValue(originalDescription);
        object inputLayout = AccessTools.Field(
            keyType,
            "InputLayoutDescription")!.GetValue(key)!;

        FieldInfo pixelField = AccessTools.Field(
            description.GetType(),
            "PixelShader")!;
        object originalPixel = pixelField.GetValue(description)!;
        Type shaderDescriptionType = originalPixel.GetType();

        object originalShaderHandle =
            AccessTools.Property(shaderDescriptionType, "Handle")
                ?.GetValue(originalPixel)
            ?? AccessTools.Field(shaderDescriptionType, "_handle")
                ?.GetValue(originalPixel)
            ?? throw new MissingMemberException(
                "ShaderDescription.Handle not found.");

        ShaderFileHandle shaderHandle = CurvedHudShaderPatch.PixelShaderHandle;
        if (originalShaderHandle.GetType() != typeof(ShaderFileHandle))
        {
            throw new InvalidOperationException(
                $"Unexpected sprite pixel-shader handle type " +
                $"'{originalShaderHandle.GetType().FullName}'.");
        }

        ShaderDefine[] originalDefines =
            AccessTools.Property(shaderDescriptionType, "Defines")
                ?.GetValue(originalPixel) as ShaderDefine[]
            ?? Array.Empty<ShaderDefine>();

        ShaderDefine[] defines = originalDefines.ToArray();

        object? customOptions = AccessTools.Property(
            shaderDescriptionType,
            "CustomOptions")?.GetValue(originalPixel);

        ConstructorInfo shaderConstructor = shaderDescriptionType
            .GetConstructors(BindingFlags.Instance |
                             BindingFlags.Public |
                             BindingFlags.NonPublic)
            .Single(constructor =>
            {
                ParameterInfo[] parameters = constructor.GetParameters();
                return parameters.Length == 3 &&
                       parameters[0].ParameterType == typeof(ShaderFileHandle) &&
                       parameters[1].ParameterType == typeof(ShaderDefine[]);
            });

        object curvedPixel = shaderConstructor.Invoke(new[]
        {
            shaderHandle,
            defines,
            customOptions
        });
        pixelField.SetValue(description, curvedPixel);

        MethodInfo create = AccessTools.GetDeclaredMethods(manager.GetType())
            .Single(method =>
                method.Name == "CreateAsync" &&
                method.GetParameters().Length == 5);

        object task = create.Invoke(manager, new[]
        {
            debugName,
            root,
            description,
            inputLayout,
            false
        }) ?? throw new InvalidOperationException(
            $"GraphicsPSOManager.CreateAsync returned null for '{debugName}'.");

        return WaitForPso(task, debugName);
    }

    static object WaitForPso(object task, string debugName)
    {
        Type taskType = task.GetType();
        if (!taskType.IsGenericType ||
            !string.Equals(
                taskType.GetGenericTypeDefinition().FullName,
                "Keen.VRage.Library.Threading.Task`1",
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"GraphicsPSOManager.CreateAsync returned unexpected task type " +
                $"'{taskType.FullName}' for '{debugName}'.");
        }

        Type resultType = taskType.GetGenericArguments()[0];
        Type extensionsType = AccessTools.TypeByName(
            "Keen.VRage.Library.Threading.TaskExtensions")
            ?? throw new MissingMemberException(
                "Keen.VRage.Library.Threading.TaskExtensions not found.");

        MethodInfo waitBlockingDefinition = extensionsType
            .GetMethods(BindingFlags.Static | BindingFlags.Public)
            .Single(method =>
            {
                if (method.Name != "WaitBlocking" ||
                    !method.IsGenericMethodDefinition)
                {
                    return false;
                }

                ParameterInfo[] parameters = method.GetParameters();
                return parameters.Length == 5 &&
                       parameters[0].ParameterType.IsGenericType &&
                       string.Equals(
                           parameters[0].ParameterType
                               .GetGenericTypeDefinition().FullName,
                           "Keen.VRage.Library.Threading.Task`1",
                           StringComparison.Ordinal) &&
                       parameters[1].ParameterType == typeof(TimeSpan) &&
                       parameters[2].IsOut;
            });

        MethodInfo waitBlocking =
            waitBlockingDefinition.MakeGenericMethod(resultType);
        object?[] arguments =
        {
            task,
            System.Threading.Timeout.InfiniteTimeSpan,
            null,
            null,
            false
        };

        bool completed = (bool)waitBlocking.Invoke(null, arguments)!;
        if (!completed)
        {
            throw new TimeoutException(
                $"Timed out creating curved HUD PSO '{debugName}'.");
        }

        object pso = arguments[2]
            ?? throw new InvalidOperationException(
                $"Curved HUD PSO task '{debugName}' completed without a result.");

        if (!CurvedHudShaderPatch.OverlayApplied)
        {
            throw new InvalidOperationException(
                CurvedHudShaderPatch.DescribeMissingDefinition());
        }

        MethodInfo isValid = pso.GetType().GetMethod(
            "IsValid",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            types: Type.EmptyTypes,
            modifiers: null)
            ?? throw new MissingMethodException(
                pso.GetType().FullName,
                "IsValid");

        if (!(bool)isValid.Invoke(pso, null)!)
        {
            throw new InvalidOperationException(
                $"Curved HUD PSO '{debugName}' finished in an invalid state. " +
                "The shader or pipeline compilation failed; inspect the earlier " +
                "shader compiler errors in the render log.");
        }

        return pso;
    }

    static Type GetCoreSystemsType()
    {
        return AccessTools.TypeByName("Keen.VRage.Render12.Core.CoreSystems")
               ?? AccessTools.TypeByName(
                   "Keen.VRage.Render12.Core.Systems.CoreSystems")
               ?? throw new MissingMemberException("CoreSystems not found.");
    }
}
