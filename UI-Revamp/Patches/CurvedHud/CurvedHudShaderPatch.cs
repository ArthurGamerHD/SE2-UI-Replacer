﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using HarmonyLib;
using Keen.VRage.Core.Render;
using Keen.VRage.Library.Diagnostics;
using Keen.VRage.Library.Filesystem;
using UI_Revamp.CurvedHud;

namespace UI_Revamp.Patches.CurvedHud;

/// <summary>
/// Provides a virtual shader-project overlay for the curved HUD pixel shader.
///
/// The custom PSO points at the virtual project. Every source file is read from
/// the engine shader project unchanged except the file that contains the real
/// __PixelShader function definition. That definition is renamed and wrapped in
/// place. Stock shader caches and source files are never mutated or invalidated.
/// </summary>
internal static class CurvedHudShaderPatch
{
    const string RootFileName = "Primitives\\SpritesPixel.hlsl";
    const string EntryName = "__PixelShader";
    const string StockEntryName = "UIRevamp_StockPixelShader";
    const string OverlayGuard = "UIREVAMP_CURVED_HUD_SHADER_OVERLAY_INCLUDED";

    // A private shader-project identity used only by this plugin. A real
    // IFileReader is registered for it in ShaderFileReaderManager.
    static readonly Guid OverlayProjectGuid =
        new("e41c97f4-b19f-46f8-a21e-91334ca7ef42");

    static readonly object Sync = new();

    static readonly Dictionary<string, string> SourceByPath =
        new(StringComparer.OrdinalIgnoreCase);

    static readonly HashSet<string> SymbolOnlyFiles =
        new(StringComparer.OrdinalIgnoreCase);

    static string? _definitionFile;
    static int _overlayApplied;

    internal static ShaderFileHandle PixelShaderHandle =>
        new(OverlayProjectGuid, RootFileName);

    internal static bool OverlayApplied =>
        Volatile.Read(ref _overlayApplied) != 0;

    internal static string DescribeMissingDefinition()
    {
        lock (Sync)
        {
            string files = SymbolOnlyFiles.Count == 0
                ? "none"
                : string.Join(", ", SymbolOnlyFiles.OrderBy(path => path));

            return "The virtual curved-HUD shader project loaded the stock " +
                   "SpritesPixel source tree, but no concrete __PixelShader " +
                   "function definition was found. Files containing only the " +
                   $"symbol were: {files}.";
        }
    }

    internal static void RegisterOverlayReader(object readerManager)
    {
        object dictionaryObject = AccessTools.Field(
            readerManager.GetType(),
            "_fileReadersByGuid")?.GetValue(readerManager)
            ?? throw new MissingFieldException(
                readerManager.GetType().FullName,
                "_fileReadersByGuid");

        if (dictionaryObject is not IDictionary dictionary)
        {
            throw new InvalidOperationException(
                "ShaderFileReaderManager._fileReadersByGuid is not an IDictionary.");
        }

        IFileReader engineReader =
            dictionary[ShaderFileHandle.EngineGuid] as IFileReader
            ?? throw new InvalidOperationException(
                "The engine shader-project reader is not registered.");

        if (dictionary.Contains(OverlayProjectGuid))
        {
            throw new InvalidOperationException(
                $"Shader project GUID '{OverlayProjectGuid}' is already registered.");
        }

        lock (Sync)
        {
            SourceByPath.Clear();
            SymbolOnlyFiles.Clear();
            _definitionFile = null;
            Volatile.Write(ref _overlayApplied, 0);
        }

        dictionary.Add(
            OverlayProjectGuid,
            new OverlayFileReader(engineReader));

        Log.Default.Info(
            $"[{Plugin.PluginId}] Registered curved HUD shader overlay project " +
            $"'{OverlayProjectGuid}'.");
    }

    static string BuildOverlaySource(string path, string stockContent)
    {
        if (!stockContent.Contains(EntryName, StringComparison.Ordinal))
            return stockContent;

        if (!TryFindPixelFunction(stockContent, out PixelFunction function))
        {
            SymbolOnlyFiles.Add(path);
            return stockContent;
        }

        if (_definitionFile != null &&
            !string.Equals(_definitionFile, path, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Multiple concrete __PixelShader definitions were found in the " +
                $"sprite shader tree: '{_definitionFile}' and '{path}'.");
        }

        _definitionFile = path;
        string transformed = CreateTransformedSource(stockContent, function);
        Interlocked.Exchange(ref _overlayApplied, 1);

        Log.Default.Info(
            $"[{Plugin.PluginId}] Built curved HUD shader overlay from " +
            $"'{path}' using UV source '{function.UvParameter.Name}" +
            $"{function.UvAccess}' and wobble carrier '" +
            $"{function.UvParameter.Name}{function.ColorAccess}' in " +
            $"'{CurvedHudDiagnostics.FormatMode(CurvedHudDiagnostics.Mode)}' mode.");

        return transformed;
    }

    static string CreateTransformedSource(
        string stockContent,
        PixelFunction function)
    {
        string header = function.Header;
        string inputType = function.UvParameter.Type;
        string inputName = function.UvParameter.Name;
        string uvAccess = function.UvAccess;
        string colorAccess = function.ColorAccess;

        string stockArguments = BuildCallArguments(
            function,
            "UIRevamp_WarpedInput",
            function.ResultParameter?.Name ?? string.Empty);

        string stockCall = BuildStockCall(function, stockArguments);

        CurvedHudDiagnostics.ShaderMode mode = CurvedHudDiagnostics.Mode;
        string shaderBody = mode switch
        {
            CurvedHudDiagnostics.ShaderMode.StockPso or
            CurvedHudDiagnostics.ShaderMode.PassThrough => $$"""
    // The composition sprite's RGB bytes carry wobble parameters. Always
    // neutralize that carrier before invoking the stock sprite shader.
    {{inputType}} UIRevamp_WarpedInput = {{inputName}};
    float UIRevamp_TextureOpacity =
        saturate(((float4)UIRevamp_WarpedInput{{colorAccess}}).a);
    UIRevamp_WarpedInput{{colorAccess}} =
        (ColorLinearPremultiplied)float4(1.0, 1.0, 1.0, 1.0);

{{stockCall}}
""",
            CurvedHudDiagnostics.ShaderMode.WarpClamp => BuildWarpBody(
                inputType,
                inputName,
                uvAccess,
                colorAccess,
                stockCall,
                "    UIRevamp_Uv = saturate(UIRevamp_Uv);"),
            CurvedHudDiagnostics.ShaderMode.WarpDiscard => BuildWarpBody(
                inputType,
                inputName,
                uvAccess,
                colorAccess,
                stockCall,
                """
    if (UIRevamp_Uv.x < 0.0 || UIRevamp_Uv.y < 0.0 ||
        UIRevamp_Uv.x > 1.0 || UIRevamp_Uv.y > 1.0)
    {
        discard;
    }
"""),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null)
        };

        string wrapper = $$"""

#undef {{EntryName}}

{{header}}
{
{{shaderBody}}
}

#endif
""";

        return $$"""
#ifndef {{OverlayGuard}}
#define {{OverlayGuard}}
#define {{EntryName}} {{StockEntryName}}

{{stockContent}}
{{wrapper}}
""";
    }

    static string BuildWarpBody(
        string inputType,
        string inputName,
        string uvAccess,
        string colorAccess,
        string stockCall,
        string edgeHandling)
    {
        return $$"""
    // Negative curvature values bow the HUD in the selected direction.
    static const float2 UIRevamp_Curvature = float2(
        {{Constants.Shader.CurvatureXHlsl}},
        {{Constants.Shader.CurvatureYHlsl}});
    static const float2 UIRevamp_OpticalCenter = float2(
        {{Constants.Shader.OpticalCenterXHlsl}},
        {{Constants.Shader.OpticalCenterYHlsl}});
    static const float2 UIRevamp_HeadOffset = float2(
        {{Constants.Shader.HeadOffsetXHlsl}},
        {{Constants.Shader.HeadOffsetYHlsl}});

    // The composition sprite encodes physical-pixel X/Y displacement and a
    // scale delta in its RGB vertex color. Byte 128 is exact neutral. The
    // carrier is reset to white before the stock shader sees it.
    static const float UIRevamp_MaxOffsetPixels =
        {{Constants.Wobble.MaxPhysicalOffsetPixelsHlsl}};
    static const float UIRevamp_MaxScaleDelta =
        {{Constants.Wobble.MaxScaleDeltaHlsl}};
    {{inputType}} UIRevamp_WarpedInput = {{inputName}};
    float4 UIRevamp_WobbleCarrier =
        (float4)UIRevamp_WarpedInput{{colorAccess}};
    float UIRevamp_TextureOpacity = saturate(UIRevamp_WobbleCarrier.a);
    float3 UIRevamp_WobbleCarrierRgb =
        UIRevamp_TextureOpacity > 0.0001
            ? UIRevamp_WobbleCarrier.rgb / UIRevamp_TextureOpacity
            : float3(
                {{Constants.Wobble.NeutralByteHlsl}} / 255.0,
                {{Constants.Wobble.NeutralByteHlsl}} / 255.0,
                {{Constants.Wobble.NeutralByteHlsl}} / 255.0);
    UIRevamp_WarpedInput{{colorAccess}} =
        (ColorLinearPremultiplied)float4(1.0, 1.0, 1.0, 1.0);

    // Sprite vertex colors are supplied as ColorSRGB but arrive here through
    // the stock sprite vertex shader as ColorLinearPremultiplied. Convert the
    // un-premultiplied carrier back to sRGB before reconstructing its original
    // byte values. Without this conversion, neutral byte 128 is read as about
    // 55, producing a permanent negative X/Y offset and slight scale shrink.
    float3 UIRevamp_WobbleCarrierLinear =
        saturate(UIRevamp_WobbleCarrierRgb);
    float3 UIRevamp_WobbleCarrierSrgbLow =
        UIRevamp_WobbleCarrierLinear * 12.92;
    float3 UIRevamp_WobbleCarrierSrgbHigh =
        1.055 * pow(
            max(UIRevamp_WobbleCarrierLinear, 0.0),
            1.0 / 2.4) - 0.055;
    float3 UIRevamp_WobbleCarrierSrgb = lerp(
        UIRevamp_WobbleCarrierSrgbLow,
        UIRevamp_WobbleCarrierSrgbHigh,
        step(0.0031308, UIRevamp_WobbleCarrierLinear));

    float3 UIRevamp_WobbleBytes =
        round(saturate(UIRevamp_WobbleCarrierSrgb) * 255.0);
    float2 UIRevamp_WobblePixels =
        ((UIRevamp_WobbleBytes.rg - {{Constants.Wobble.NeutralByteHlsl}}) /
         {{Constants.Wobble.SignedByteRangeHlsl}}) *
        UIRevamp_MaxOffsetPixels;
    float UIRevamp_WobbleScale =
        1.0 + ((UIRevamp_WobbleBytes.b -
         {{Constants.Wobble.NeutralByteHlsl}}) /
         {{Constants.Wobble.SignedByteRangeHlsl}}) *
        UIRevamp_MaxScaleDelta;

    float2 UIRevamp_SourceUv = UIRevamp_WarpedInput{{uvAccess}};

    // Convert the physical-pixel displacement to UV units from the actual
    // rasterized full-screen sprite. This automatically follows resolution and
    // UI RenderScaling without another shader constant buffer.
    float2 UIRevamp_UvPerPixelX = ddx(UIRevamp_SourceUv);
    float2 UIRevamp_UvPerPixelY = ddy(UIRevamp_SourceUv);
    float2 UIRevamp_TranslationUv =
        UIRevamp_WobblePixels.x * UIRevamp_UvPerPixelX +
        UIRevamp_WobblePixels.y * UIRevamp_UvPerPixelY;

    // Inverse sample transform for center-origin scale followed by translation.
    // Because the quad is full-screen and out-of-range UVs are discarded, the
    // curved silhouette itself moves and scales rather than only its interior
    // content.
    float2 UIRevamp_SurfaceUv =
        ((UIRevamp_SourceUv - UIRevamp_OpticalCenter -
          UIRevamp_TranslationUv) / UIRevamp_WobbleScale) +
        UIRevamp_OpticalCenter;

    float2 UIRevamp_Centered =
        (UIRevamp_SurfaceUv - UIRevamp_OpticalCenter) * 2.0;

    float2 UIRevamp_Warped = UIRevamp_Centered;
    UIRevamp_Warped.y *=
        1.0 + UIRevamp_Curvature.x * UIRevamp_Centered.x * UIRevamp_Centered.x;
    UIRevamp_Warped.x *=
        1.0 + UIRevamp_Curvature.y * UIRevamp_Centered.y * UIRevamp_Centered.y;

    float2 UIRevamp_Uv =
        UIRevamp_Warped * 0.5 + UIRevamp_OpticalCenter + UIRevamp_HeadOffset;

{{edgeHandling}}

    // SE2's sprite entry returns ColorLinearPremultiplied rather than float4.
    // Only replace its input UV/color and let the stock shader construct its
    // engine-owned result type.
    UIRevamp_WarpedInput{{uvAccess}} = UIRevamp_Uv;

{{stockCall}}
""";
    }

    static string BuildStockCall(
        PixelFunction function,
        string stockArguments)
    {
        if (function.ResultParameter == null)
        {
            return $$"""
    {{function.ReturnType}} UIRevamp_Output =
        {{StockEntryName}}({{stockArguments}});
    UIRevamp_Output.Values *= UIRevamp_TextureOpacity;
    return UIRevamp_Output;
""";
        }

        string resultName = function.ResultParameter.Name;
        return $$"""
    {{StockEntryName}}({{stockArguments}});
    {{resultName}}.Values *= UIRevamp_TextureOpacity;
""";
    }

    static string BuildCallArguments(
        PixelFunction function,
        string inputReplacement,
        string resultReplacement)
    {
        return string.Join(", ", function.Parameters.Select(parameter =>
        {
            if (ReferenceEquals(parameter, function.UvParameter))
                return inputReplacement;
            if (ReferenceEquals(parameter, function.ResultParameter))
                return resultReplacement;
            return parameter.Name;
        }));
    }

    static bool TryFindPixelFunction(
        string content,
        out PixelFunction function)
    {
        int searchStart = 0;
        while (true)
        {
            int nameIndex = content.IndexOf(
                EntryName,
                searchStart,
                StringComparison.Ordinal);
            if (nameIndex < 0)
                break;

            searchStart = nameIndex + EntryName.Length;
            if (!IsIdentifierBoundary(content, nameIndex - 1) ||
                !IsIdentifierBoundary(content, searchStart))
            {
                continue;
            }

            int openParen = SkipTrivia(content, searchStart);
            if (openParen >= content.Length || content[openParen] != '(')
                continue;

            int closeParen = FindMatchingDelimiter(
                content,
                openParen,
                '(',
                ')');
            if (closeParen < 0)
                continue;

            int bodyOpen = FindBodyOpen(content, closeParen + 1);
            if (bodyOpen < 0)
                continue;

            int bodyClose = FindMatchingDelimiter(
                content,
                bodyOpen,
                '{',
                '}');
            if (bodyClose < 0)
            {
                throw new InvalidOperationException(
                    "The stock sprite __PixelShader has an unmatched body brace.");
            }

            IdentifierToken returnToken = FindPreviousIdentifier(
                content,
                nameIndex - 1)
                ?? throw new InvalidOperationException(
                    "Could not identify the stock sprite pixel-shader return type.");

            string returnType = returnToken.Text;
            string header = content.Substring(
                returnToken.Start,
                bodyOpen - returnToken.Start).Trim();

            List<ShaderParameter> parameters = ParseParameters(
                content.Substring(
                    openParen + 1,
                    closeParen - openParen - 1));

            string body = content.Substring(
                bodyOpen + 1,
                bodyClose - bodyOpen - 1);

            (ShaderParameter Parameter, string Access)? uv =
                FindUvParameter(parameters, body);
            if (uv == null)
            {
                throw new InvalidOperationException(
                    "Found the concrete stock sprite __PixelShader function, " +
                    "but could not identify its texture-coordinate parameter.");
            }

            (ShaderParameter Parameter, string Access)? color =
                FindColorParameter(parameters, body);
            if (color == null)
            {
                throw new InvalidOperationException(
                    "Found the concrete stock sprite __PixelShader function, " +
                    "but could not identify its per-sprite color field for wobble parameters.");
            }

            if (!ReferenceEquals(color.Value.Parameter, uv.Value.Parameter))
            {
                throw new InvalidOperationException(
                    "The stock sprite shader exposes UV and color through separate " +
                    "parameters; the curved HUD wrapper expects one vertex-stage input struct.");
            }

            ShaderParameter? resultParameter = null;
            if (string.Equals(returnType, "void", StringComparison.Ordinal))
            {
                resultParameter = parameters.SingleOrDefault(parameter =>
                    parameter.IsOutput &&
                    (parameter.Semantic.Contains(
                         "SV_Target",
                         StringComparison.OrdinalIgnoreCase) ||
                     parameter.Type.EndsWith("4", StringComparison.Ordinal)));

                if (resultParameter == null)
                {
                    throw new InvalidOperationException(
                        "The stock sprite __PixelShader returns void, but no " +
                        "single color output parameter could be identified.");
                }
            }

            ShaderParameter[] unsupportedOutputs = parameters
                .Where(parameter =>
                    parameter.IsOutput &&
                    !ReferenceEquals(parameter, resultParameter) &&
                    !ReferenceEquals(parameter, uv.Value.Parameter))
                .ToArray();
            if (unsupportedOutputs.Length != 0)
            {
                throw new InvalidOperationException(
                    "The stock sprite __PixelShader has unsupported additional " +
                    "output parameters: " +
                    string.Join(", ", unsupportedOutputs.Select(p => p.Name)));
            }

            function = new PixelFunction(
                header,
                returnType,
                parameters,
                uv.Value.Parameter,
                uv.Value.Access,
                color.Value.Access,
                resultParameter);
            return true;
        }

        function = null!;
        return false;
    }

    static List<ShaderParameter> ParseParameters(string parameterList)
    {
        var result = new List<ShaderParameter>();
        foreach (string raw in SplitTopLevel(parameterList, ','))
        {
            string parameter = raw.Trim();
            if (parameter.Length == 0 || parameter == "void")
                continue;

            int colon = FindTopLevelCharacter(parameter, ':');
            string declaration = colon < 0
                ? parameter
                : parameter[..colon].Trim();
            string semantic = colon < 0
                ? string.Empty
                : parameter[(colon + 1)..].Trim();

            MatchCollection identifiers = Regex.Matches(
                declaration,
                @"[A-Za-z_][A-Za-z0-9_]*",
                RegexOptions.CultureInvariant);
            if (identifiers.Count < 2)
            {
                throw new InvalidOperationException(
                    $"Could not parse sprite shader parameter '{parameter}'.");
            }

            string name = identifiers[identifiers.Count - 1].Value;
            string type = identifiers
                .Cast<Match>()
                .Take(identifiers.Count - 1)
                .Select(match => match.Value)
                .Last(token => !IsParameterQualifier(token));

            bool hasOut = identifiers.Cast<Match>().Any(match =>
                string.Equals(match.Value, "out", StringComparison.Ordinal) ||
                string.Equals(match.Value, "inout", StringComparison.Ordinal));
            bool hasIn = identifiers.Cast<Match>().Any(match =>
                string.Equals(match.Value, "in", StringComparison.Ordinal) ||
                string.Equals(match.Value, "inout", StringComparison.Ordinal));

            result.Add(new ShaderParameter(
                type,
                name,
                semantic,
                IsOutput: hasOut,
                IsInput: hasIn || !hasOut));
        }

        return result;
    }

    static (ShaderParameter Parameter, string Access)? FindUvParameter(
        IEnumerable<ShaderParameter> parameters,
        string body)
    {
        foreach (ShaderParameter parameter in parameters.Where(p => p.IsInput))
        {
            MatchCollection fields = Regex.Matches(
                body,
                $@"\b{Regex.Escape(parameter.Name)}\s*\.\s*(?<field>[A-Za-z_][A-Za-z0-9_]*)",
                RegexOptions.CultureInvariant);

            string? preferred = fields
                .Cast<Match>()
                .Select(match => match.Groups["field"].Value)
                .Distinct(StringComparer.Ordinal)
                .FirstOrDefault(field =>
                    field.Contains("tex", StringComparison.OrdinalIgnoreCase) ||
                    field.Contains("uv", StringComparison.OrdinalIgnoreCase));

            if (preferred != null)
                return (parameter, $".{preferred}.xy");
        }

        foreach (ShaderParameter parameter in parameters.Where(p => p.IsInput))
        {
            if ((parameter.Name.Contains("tex", StringComparison.OrdinalIgnoreCase) ||
                 parameter.Name.Contains("uv", StringComparison.OrdinalIgnoreCase) ||
                 parameter.Semantic.Contains("TEXCOORD", StringComparison.OrdinalIgnoreCase)) &&
                (parameter.Type.EndsWith("2", StringComparison.Ordinal) ||
                 parameter.Type.EndsWith("4", StringComparison.Ordinal)))
            {
                return (parameter, ".xy");
            }
        }

        return null;
    }

    static (ShaderParameter Parameter, string Access)? FindColorParameter(
        IEnumerable<ShaderParameter> parameters,
        string body)
    {
        foreach (ShaderParameter parameter in parameters.Where(p => p.IsInput))
        {
            MatchCollection fields = Regex.Matches(
                body,
                $@"\b{Regex.Escape(parameter.Name)}\s*\.\s*(?<field>[A-Za-z_][A-Za-z0-9_]*)",
                RegexOptions.CultureInvariant);

            string? preferred = fields
                .Cast<Match>()
                .Select(match => match.Groups["field"].Value)
                .Distinct(StringComparer.Ordinal)
                .FirstOrDefault(field =>
                    field.Contains("color", StringComparison.OrdinalIgnoreCase) ||
                    field.Contains("colour", StringComparison.OrdinalIgnoreCase) ||
                    field.Contains("tint", StringComparison.OrdinalIgnoreCase));

            if (preferred != null)
                return (parameter, $".{preferred}");
        }

        foreach (ShaderParameter parameter in parameters.Where(p => p.IsInput))
        {
            if ((parameter.Name.Contains("color", StringComparison.OrdinalIgnoreCase) ||
                 parameter.Name.Contains("colour", StringComparison.OrdinalIgnoreCase) ||
                 parameter.Semantic.Contains("COLOR", StringComparison.OrdinalIgnoreCase)) &&
                parameter.Type.EndsWith("4", StringComparison.Ordinal))
            {
                return (parameter, string.Empty);
            }
        }

        return null;
    }

    static IEnumerable<string> SplitTopLevel(string text, char delimiter)
    {
        int start = 0;
        int round = 0;
        int square = 0;
        int angle = 0;
        bool inLineComment = false;
        bool inBlockComment = false;
        bool inString = false;

        for (int i = 0; i < text.Length; i++)
        {
            char current = text[i];
            char next = i + 1 < text.Length ? text[i + 1] : '\0';

            if (inLineComment)
            {
                if (current == '\n')
                    inLineComment = false;
                continue;
            }
            if (inBlockComment)
            {
                if (current == '*' && next == '/')
                {
                    inBlockComment = false;
                    i++;
                }
                continue;
            }
            if (inString)
            {
                if (current == '\\')
                    i++;
                else if (current == '"')
                    inString = false;
                continue;
            }
            if (current == '/' && next == '/')
            {
                inLineComment = true;
                i++;
                continue;
            }
            if (current == '/' && next == '*')
            {
                inBlockComment = true;
                i++;
                continue;
            }
            if (current == '"')
            {
                inString = true;
                continue;
            }

            switch (current)
            {
                case '(':
                    round++;
                    break;
                case ')':
                    round--;
                    break;
                case '[':
                    square++;
                    break;
                case ']':
                    square--;
                    break;
                case '<':
                    angle++;
                    break;
                case '>':
                    angle = Math.Max(0, angle - 1);
                    break;
            }

            if (current == delimiter && round == 0 && square == 0 && angle == 0)
            {
                yield return text.Substring(start, i - start);
                start = i + 1;
            }
        }

        yield return text[start..];
    }

    static int FindTopLevelCharacter(string text, char character)
    {
        int index = 0;
        foreach (string part in SplitTopLevel(text, character))
        {
            if (index + part.Length < text.Length)
                return index + part.Length;
            index += part.Length + 1;
        }
        return -1;
    }

    static int FindBodyOpen(string text, int start)
    {
        bool inLineComment = false;
        bool inBlockComment = false;
        bool inString = false;

        for (int i = start; i < text.Length; i++)
        {
            char current = text[i];
            char next = i + 1 < text.Length ? text[i + 1] : '\0';

            if (inLineComment)
            {
                if (current == '\n')
                    inLineComment = false;
                continue;
            }
            if (inBlockComment)
            {
                if (current == '*' && next == '/')
                {
                    inBlockComment = false;
                    i++;
                }
                continue;
            }
            if (inString)
            {
                if (current == '\\')
                    i++;
                else if (current == '"')
                    inString = false;
                continue;
            }
            if (current == '/' && next == '/')
            {
                inLineComment = true;
                i++;
                continue;
            }
            if (current == '/' && next == '*')
            {
                inBlockComment = true;
                i++;
                continue;
            }
            if (current == '"')
            {
                inString = true;
                continue;
            }
            if (current == ';')
                return -1;
            if (current == '{')
                return i;
        }

        return -1;
    }

    static int FindMatchingDelimiter(
        string text,
        int openIndex,
        char open,
        char close)
    {
        int depth = 0;
        bool inLineComment = false;
        bool inBlockComment = false;
        bool inString = false;

        for (int i = openIndex; i < text.Length; i++)
        {
            char current = text[i];
            char next = i + 1 < text.Length ? text[i + 1] : '\0';

            if (inLineComment)
            {
                if (current == '\n')
                    inLineComment = false;
                continue;
            }
            if (inBlockComment)
            {
                if (current == '*' && next == '/')
                {
                    inBlockComment = false;
                    i++;
                }
                continue;
            }
            if (inString)
            {
                if (current == '\\')
                    i++;
                else if (current == '"')
                    inString = false;
                continue;
            }
            if (current == '/' && next == '/')
            {
                inLineComment = true;
                i++;
                continue;
            }
            if (current == '/' && next == '*')
            {
                inBlockComment = true;
                i++;
                continue;
            }
            if (current == '"')
            {
                inString = true;
                continue;
            }

            if (current == open)
                depth++;
            else if (current == close && --depth == 0)
                return i;
        }

        return -1;
    }

    static int SkipTrivia(string text, int index)
    {
        while (index < text.Length)
        {
            if (char.IsWhiteSpace(text[index]))
            {
                index++;
                continue;
            }

            if (index + 1 < text.Length &&
                text[index] == '/' && text[index + 1] == '/')
            {
                index += 2;
                while (index < text.Length && text[index] != '\n')
                    index++;
                continue;
            }

            if (index + 1 < text.Length &&
                text[index] == '/' && text[index + 1] == '*')
            {
                int end = text.IndexOf("*/", index + 2, StringComparison.Ordinal);
                return end < 0 ? text.Length : SkipTrivia(text, end + 2);
            }

            break;
        }

        return index;
    }

    static IdentifierToken? FindPreviousIdentifier(string text, int index)
    {
        while (index >= 0 && !IsIdentifierCharacter(text[index]))
            index--;
        if (index < 0)
            return null;

        int end = index + 1;
        while (index >= 0 && IsIdentifierCharacter(text[index]))
            index--;

        int start = index + 1;
        return new IdentifierToken(start, text.Substring(start, end - start));
    }

    static bool IsIdentifierBoundary(string text, int index) =>
        index < 0 || index >= text.Length || !IsIdentifierCharacter(text[index]);

    static bool IsIdentifierCharacter(char value) =>
        char.IsLetterOrDigit(value) || value == '_';

    static bool IsParameterQualifier(string value) =>
        value is "in" or "out" or "inout" or "const" or "linear" or
            "centroid" or "noperspective" or "sample" or
            "nointerpolation" or "precise" or "uniform" or "static";

    static string NormalizePath(string path) =>
        path.Replace('/', '\\');

    sealed class OverlayFileReader : IFileReader
    {
        readonly IFileReader _engineReader;

        internal OverlayFileReader(IFileReader engineReader)
        {
            _engineReader = engineReader
                ?? throw new ArgumentNullException(nameof(engineReader));
        }

        public Stream OpenRead(
            string file,
            FileShare share = FileShare.Read,
            AdvancedFileOptions options = (AdvancedFileOptions)0)
        {
            string path = NormalizePath(file);
            string content;

            lock (Sync)
            {
                if (!SourceByPath.TryGetValue(path, out content!))
                {
                    using Stream stream =
                        _engineReader.OpenRead(file, share, options);
                    using var sourceReader = new StreamReader(stream);
                    content = BuildOverlaySource(
                        path,
                        sourceReader.ReadToEnd());
                    SourceByPath.Add(path, content);
                }
            }

            return new MemoryStream(
                Encoding.UTF8.GetBytes(content),
                writable: false);
        }

        public bool TryOpenReadSafeHandle(
            string file,
            out AccessHandle handle,
            FileShare share = FileShare.Read,
            AdvancedFileOptions options = (AdvancedFileOptions)0)
        {
            // Overlay files are generated in memory and therefore cannot expose
            // a native filesystem handle.
            handle = null!;
            return false;
        }

        public bool FileExists(string path) =>
            _engineReader.FileExists(path);

        public bool DirectoryExists(string path) =>
            _engineReader.DirectoryExists(path);

        public IEnumerable<string> EnumerateFiles(
            string path,
            bool includeHiddenEntries = false) =>
            _engineReader.EnumerateFiles(path, includeHiddenEntries);

        public IEnumerable<string> EnumerateDirectories(
            string path,
            bool includeHiddenEntries = false) =>
            _engineReader.EnumerateDirectories(path, includeHiddenEntries);

        public FileSystemEntryInfo GetInfo(string path, PathType type) =>
            _engineReader.GetInfo(path, type);
    }

    sealed record ShaderParameter(
        string Type,
        string Name,
        string Semantic,
        bool IsOutput,
        bool IsInput);

    sealed record PixelFunction(
        string Header,
        string ReturnType,
        List<ShaderParameter> Parameters,
        ShaderParameter UvParameter,
        string UvAccess,
        string ColorAccess,
        ShaderParameter? ResultParameter);

    sealed record IdentifierToken(int Start, string Text);
}

[HarmonyPatch]
internal static class CurvedHudShaderFileReaderRegistrationPatch
{
    static MethodBase TargetMethod()
    {
        Type type = AccessTools.TypeByName(
            "Keen.VRage.Render12.Resources.Shaders.ShaderFileReaderManager")
            ?? throw new MissingMemberException(
                "ShaderFileReaderManager not found.");

        return type.GetConstructors(
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic)
            .Single(constructor =>
                constructor.GetParameters().Length == 1);
    }

    // ReSharper disable once InconsistentNaming
    static void Postfix(object __instance)
    {
        CurvedHudShaderPatch.RegisterOverlayReader(__instance);
    }
}
