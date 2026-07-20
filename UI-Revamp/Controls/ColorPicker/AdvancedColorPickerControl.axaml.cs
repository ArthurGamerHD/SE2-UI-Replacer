using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using Keen.VRage.Library.Mathematics;
using Keen.VRage.UI.Shared.Extensions;

namespace UI_Revamp.Controls.ColorPicker;

public class AdvancedColorPickerControl : TemplatedControl
{
    public static readonly StyledProperty<string?> LabelProperty =
        AvaloniaProperty.Register<AdvancedColorPickerControl, string?>(nameof(Label));

    public static readonly StyledProperty<SolidColorBrush> ColorPreviewProperty =
        AvaloniaProperty.Register<AdvancedColorPickerControl, SolidColorBrush>(nameof(ColorPreview));

    public static readonly DirectProperty<AdvancedColorPickerControl, string> HexColorProperty =
        AvaloniaProperty.RegisterDirect<AdvancedColorPickerControl, string>(nameof(HexColor), control => control.HexColor,
            (control, value) => control.HexColor = value);

    public static readonly DirectProperty<AdvancedColorPickerControl, ColorHSV> ColorProperty =
        AvaloniaProperty.RegisterDirect<AdvancedColorPickerControl, ColorHSV>(nameof(Color), control => control.Color,
            (control, value) => control.Color = value);

    Border? _wheelColor;
    Grid? _wheel;
    Ellipse? _wheelSelector;
    Slider _sliderH = null!;
    Slider _sliderS = null!;
    Slider _sliderV = null!;
    ColorHSV _color;
    bool _isManualInput;
    int _internalChange;
    bool _hasTemplate;

    public string? Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public SolidColorBrush ColorPreview
    {
        get => GetValue(ColorPreviewProperty);
        set => SetValue(ColorPreviewProperty, value);
    }

    public string HexColor
    {
        get => $"#{Color.ToSRGB().ToHtml()}";
        set
        {
            if (value.Length < 6 || value.Length > 7)
            {
                return;
            }

            if (!value.StartsWith("#"))
            {
                value = "#" + value;
            }

            if (!Avalonia.Media.Color.TryParse(value, out var color))
            {
                return;
            }

            _isManualInput = true;
            Color = color.ToHSV();
            UpdateSliders();
            _isManualInput = false;
        }
    }

    public ColorHSV Color
    {
        get => _color;
        set
        {
            value.A = 1;

            var oldHex = HexColor;
            var oldColor = Color;

            _color = value;
            RaisePropertyChanged(ColorProperty, oldColor, _color);

            if (!_isManualInput)
            {
                RaisePropertyChanged(HexColorProperty, oldHex, HexColor);
            }

            UpdatePreview();

            if (_internalChange == 0 && _hasTemplate)
            {
                UpdateSliders();
            }
        }
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        if (_wheel != null)
        {
            _wheel.PointerPressed -= OnWheelPointerPressed;
            _wheel.PointerMoved -= OnWheelPointerMoved;
            _wheel.PropertyChanged -= OnWheelPropertyChanged;
        }

        _wheel = e.NameScope.Find<Grid>("PART_Wheel");

        if (_wheel != null)
        {
            _wheel.PointerPressed += OnWheelPointerPressed;
            _wheel.PointerMoved += OnWheelPointerMoved;
            _wheel.PropertyChanged += OnWheelPropertyChanged;
        }

        if (_sliderH != null)
        {
            _sliderH.ValueChanged -= OnSliderHValueChanged;
        }

        if (_sliderS != null)
        {
            _sliderS.ValueChanged -= OnSliderSValueChanged;
        }

        if (_sliderV != null)
        {
            _sliderV.ValueChanged -= OnSliderVValueChanged;
        }

        _sliderH = e.NameScope.Find<Slider>("PART_SliderH")!;
        _sliderS = e.NameScope.Find<Slider>("PART_SliderS")!;
        _sliderV = e.NameScope.Find<Slider>("PART_SliderV")!;

        _sliderH.ValueChanged += OnSliderHValueChanged;
        _sliderS.ValueChanged += OnSliderSValueChanged;
        _sliderV.ValueChanged += OnSliderVValueChanged;

        UpdateSliders();

        _wheelColor = e.NameScope.Find<Border>("PART_WheelColor");
        _wheelSelector = e.NameScope.Find<Ellipse>("PART_WheelSelector");

        UpdatePreview();
        _hasTemplate = true;
    }

    void UpdatePreview()
    {
        if (_wheelColor != null)
        {
            _wheelColor.Opacity = Color.V;
        }

        ColorPreview = new SolidColorBrush(_color.ToAvalonia());

        if (_wheel == null || _wheelSelector == null)
        {
            return;
        }

        var bounds = _wheel.Bounds.Size / 2;
        var angleRadians = MathHelper.ToRadians(Color.H * 360 + 90);
        var distance = Color.S * ((bounds.Width + bounds.Height) / 2);
        var x = distance * Math.Cos(angleRadians);
        var y = distance * Math.Sin(angleRadians);

        _wheelSelector.RenderTransform = new TranslateTransform(x, y);
    }

    void OnWheelPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == BoundsProperty)
        {
            UpdatePreview();
        }
    }

    void OnSliderVValueChanged(object? sender, RangeBaseValueChangedEventArgs args)
    {
        _internalChange++;
        if (Math.Abs(Color.V - args.NewValue / 100) > 0.01)
        {
            Color = new ColorHSV((float)(_sliderH.Value / 360d), (float)(_sliderS.Value / 100d), (float)(args.NewValue / 100d), 1);
        }

        _internalChange--;
    }

    void OnSliderSValueChanged(object? sender, RangeBaseValueChangedEventArgs args)
    {
        _internalChange++;
        if (Math.Abs(Color.S - args.NewValue / 100) > 0.01)
        {
            Color = new ColorHSV((float)_sliderH.Value / 360, (float)(args.NewValue / 100), (float)(_sliderV.Value / 100), 1);
        }

        _internalChange--;
    }

    void OnSliderHValueChanged(object? sender, RangeBaseValueChangedEventArgs args)
    {
        _internalChange++;
        if (Math.Abs(Color.H - args.NewValue / 360) > 0.003)
        {
            Color = new ColorHSV((float)args.NewValue / 360, (float)(_sliderS.Value / 100), (float)(_sliderV.Value / 100), 1);
        }

        _internalChange--;
    }

    void UpdateSliders()
    {
        _sliderH.Value = (int)(_color.H * 360);
        _sliderS.Value = (int)(_color.S * 100);
        _sliderV.Value = (int)(_color.V * 100);
    }

    void OnWheelPointerPressed(object? sender, PointerPressedEventArgs args)
    {
        SetColorFromWheel(args);
    }

    void OnWheelPointerMoved(object? sender, PointerEventArgs args)
    {
        if (_wheel == null)
        {
            return;
        }

        if (args.GetCurrentPoint(_wheel).Properties.IsLeftButtonPressed)
        {
            SetColorFromWheel(args);
        }
    }

    void SetColorFromWheel(PointerEventArgs args)
    {
        if (_wheel == null || _wheelSelector == null)
        {
            return;
        }

        var pos = args.GetPosition(_wheel);
        var bounds = _wheel.Bounds.Size / 2;
        var relativePosition = pos - new Point(bounds.Width, bounds.Height);
        var distance = Math.Sqrt(relativePosition.X * relativePosition.X + relativePosition.Y * relativePosition.Y);
        var angleRadians = Math.Atan2(relativePosition.Y, relativePosition.X) - Math.PI / 2;
        var angleDegrees = angleRadians * (180.0 / Math.PI);

        switch (angleDegrees)
        {
            case < 0:
                angleDegrees += 360;
                break;
            case >= 360:
                angleDegrees -= 360;
                break;
        }

        _sliderS.Value = (int)(Math.Clamp(distance / ((bounds.Height + bounds.Width) / 2), 0, 1) * 100);
        _sliderH.Value = (int)angleDegrees;
    }
}
