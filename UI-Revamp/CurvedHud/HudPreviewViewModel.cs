#if PREVIEW
using System;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace UI_Revamp.CurvedHud;

public sealed class HudPreviewViewModel : INotifyPropertyChanged
{
    const double MaxSpeedSliderMaximum = 1300.0;
    const double MaxAltitudeSliderMaximum = 100000.0;
    double _bearing = 90.0;
    double _compassVisibleDegrees = 70.0;
    double _pitch;
    double _roll;
    double _speed = 250;
    double _maxSpeed = 300.0;
    double _altitude = 4300.0;
    double _maxAltitude = 43210.0;
    double _speedBarValue;
    double _altitudeBarValue;
    bool _hasNearestPlanet = true;
    bool _isInSpace;
    bool _showUtilityPanel;
    bool _showDesignBackground = true;
    string _speedValueText = string.Empty;
    string _speedUnitText = "m/s";
    string _altitudeValueText = string.Empty;

    public HudPreviewViewModel()
    {
        RefreshSpeedReadout();
        RefreshAltitudeReadout();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public double Bearing
    {
        get => _bearing;
        set => SetProperty(ref _bearing, Clamp(value, 0.0, 360.0));
    }

    public double CompassVisibleDegrees
    {
        get => _compassVisibleDegrees;
        set => SetProperty(ref _compassVisibleDegrees, Clamp(value, 20.0, 180.0));
    }

    public double Pitch
    {
        get => _pitch;
        set => SetProperty(ref _pitch, Clamp(value, -90.0, 90.0));
    }

    public double Roll
    {
        get => _roll;
        set => SetProperty(ref _roll, Clamp(value, -180.0, 180.0));
    }

    public double Speed
    {
        get => _speed;
        set
        {
            if (!SetProperty(ref _speed, Clamp(value, 0.0, MaxSpeed)))
                return;

            RefreshSpeedReadout();
        }
    }

    public double MaxSpeed
    {
        get => _maxSpeed;
        set
        {
            if (!SetProperty(ref _maxSpeed, Clamp(value, 0.1, MaxSpeedSliderMaximum)))
                return;

            if (Speed > MaxSpeed)
                Speed = MaxSpeed;

            RefreshSpeedReadout();
        }
    }

    public double SpeedBarValue
    {
        get => _speedBarValue;
        private set => SetProperty(ref _speedBarValue, Clamp(value, 0.0, 100.0));
    }

    public string SpeedValueText
    {
        get => _speedValueText;
        private set => SetProperty(ref _speedValueText, value);
    }

    public string SpeedUnitText
    {
        get => _speedUnitText;
        private set => SetProperty(ref _speedUnitText, value);
    }

    public double Altitude
    {
        get => _altitude;
        set
        {
            if (!SetProperty(ref _altitude, Clamp(value, 0.0, MaxAltitude)))
                return;

            RefreshAltitudeReadout();
        }
    }

    public double MaxAltitude
    {
        get => _maxAltitude;
        set
        {
            if (!SetProperty(ref _maxAltitude, Clamp(value, 1.0, MaxAltitudeSliderMaximum)))
                return;

            if (Altitude > MaxAltitude)
                Altitude = MaxAltitude;

            RefreshAltitudeReadout();
        }
    }

    public double AltitudeBarValue
    {
        get => _altitudeBarValue;
        private set => SetProperty(ref _altitudeBarValue, Clamp(value, 0.0, 100.0));
    }

    public string AltitudeValueText
    {
        get => _altitudeValueText;
        private set => SetProperty(ref _altitudeValueText, value);
    }

    public bool HasNearestPlanet
    {
        get => _hasNearestPlanet;
        set
        {
            if (!SetProperty(ref _hasNearestPlanet, value))
                return;

            OnPlanetHudStateChanged();
        }
    }

    public bool IsInSpace
    {
        get => _isInSpace;
        set
        {
            if (!SetProperty(ref _isInSpace, value))
                return;

            OnPlanetHudStateChanged();
        }
    }

    public bool ShowDesignBackground
    {
        get => _showDesignBackground;
        set => SetProperty(ref _showDesignBackground, value);
    }

    public bool ShowUtilityPanel
    {
        get => _showUtilityPanel;
        set => SetProperty(ref _showUtilityPanel, value);
    }

    public bool ShowPlanetHud => HasNearestPlanet && !IsInSpace;

    public bool ShowSpaceReticle => !ShowPlanetHud;

    public void Reset()
    {
        Bearing = 90.0;
        CompassVisibleDegrees = 70.0;
        Pitch = 0.0;
        Roll = 0.0;
        Speed = 123.4;
        MaxSpeed = 300.0;
        Altitude = 1430.0;
        MaxAltitude = 43210.0;
        HasNearestPlanet = true;
        IsInSpace = false;
        ShowDesignBackground = true;
    }

    static double Clamp(double value, double minimum, double maximum)
    {
        if (double.IsNaN(value))
            return minimum;

        return Math.Clamp(value, minimum, maximum);
    }

    static double CalculatePercent(double value, double maximum)
    {
        if (!double.IsFinite(value) || !double.IsFinite(maximum) || maximum <= 0.0)
            return 0.0;

        return value / maximum * 100.0;
    }

    static string FormatDistance(double meters)
    {
        int roundedMeters = (int)Math.Round(meters, MidpointRounding.AwayFromZero);
        int absoluteMeters = Math.Abs(roundedMeters);
        if (absoluteMeters >= 1000)
        {
            double kilometers = absoluteMeters / 1000.0;
            string sign = roundedMeters < 0 ? "-" : string.Empty;
            return sign + kilometers.ToString("0.##", CultureInfo.InvariantCulture) + "km";
        }

        return roundedMeters.ToString("0", CultureInfo.InvariantCulture) + "m";
    }

    void RefreshSpeedReadout()
    {
        SpeedBarValue = CalculatePercent(Speed, MaxSpeed);
        SpeedValueText = Speed.ToString("0.0", CultureInfo.InvariantCulture);
        SpeedUnitText = "m/s";
    }

    void RefreshAltitudeReadout()
    {
        AltitudeBarValue = CalculatePercent(Altitude, MaxAltitude);
        AltitudeValueText = FormatDistance(Altitude);
    }

    void OnPlanetHudStateChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowPlanetHud)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowSpaceReticle)));
    }

    bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(storage, value))
            return false;

        storage = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}
#endif
