using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace UI_Revamp.CurvedHud;

public class Compass : Control
{
    const double LabelFontSize = 11;
    const double FrameStrokeThickness = 2.0;
    const double TickStrokeThickness = 1.8;
    const double TickStepDegrees = 7.5;
    const int TallTickMultiple = 2;
    const int NumberTickMultiple = 4;

    public static readonly StyledProperty<double> BearingProperty =
        AvaloniaProperty.Register<Compass, double>(nameof(Bearing), 90);

    public static readonly StyledProperty<double> VisibleDegreesProperty =
        AvaloniaProperty.Register<Compass, double>(nameof(VisibleDegrees), 80);

    public static readonly StyledProperty<IBrush> FrameBrushProperty =
        AvaloniaProperty.Register<Compass, IBrush>(nameof(FrameBrush), SolidColorBrush.Parse("White"));

    public static readonly StyledProperty<IBrush> TickBrushProperty =
        AvaloniaProperty.Register<Compass, IBrush>(nameof(TickBrush), SolidColorBrush.Parse("White"));

    public static readonly StyledProperty<IBrush> TextBrushProperty =
        AvaloniaProperty.Register<Compass, IBrush>(nameof(TextBrush), SolidColorBrush.Parse("White"));

    static Compass()
    {
        WidthProperty.OverrideDefaultValue<Compass>(700);
        HeightProperty.OverrideDefaultValue<Compass>(40);

        AffectsRender<Compass>(
            BearingProperty,
            VisibleDegreesProperty,
            FrameBrushProperty,
            TickBrushProperty,
            TextBrushProperty);
    }

    public double Bearing
    {
        get => GetValue(BearingProperty);
        set => SetValue(BearingProperty, value);
    }

    public double VisibleDegrees
    {
        get => GetValue(VisibleDegreesProperty);
        set => SetValue(VisibleDegreesProperty, value);
    }

    public IBrush FrameBrush
    {
        get => GetValue(FrameBrushProperty);
        set => SetValue(FrameBrushProperty, value);
    }

    public IBrush TickBrush
    {
        get => GetValue(TickBrushProperty);
        set => SetValue(TickBrushProperty, value);
    }

    public IBrush TextBrush
    {
        get => GetValue(TextBrushProperty);
        set => SetValue(TextBrushProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        double width = Bounds.Width;
        double height = Bounds.Height;
        if (width <= 0 || height <= 0)
            return;

        using (context.PushClip(new Rect(Bounds.Size)))
        {
            DrawFrame(context, width, height);
            DrawBearingTape(context, width, height);
        }
    }

    void DrawFrame(DrawingContext context, double width, double height)
    {
        var framePen = new Pen(FrameBrush, FrameStrokeThickness);
        const double inset = 0.75;
        double top = Math.Max(inset, height * 0.5);
        double bottom = Math.Max(top, height - inset);
        double right = Math.Max(inset, width - inset);

        context.DrawLine(framePen, new Point(inset, top), new Point(inset, bottom));
        context.DrawLine(framePen, new Point(inset, bottom), new Point(right, bottom));
        context.DrawLine(framePen, new Point(right, bottom), new Point(right, top));
    }

    void DrawBearingTape(DrawingContext context, double width, double height)
    {
        double visibleDegrees = Math.Max(20, VisibleDegrees);
        double pixelsPerDegree = width / visibleDegrees;
        double centerX = width / 2;
        double labelY = 0;
        var tickPen = new Pen(TickBrush, TickStrokeThickness);

        double bearing = NormalizeDegrees(Bearing);
        int firstTickIndex = (int)Math.Floor((bearing - visibleDegrees / 2) / TickStepDegrees);
        int lastTickIndex = (int)Math.Ceiling((bearing + visibleDegrees / 2) / TickStepDegrees);

        for (int tickIndex = firstTickIndex; tickIndex <= lastTickIndex; tickIndex++)
        {
            double tick = tickIndex * TickStepDegrees;
            double x = centerX + (tick - bearing) * pixelsPerDegree;
            if (x < -1 || x > width + 1)
                continue;

            bool isTallTick = tickIndex % TallTickMultiple == 0;
            bool hasLabel = tickIndex % NumberTickMultiple == 0;
            double tickHeight = hasLabel ? 14 : isTallTick ? 10 : 5;
            double tickBottomMargin = hasLabel ? 5 : isTallTick ? 6 : 8;
            double tickBottom = Math.Max(0, height - tickBottomMargin);
            double tickTop = Math.Max(0, tickBottom - tickHeight);

            context.DrawLine(tickPen, new Point(x, tickTop), new Point(x, tickBottom));

            if (hasLabel)
                DrawLabel(context, WrapBearing(tick), x, labelY, width);
        }
    }

    void DrawLabel(DrawingContext context, int bearing, double centerX, double y, double width)
    {
        string text = FormatBearingLabel(bearing);
        var formattedText = new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface(FontFamily.Default, FontStyle.Italic, FontWeight.Bold),
            LabelFontSize,
            TextBrush);

        const double edgePadding = 3;
        double x = centerX - formattedText.Width / 2;
        if (x < edgePadding || x + formattedText.Width > width - edgePadding)
            return;

        context.DrawText(formattedText, new Point(x, y));
    }

    static string FormatBearingLabel(int bearing)
    {
        return bearing switch
        {
            0 => "N",
            90 => "E",
            180 => "S",
            270 => "W",
            _ => bearing.ToString(CultureInfo.InvariantCulture)
        };
    }

    static int WrapBearing(double bearing)
    {
        int rounded = (int)Math.Round(bearing, MidpointRounding.AwayFromZero);
        return ((rounded % 360) + 360) % 360;
    }

    static double NormalizeDegrees(double bearing)
    {
        double normalized = bearing % 360.0;
        return normalized < 0.0 ? normalized + 360.0 : normalized;
    }
}
