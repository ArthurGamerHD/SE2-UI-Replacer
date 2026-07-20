using System.Globalization;

namespace UI_Revamp.CurvedHud;

internal static class Constants
{
    internal static class Layout
    {
        internal const double SafeMarginXRatio = 0.025;
        internal const double SafeMarginYRatio = 0.025;
        internal const double MinSafeMarginX = 24.0;
        internal const double MinSafeMarginY = 24.0;
        internal const double MaxSafeMarginX = 96.0;
        internal const double MaxSafeMarginY = 96.0;
        internal const double RenderScalingEpsilon = 0.0001;
    }

    internal static class Shader
    {
        internal const double CurvatureX = -0.05333333333333334d;
        internal const double CurvatureY = -0.016666666666666666d;
        internal const double OpticalCenterX = 0.5;
        internal const double OpticalCenterY = 0.5;
        internal const double HeadOffsetX = 0.0;
        internal const double HeadOffsetY = 0.0;

        internal static string CurvatureXHlsl => FormatHlslFloat(CurvatureX);
        internal static string CurvatureYHlsl => FormatHlslFloat(CurvatureY);
        internal static string OpticalCenterXHlsl => FormatHlslFloat(OpticalCenterX);
        internal static string OpticalCenterYHlsl => FormatHlslFloat(OpticalCenterY);
        internal static string HeadOffsetXHlsl => FormatHlslFloat(HeadOffsetX);
        internal static string HeadOffsetYHlsl => FormatHlslFloat(HeadOffsetY);
    }

    internal static class Wobble
    {
        internal const double MaxPhysicalOffsetPixels = 64.0;
        internal const double MaxScaleDelta = 0.02;
        internal const double NeutralByte = 128.0;
        internal const double SignedByteRange = 127.0;
        internal const int MinPackedByte = 1;
        internal const int MaxPackedByte = 255;

        internal static string MaxPhysicalOffsetPixelsHlsl =>
            FormatHlslFloat(MaxPhysicalOffsetPixels);

        internal static string MaxScaleDeltaHlsl => FormatHlslFloat(MaxScaleDelta);
        internal static string NeutralByteHlsl => FormatHlslFloat(NeutralByte);
        internal static string SignedByteRangeHlsl => FormatHlslFloat(SignedByteRange);
    }

    internal static class Composition
    {
        internal const int MainViewSortLayer = 7900;
        internal const int DrawingContextSortLayer = 100;
        internal const int MainViewProbeSortLayer = 7990;
        internal const int OffscreenProbeSortLayer = 1000;
        internal const double TextureCenterOffsetX = 0.0;
        internal const double TextureCenterOffsetY = 0.0;
        internal const double CaptureRetryIntervalMs = 750.0;
        internal const double CompositionRetryIntervalMs = 100.0;
    }

    internal static class Diagnostics
    {
        internal const CurvedHudDiagnostics.ShaderMode ShaderMode =
            CurvedHudDiagnostics.ShaderMode.WarpDiscard;

        internal const CurvedHudDiagnostics.BatchMode OffscreenBatchMode =
            CurvedHudDiagnostics.BatchMode.Persistent;

        internal const CurvedHudDiagnostics.ProbeMode ProbeMode =
            CurvedHudDiagnostics.ProbeMode.None;

        internal const bool UseImmediateOffscreenBatch = false;
        internal const bool WaitForFirstOffscreenSubmit = false;
        internal const bool DrawMainViewProbe = false;
        internal const bool DrawOffscreenProbe = false;
        internal const bool CaptureOffscreenTarget = false;
    }

    internal static string FormatHlslFloat(double value)
    {
        return value.ToString("0.0#########", CultureInfo.InvariantCulture);
    }

    internal static string FormatHlslBool(bool value)
    {
        return value ? "true" : "false";
    }
}
