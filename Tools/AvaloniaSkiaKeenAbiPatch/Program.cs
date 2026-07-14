using System;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: AvaloniaSkiaKeenAbiPatch <Avalonia.Skia.dll> <Keen Avalonia.Base.dll>");
    return 2;
}

var skiaPath = Path.GetFullPath(args[0]);
var avaloniaBasePath = Path.GetFullPath(args[1]);

if (!File.Exists(skiaPath))
{
    Console.Error.WriteLine($"Avalonia.Skia.dll not found: {skiaPath}");
    return 2;
}

if (!File.Exists(avaloniaBasePath))
{
    Console.Error.WriteLine($"Keen Avalonia.Base.dll not found: {avaloniaBasePath}");
    return 2;
}

var resolver = new DefaultAssemblyResolver();
resolver.AddSearchDirectory(Path.GetDirectoryName(skiaPath)!);
resolver.AddSearchDirectory(Path.GetDirectoryName(avaloniaBasePath)!);
resolver.AddSearchDirectory(Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
    ".nuget",
    "packages",
    "skiasharp",
    "2.88.7",
    "lib",
    "net6.0"));

var readerParameters = new ReaderParameters
{
    AssemblyResolver = resolver,
    ReadSymbols = false
};

var module = ModuleDefinition.ReadModule(skiaPath, readerParameters);
var drawingContext = module.GetType("Avalonia.Skia.DrawingContextImpl")
    ?? throw new InvalidOperationException("Avalonia.Skia.DrawingContextImpl was not found.");

var avaloniaBase = ModuleDefinition.ReadModule(avaloniaBasePath, readerParameters);
var rect = ImportBaseType("Avalonia.Rect");
var roundedRect = ImportBaseType("Avalonia.RoundedRect");
var matrix = ImportBaseType("Avalonia.Matrix");
var geometry = ImportBaseType("Avalonia.Platform.IGeometryImpl");
var drawingContextImpl = ImportBaseType("Avalonia.Platform.IDrawingContextImpl");

var changed = false;
changed |= GuardDrawGlyphRunCast();
changed |= GuardDrawGeometryCast();
changed |= GuardPushGeometryClipCast();
changed |= RewriteTransformedRectClipOverload();
changed |= RewriteTransformedRoundedRectClipOverload();
changed |= RewriteTransformedGeometryClipOverload();
changed |= AddNoOpMethod("Submit");
changed |= AddNoOpMethod("Reset", drawingContextImpl);

if (!changed)
{
    Console.WriteLine("Avalonia.Skia.dll already contains the Keen Avalonia drawing-context ABI shims.");
    return 0;
}

var tempPath = skiaPath + ".patched";
module.Write(tempPath, new WriterParameters { WriteSymbols = false });
avaloniaBase.Dispose();
module.Dispose();
File.Copy(tempPath, skiaPath, overwrite: true);
File.Delete(tempPath);

Console.WriteLine("Patched Avalonia.Skia.dll with Keen Avalonia drawing-context ABI shims.");
return 0;

TypeReference ImportBaseType(string fullName)
{
    var type = avaloniaBase.GetType(fullName)
        ?? throw new InvalidOperationException($"{fullName} was not found in {avaloniaBasePath}.");
    return module.ImportReference(type);
}

bool RewriteTransformedRectClipOverload()
{
    var method = FindMethod("PushClip", rect, matrix) ?? CreateVoidMethodFrom("PushClip", FindMethod("PushClip", rect)!, rect, matrix);
    var pushClip = FindMethod("PushClip", rect)
        ?? throw new InvalidOperationException($"{drawingContext.FullName}.PushClip({rect.FullName}) was not found.");

    RewriteTransformedClipBody(method, il => il.Append(il.Create(OpCodes.Call, module.ImportReference(pushClip))));

    return true;
}

bool RewriteTransformedRoundedRectClipOverload()
{
    var method = FindMethod("PushClip", roundedRect, matrix) ?? CreateVoidMethodFrom("PushClip", FindMethod("PushClip", roundedRect)!, roundedRect, matrix);
    var pushClip = FindMethod("PushClip", roundedRect)
        ?? throw new InvalidOperationException($"{drawingContext.FullName}.PushClip({roundedRect.FullName}) was not found.");

    RewriteTransformedClipBody(method, il => il.Append(il.Create(OpCodes.Call, module.ImportReference(pushClip))));

    return true;
}

bool RewriteTransformedGeometryClipOverload()
{
    var method = FindMethod("PushGeometryClip", geometry, matrix) ?? CreateVoidMethodFrom("PushGeometryClip", FindMethod("PushGeometryClip", geometry)!, geometry, matrix);
    var pushGeometryClip = FindMethod("PushGeometryClip", geometry)
        ?? throw new InvalidOperationException($"{drawingContext.FullName}.PushGeometryClip({geometry.FullName}) was not found.");

    RewriteTransformedClipBody(method, il => il.Append(il.Create(OpCodes.Call, module.ImportReference(pushGeometryClip))));

    return true;
}

void RewriteTransformedClipBody(MethodDefinition method, Action<ILProcessor> emitClipCall)
{
    RewriteBody(method, il =>
    {
        var savedTransform = new VariableDefinition(matrix);
        method.Body.Variables.Add(savedTransform);
        method.Body.InitLocals = true;

        var transformProperty = drawingContext.Properties.First(property => property.Name == "Transform");
        var getTransform = module.ImportReference(transformProperty.GetMethod);
        var setTransform = module.ImportReference(transformProperty.SetMethod);

        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Call, getTransform));
        il.Append(il.Create(OpCodes.Stloc, savedTransform));

        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Ldarg_2));
        il.Append(il.Create(OpCodes.Call, setTransform));

        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Ldarg_1));
        emitClipCall(il);

        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Ldloc, savedTransform));
        il.Append(il.Create(OpCodes.Call, setTransform));
        il.Append(il.Create(OpCodes.Ret));
    });
}

MethodDefinition CreateVoidMethodFrom(string methodName, MethodDefinition template, params TypeReference[] parameters)
{
    var method = new MethodDefinition(
        methodName,
        template.Attributes,
        module.TypeSystem.Void)
    {
        ImplAttributes = template.ImplAttributes
    };

    foreach (var parameter in parameters)
    {
        method.Parameters.Add(new ParameterDefinition(parameter));
    }

    drawingContext.Methods.Add(method);
    return method;
}

void RewriteBody(MethodDefinition method, Action<ILProcessor> emit)
{
    method.Body = new MethodBody(method);
    var il = method.Body.GetILProcessor();
    emit(il);
}

bool AddNoOpMethod(string methodName, params TypeReference[] parameters)
{
    if (FindMethod(methodName, parameters) != null)
    {
        return false;
    }

    var method = new MethodDefinition(
        methodName,
        MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.Virtual | MethodAttributes.NewSlot,
        module.TypeSystem.Void);

    foreach (var parameter in parameters)
    {
        method.Parameters.Add(new ParameterDefinition(parameter));
    }

    var il = method.Body.GetILProcessor();
    il.Append(il.Create(OpCodes.Ret));

    drawingContext.Methods.Add(method);
    return true;
}

bool GuardDrawGlyphRunCast()
{
    const string originalName = "DrawGlyphRun";
    const string coreName = "DrawGlyphRunSkiaCore";

    var existingWrapper = FindMethod(originalName, ImportBaseType("Avalonia.Media.IBrush"), ImportBaseType("Avalonia.Platform.IGlyphRunImpl"));
    var existingCore = drawingContext.Methods.FirstOrDefault(method => method.Name == coreName);
    if (existingCore != null)
    {
        return false;
    }

    if (existingWrapper == null)
    {
        throw new InvalidOperationException($"{drawingContext.FullName}.{originalName} was not found.");
    }

    existingWrapper.Name = coreName;

    var brush = existingWrapper.Parameters[0].ParameterType;
    var glyphRun = existingWrapper.Parameters[1].ParameterType;
    var skiaGlyphRun = module.GetType("Avalonia.Skia.GlyphRunImpl")
        ?? throw new InvalidOperationException("Avalonia.Skia.GlyphRunImpl was not found.");

    var wrapper = new MethodDefinition(
        originalName,
        existingWrapper.Attributes,
        module.TypeSystem.Void)
    {
        ImplAttributes = existingWrapper.ImplAttributes
    };

    wrapper.Parameters.Add(new ParameterDefinition(brush));
    wrapper.Parameters.Add(new ParameterDefinition(glyphRun));

    var il = wrapper.Body.GetILProcessor();
    var ret = il.Create(OpCodes.Ret);
    il.Append(il.Create(OpCodes.Ldarg_2));
    il.Append(il.Create(OpCodes.Isinst, skiaGlyphRun));
    il.Append(il.Create(OpCodes.Brfalse_S, ret));
    il.Append(il.Create(OpCodes.Ldarg_0));
    il.Append(il.Create(OpCodes.Ldarg_1));
    il.Append(il.Create(OpCodes.Ldarg_2));
    il.Append(il.Create(OpCodes.Call, existingWrapper));
    il.Append(ret);

    drawingContext.Methods.Add(wrapper);
    return true;
}

bool GuardDrawGeometryCast()
{
    const string originalName = "DrawGeometry";
    const string coreName = "DrawGeometrySkiaCore";

    var brush = ImportBaseType("Avalonia.Media.IBrush");
    var pen = ImportBaseType("Avalonia.Media.IPen");
    var existingWrapper = FindMethod(originalName, brush, pen, geometry);
    var existingCore = drawingContext.Methods.FirstOrDefault(method => method.Name == coreName);
    if (existingCore != null)
    {
        return false;
    }

    if (existingWrapper == null)
    {
        throw new InvalidOperationException($"{drawingContext.FullName}.{originalName} was not found.");
    }

    existingWrapper.Name = coreName;

    var skiaGeometry = module.GetType("Avalonia.Skia.GeometryImpl")
        ?? throw new InvalidOperationException("Avalonia.Skia.GeometryImpl was not found.");

    var wrapper = new MethodDefinition(
        originalName,
        existingWrapper.Attributes,
        module.TypeSystem.Void)
    {
        ImplAttributes = existingWrapper.ImplAttributes
    };

    wrapper.Parameters.Add(new ParameterDefinition(brush));
    wrapper.Parameters.Add(new ParameterDefinition(pen));
    wrapper.Parameters.Add(new ParameterDefinition(geometry));

    var il = wrapper.Body.GetILProcessor();
    var ret = il.Create(OpCodes.Ret);
    il.Append(il.Create(OpCodes.Ldarg_3));
    il.Append(il.Create(OpCodes.Isinst, skiaGeometry));
    il.Append(il.Create(OpCodes.Brfalse_S, ret));
    il.Append(il.Create(OpCodes.Ldarg_0));
    il.Append(il.Create(OpCodes.Ldarg_1));
    il.Append(il.Create(OpCodes.Ldarg_2));
    il.Append(il.Create(OpCodes.Ldarg_3));
    il.Append(il.Create(OpCodes.Call, existingWrapper));
    il.Append(ret);

    drawingContext.Methods.Add(wrapper);
    return true;
}

bool GuardPushGeometryClipCast()
{
    const string originalName = "PushGeometryClip";
    const string coreName = "PushGeometryClipSkiaCore";

    var existingWrapper = FindMethod(originalName, geometry);
    var existingCore = drawingContext.Methods.FirstOrDefault(method => method.Name == coreName);
    if (existingCore != null)
    {
        return false;
    }

    if (existingWrapper == null)
    {
        throw new InvalidOperationException($"{drawingContext.FullName}.{originalName} was not found.");
    }

    existingWrapper.Name = coreName;

    var skiaGeometry = module.GetType("Avalonia.Skia.GeometryImpl")
        ?? throw new InvalidOperationException("Avalonia.Skia.GeometryImpl was not found.");
    var geometryDefinition = avaloniaBase.GetType("Avalonia.Platform.IGeometryImpl")
        ?? throw new InvalidOperationException("Avalonia.Platform.IGeometryImpl was not found.");
    var getBounds = geometryDefinition.Properties.First(property => property.Name == "Bounds").GetMethod;

    var wrapper = new MethodDefinition(
        originalName,
        existingWrapper.Attributes,
        module.TypeSystem.Void)
    {
        ImplAttributes = existingWrapper.ImplAttributes
    };

    wrapper.Parameters.Add(new ParameterDefinition(geometry));

    var il = wrapper.Body.GetILProcessor();
    var fallback = il.Create(OpCodes.Ldarg_0);
    var ret = il.Create(OpCodes.Ret);
    il.Append(il.Create(OpCodes.Ldarg_1));
    il.Append(il.Create(OpCodes.Isinst, skiaGeometry));
    il.Append(il.Create(OpCodes.Brfalse_S, fallback));
    il.Append(il.Create(OpCodes.Ldarg_0));
    il.Append(il.Create(OpCodes.Ldarg_1));
    il.Append(il.Create(OpCodes.Call, existingWrapper));
    il.Append(il.Create(OpCodes.Ret));
    il.Append(fallback);
    il.Append(il.Create(OpCodes.Ldarg_1));
    il.Append(il.Create(OpCodes.Callvirt, module.ImportReference(getBounds)));
    il.Append(il.Create(OpCodes.Call, FindMethod("PushClip", rect)!));
    il.Append(ret);

    drawingContext.Methods.Add(wrapper);
    return true;
}

MethodDefinition? FindMethod(string name, params TypeReference[] parameters)
{
    return drawingContext.Methods.FirstOrDefault(method =>
        method.Name == name &&
        method.Parameters.Count == parameters.Length &&
        method.Parameters.Select(parameter => parameter.ParameterType.FullName)
            .SequenceEqual(parameters.Select(parameter => parameter.FullName)));
}
