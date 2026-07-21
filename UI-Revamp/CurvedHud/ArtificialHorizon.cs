using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace UI_Revamp.CurvedHud;

public class ArtificialHorizon : Control
{
    const double LineWidth = 64;
    const double LineSpacing = 244;
    const double HorizonStrokeThickness = 3;
    const double PitchLadderStrokeThickness = 1.5;
    const double PitchLadderTickWidth = 32;
    const double PitchLadderTickSpacing = 270;
    const double PitchLadderEndCapLength = 8;
    const double PitchLadderStepDegrees = 5;
    const double PitchLadderVisibleDegrees = 7.5;
    const double PitchLadderFadeDegrees = 2.5;
    const double PitchLadderStepHeight = 92;
    const double PitchLadderLabelGap = 8;
    const double PitchLadderLabelFontSize = 11;

    public static readonly StyledProperty<double> PitchProperty =
        AvaloniaProperty.Register<ArtificialHorizon, double>(nameof(Pitch));

    public static readonly StyledProperty<double> RollProperty =
        AvaloniaProperty.Register<ArtificialHorizon, double>(nameof(Roll));

    public static readonly StyledProperty<IBrush> LineBrushProperty =
        AvaloniaProperty.Register<ArtificialHorizon, IBrush>(nameof(LineBrush), SolidColorBrush.Parse("White"));

    static ArtificialHorizon()
    {
        AffectsRender<ArtificialHorizon>(
            PitchProperty,
            RollProperty,
            LineBrushProperty);
    }

    public double Pitch
    {
        get => GetValue(PitchProperty);
        set => SetValue(PitchProperty, value);
    }

    public double Roll
    {
        get => GetValue(RollProperty);
        set => SetValue(RollProperty, value);
    }

    public IBrush LineBrush
    {
        get => GetValue(LineBrushProperty);
        set => SetValue(LineBrushProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        double width = Bounds.Width;
        double height = Bounds.Height;
        if (width <= 0 || height <= 0)
            return;

        double rollRadians = Roll * Math.PI / 180;
        var center = new Point(width / 2, height / 2);
        var lineAxis = new Vector(Math.Cos(rollRadians), Math.Sin(rollRadians));
        double halfSpacing = LineSpacing / 2;

        var pitchLadderPen = new Pen(LineBrush, PitchLadderStrokeThickness);
        DrawPitchLadder(context, pitchLadderPen, center, width, height);

        var horizonPen = new Pen(Brushes.White, HorizonStrokeThickness);
        DrawCenteredLine(context, horizonPen, Offset(center, lineAxis, -halfSpacing), lineAxis);
        DrawCenteredLine(context, horizonPen, Offset(center, lineAxis, halfSpacing), lineAxis);
    }

    void DrawPitchLadder(DrawingContext context, IPen pen, Point horizonCenter, double width, double height)
    {
        double centerX = horizonCenter.X;
        double centerY = horizonCenter.Y;
        int firstTickIndex = (int)Math.Floor((Pitch - PitchLadderVisibleDegrees) / PitchLadderStepDegrees);
        int lastTickIndex = (int)Math.Ceiling((Pitch + PitchLadderVisibleDegrees) / PitchLadderStepDegrees);

        for (int tickIndex = firstTickIndex; tickIndex <= lastTickIndex; tickIndex++)
        {
            DrawPitchTick(
                context,
                pen,
                tickIndex * PitchLadderStepDegrees,
                centerX,
                centerY,
                width,
                height);
        }
    }

    void DrawPitchTick(
        DrawingContext context,
        IPen pen,
        double tickPitch,
        double centerX,
        double centerY,
        double width,
        double height)
    {
        double pitchDelta = tickPitch - Pitch;
        double opacity = CalculatePitchTickOpacity(pitchDelta);
        if (opacity <= 0.0)
            return;

        double y = centerY - pitchDelta / PitchLadderStepDegrees * PitchLadderStepHeight;
        if (y < 0 || y > height)
            return;

        double halfWidth = PitchLadderTickWidth / 2;
        double halfSpacing = PitchLadderTickSpacing / 2;
        using (context.PushOpacity(opacity))
        {
            DrawCappedHorizontalLine(context, pen, centerX - halfSpacing, y, halfWidth, centerY, capLeft: true);
            DrawCappedHorizontalLine(context, pen, centerX + halfSpacing, y, halfWidth, centerY, capLeft: false);
            DrawPitchLabel(
                context,
                (int)tickPitch,
                centerX + halfSpacing + halfWidth + PitchLadderLabelGap,
                y,
                width,
                height);
        }
    }

    void DrawPitchLabel(
        DrawingContext context,
        int pitch,
        double x,
        double centerY,
        double width,
        double height)
    {
        string text = NormalizePitchLabel(pitch).ToString(CultureInfo.InvariantCulture);
        var formattedText = new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface(FontFamily.Default, FontStyle.Italic, FontWeight.Black),
            PitchLadderLabelFontSize,
            LineBrush);

        double y = centerY - formattedText.Height / 2;
        if (x + formattedText.Width > width || y < 0 || y + formattedText.Height > height)
            return;

        context.DrawText(formattedText, new Point(x, y));
    }

    static int NormalizePitchLabel(int pitch)
    {
        int normalized = pitch % 360;
        if (normalized > 180)
            normalized -= 360;
        else if (normalized < -180)
            normalized += 360;

        if (normalized > 90)
            normalized -= 180;
        else if (normalized < -90)
            normalized += 180;

        return normalized;
    }

    static double CalculatePitchTickOpacity(double pitchDelta)
    {
        double distance = Math.Abs(pitchDelta);
        if (distance > PitchLadderVisibleDegrees)
            return 0.0;

        double fadeStart = PitchLadderVisibleDegrees - PitchLadderFadeDegrees;
        if (distance <= fadeStart)
            return 1.0;

        return (PitchLadderVisibleDegrees - distance) / PitchLadderFadeDegrees;
    }

    static void DrawCappedHorizontalLine(
        DrawingContext context,
        IPen pen,
        double centerX,
        double y,
        double halfWidth,
        double centerY,
        bool capLeft)
    {
        double left = centerX - halfWidth;
        double right = centerX + halfWidth;
        double cappedX = capLeft ? left : right;
        double capDirection = Math.Sign(centerY - y);
        double capLength = Math.Min(Math.Abs(centerY - y) / PitchLadderStepHeight, 1.0) *
                           PitchLadderEndCapLength;

        context.DrawLine(pen, new Point(left, y), new Point(right, y));
        if (capLength > 0.0)
        {
            context.DrawLine(pen, new Point(cappedX, y), new Point(cappedX, y + capDirection * capLength));
        }
    }

    static void DrawCenteredLine(DrawingContext context, IPen pen, Point center, Vector axis)
    {
        double halfWidth = LineWidth / 2;
        context.DrawLine(pen, Offset(center, axis, -halfWidth), Offset(center, axis, halfWidth));
    }

    static Point Offset(Point point, Vector axis, double distance)
    {
        return new Point(point.X + axis.X * distance, point.Y + axis.Y * distance);
    }
}
