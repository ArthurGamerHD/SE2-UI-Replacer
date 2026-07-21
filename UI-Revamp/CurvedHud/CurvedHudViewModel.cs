using System;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Keen.Game2.Client.UI.HUD.Flight;
using Keen.Game2.Client.UI.HUD.Movement;
using Keen.Game2.Client.UI.Options;
using Keen.Game2.Simulation.GameSystems.Sun;
using Keen.Game2.Simulation.WorldObjects.Movement;
using Keen.VRage.Core;
using Keen.VRage.Core.Game.Components;
using Keen.VRage.Core.Game.Data;
using Keen.VRage.DCS.Components;
using Keen.VRage.Library.Mathematics;
using Keen.VRage.Render.EngineComponents;
using Keen.VRage.Voxels.Components;

namespace UI_Revamp.CurvedHud;

public sealed class CurvedHudViewModel : INotifyPropertyChanged
{
    const double VECTOR_EPSILON_SQUARED = 0.0000000001;
    const int FLIGHT_HUD_VIEW_MODEL_REFRESH_FRAMES = 15;
    static readonly SunSessionComponentObjectBuilder DefaultSunState = new();
    static readonly Vector3D DefaultSunAxis = NormalizeOrDefault(DefaultSunState.Axis, Vector3D.Forward);
    static readonly Vector3D DefaultSunStartDirection = CreateDefaultSunStartDirection(DefaultSunState);
    static readonly PropertyInfo? FlightHudTargetProperty = typeof(FlightHUDViewModel).GetProperty(
        "Target",
        BindingFlags.Instance | BindingFlags.NonPublic);
    static readonly FieldInfo? MovementHudGuiOptionsField = typeof(MovementHUDScreenViewModel).GetField(
        "_guiOptions",
        BindingFlags.Instance | BindingFlags.NonPublic);
    readonly SunSessionComponentObjectBuilder _sunState = new();
    double _bearing;
    double _compassVisibleDegrees = 70.0;
    double _pitch;
    double _roll;
    double _speedBarValue;
    double _altitudeBarValue;
    double _nearestPlanetDistance;
    bool _showFlightHud;
    bool _hasNearestPlanet;
    bool _isInSpace;
    int _flightHudViewModelRefreshFrames;
    FlightHUDViewModel? _flightHudViewModel;
    string _speedValueText = "0.0";
    string _speedUnitText = "m/s";
    string _altitudeValueText = "0m";
    string? _nearestPlanetName;

    public event PropertyChangedEventHandler? PropertyChanged;

    public double Bearing
    {
        get => _bearing;
        private set => SetProperty(ref _bearing, NormalizeDegrees(value));
    }

    public double CompassVisibleDegrees
    {
        get => _compassVisibleDegrees;
        private set => SetProperty(ref _compassVisibleDegrees, Clamp(value, 20.0, 180.0));
    }

    public double Pitch
    {
        get => _pitch;
        private set => SetProperty(ref _pitch, value);
    }

    public double Roll
    {
        get => _roll;
        private set => SetProperty(ref _roll, value);
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
        private set
        {
            if (Equals(_hasNearestPlanet, value))
                return;

            SetProperty(ref _hasNearestPlanet, value);
            OnPlanetHudStateChanged();
        }
    }

    public bool IsInSpace
    {
        get => _isInSpace;
        private set
        {
            if (Equals(_isInSpace, value))
                return;

            SetProperty(ref _isInSpace, value);
            OnPlanetHudStateChanged();
        }
    }

    public bool ShowFlightHud
    {
        get => _showFlightHud;
        private set
        {
            if (Equals(_showFlightHud, value))
                return;

            SetProperty(ref _showFlightHud, value);
            OnHudStateChanged();
        }
    }

    public bool ShowPlanetHud => ShowFlightHud && HasNearestPlanet && !IsInSpace;

    public bool ShowSpaceReticle => ShowFlightHud && !ShowPlanetHud;

    public string? NearestPlanetName
    {
        get => _nearestPlanetName;
        private set => SetProperty(ref _nearestPlanetName, value);
    }

    public double NearestPlanetDistance
    {
        get => _nearestPlanetDistance;
        private set => SetProperty(ref _nearestPlanetDistance, value);
    }

    public void UpdateFromGame()
    {
        bool nativeFlightHudVisible = NativeFlightHudController.IsNativeFlightHudVisible();
        bool hasFlightState = TryGetGameFlightState(
            out double pitch,
            out double roll,
            out bool isInSpace);
        if (hasFlightState)
        {
            Pitch = pitch;
            Roll = roll;
            IsInSpace = isInSpace;
            UpdateReadouts(_flightHudViewModel);
        }
        else
        {
            Pitch = 0.0;
            Roll = 0.0;
            UpdateReadouts(null);
        }

        ShowFlightHud = Plugin.Settings.CompactFlightHud && nativeFlightHudVisible;

        Entity? flightHudTargetEntity = TryGetFlightHudTargetEntity();
        Entity? observerEntity = flightHudTargetEntity ??
                                 Plugin.ClientPlayers
                                     ?.LocalPlayerController
                                     ?.GetTopObservableEntity();
        Entity? cameraEntity = Plugin.ClientPlayers
            ?.LocalPlayerController
            ?.CameraSystem
            ?.RenderCameraEntity;
        Entity? headingEntity = flightHudTargetEntity ?? cameraEntity ?? observerEntity;
        Entity? contextEntity = observerEntity ?? cameraEntity;

        if (!TryGetTransform(contextEntity, out WorldTransform observerTransform) ||
            !TryGetTransform(headingEntity, out WorldTransform headingTransform) ||
            !TryGetNearestPlanet(
                contextEntity!,
                observerTransform.Position,
                out VoxelPlanetComponent planetComponent,
                out WorldTransform planetTransform,
                out double distance))
        {
            HasNearestPlanet = false;
            IsInSpace = true;
            NearestPlanetName = null;
            NearestPlanetDistance = 0.0;
            Bearing = 0.0;
            return;
        }

        if (TryGetControlReferenceTransform(flightHudTargetEntity, headingTransform, out WorldTransform controlReferenceTransform))
        {
            headingTransform = controlReferenceTransform;
        }

        HasNearestPlanet = true;
        IsInSpace = hasFlightState && isInSpace;
        NearestPlanetName = planetComponent.Name;
        NearestPlanetDistance = distance;
        CompassVisibleDegrees = GetRenderCameraFovDegrees();
        Bearing = CalculateBearing(
            observerTransform.Position,
            headingTransform,
            planetTransform,
            GetSunAxis(contextEntity!));
    }

    bool TryGetGameFlightState(out double pitch, out double roll, out bool isInSpace)
    {
        if (_flightHudViewModel == null || _flightHudViewModelRefreshFrames <= 0)
        {
            _flightHudViewModel = FindFlightHudViewModel();
            _flightHudViewModelRefreshFrames = FLIGHT_HUD_VIEW_MODEL_REFRESH_FRAMES;
        }
        else
        {
            _flightHudViewModelRefreshFrames--;
        }

        if (_flightHudViewModel == null)
        {
            pitch = 0.0;
            roll = 0.0;
            isInSpace = true;
            return false;
        }

        pitch = _flightHudViewModel.PitchDegrees;
        roll = _flightHudViewModel.EffectiveRollDegrees;
        isInSpace = _flightHudViewModel.IsInSpace;
        return true;
    }

    void UpdateReadouts(FlightHUDViewModel? flightHudViewModel)
    {
        if (flightHudViewModel == null)
        {
            SpeedBarValue = 0.0;
            SpeedValueText = "0.0";
            SpeedUnitText = "m/s";
            AltitudeBarValue = 0.0;
            AltitudeValueText = "0m";
            return;
        }

        MovementHUDScreenViewModel movementHudViewModel = flightHudViewModel.MovementHUDScreenViewModel;
        SpeedBarValue = CalculatePercent(movementHudViewModel.Speed, movementHudViewModel.MaxSpeed);
        (SpeedValueText, SpeedUnitText) = FormatSpeed(
            movementHudViewModel.Speed,
            TryGetSpeedUnit(movementHudViewModel));

        AltitudeBarValue = CalculatePercent(flightHudViewModel.BarValue, flightHudViewModel.MaxAltitude);
        AltitudeValueText = FormatDistance(flightHudViewModel.Altitude);
    }

    static SpeedUnit TryGetSpeedUnit(MovementHUDScreenViewModel movementHudViewModel)
    {
        return MovementHudGuiOptionsField?.GetValue(movementHudViewModel) is GUIOptions guiOptions
            ? guiOptions.SpeedUnit
            : SpeedUnit.MetersPerSecond;
    }

    static double CalculatePercent(double value, double maximum)
    {
        if (!double.IsFinite(value) || !double.IsFinite(maximum) || maximum <= 0.0)
        {
            return 0.0;
        }

        return value / maximum * 100.0;
    }

    static (string Value, string Unit) FormatSpeed(float metersPerSecond, SpeedUnit speedUnit)
    {
        double converted = speedUnit switch
        {
            SpeedUnit.KilometersPerHour => metersPerSecond * 3.6,
            SpeedUnit.MilesPerHour => metersPerSecond * 2.23694,
            _ => metersPerSecond
        };

        string valueFormat = speedUnit == SpeedUnit.MetersPerSecond ? "0.0" : "0";
        string unit = speedUnit switch
        {
            SpeedUnit.KilometersPerHour => "km/h",
            SpeedUnit.MilesPerHour => "mph",
            _ => "m/s"
        };

        return (converted.ToString(valueFormat, CultureInfo.InvariantCulture), unit);
    }

    static string FormatDistance(int meters)
    {
        int absoluteMeters = Math.Abs(meters);
        if (absoluteMeters >= 1000)
        {
            double kilometers = absoluteMeters / 1000.0;
            string sign = meters < 0 ? "-" : string.Empty;
            return sign + kilometers.ToString("0.##", CultureInfo.InvariantCulture) + "km";
        }

        return meters.ToString("0", CultureInfo.InvariantCulture) + "m";
    }

    static FlightHUDViewModel? FindFlightHudViewModel()
    {
        TopLevel? mainWindow = Plugin.MainWindow;
        if (mainWindow == null)
        {
            return null;
        }

        if (TryGetFlightHudViewModel(mainWindow.DataContext, out FlightHUDViewModel? rootViewModel))
        {
            return rootViewModel;
        }

        return mainWindow.GetVisualDescendants()
            .OfType<Control>()
            .Select(control => TryGetFlightHudViewModel(control.DataContext, out FlightHUDViewModel? viewModel)
                ? viewModel
                : null)
            .Where(viewModel => viewModel != null)
            .FirstOrDefault();
    }

    static bool TryGetFlightHudViewModel(object? dataContext, out FlightHUDViewModel? viewModel)
    {
        if (dataContext is FlightHUDViewModel directViewModel)
        {
            viewModel = directViewModel;
            return true;
        }

        PropertyInfo? property = dataContext
            ?.GetType()
            .GetProperty("FlightHUDViewModel", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        viewModel = property?.GetValue(dataContext) as FlightHUDViewModel;
        return viewModel != null;
    }

    Entity? TryGetFlightHudTargetEntity()
    {
        if (_flightHudViewModel == null || FlightHudTargetProperty == null)
        {
            return null;
        }

        return FlightHudTargetProperty.GetValue(_flightHudViewModel) as Entity;
    }

    static bool TryGetControlReferenceTransform(
        Entity? targetEntity,
        WorldTransform fallbackTransform,
        out WorldTransform transform)
    {
        transform = fallbackTransform;

        Entity? controlEntity = TryGetControlReferenceEntity(targetEntity);
        if (controlEntity == null)
        {
            return false;
        }

        if (controlEntity.Data.TryGet<TargetControlData>(out TargetControlData targetControl) &&
            targetControl.Operable)
        {
            transform = new WorldTransform(
                fallbackTransform.Position,
                targetControl.TargetOrientation * targetControl.RelativeCockpitOrientation);
            return true;
        }

        if (!controlEntity.Data.TryGet<ControlReferenceOrientation>(out ControlReferenceOrientation controlReference) ||
            !TryGetTransform(controlEntity, out WorldTransform controlTransform))
        {
            return false;
        }

        transform = new WorldTransform(
            fallbackTransform.Position,
            controlTransform.Orientation * controlReference.Orientation);
        return true;
    }

    static Entity? TryGetControlReferenceEntity(Entity? targetEntity)
    {
        return targetEntity?.GetTopLevelParent();
    }

    Vector3D GetSunAxis(Entity contextEntity)
    {
        SunSessionComponent? sun = contextEntity.GetSession().TryGet<SunSessionComponent>() ??
                                   contextEntity.GetSession().SessionComponents.FirstOrDefault<SunSessionComponent>();
        if (sun == null)
        {
            return DefaultSunAxis;
        }

        sun.Serialize(_sunState);
        return NormalizeOrDefault(_sunState.Axis, DefaultSunAxis);
    }

    static bool TryGetTransform(Entity? entity, out WorldTransform transform)
    {
        transform = default;

        if (entity == null)
        {
            return false;
        }

        transform = entity.Data.GetWorldTransform();
        return true;
    }

    static double GetRenderCameraFovDegrees()
    {
        float fov = RenderEngineComponent.Instance.RenderContracts.GetSettings().CameraFov;
        return fov > 0.0f && float.IsFinite(fov)
            ? MathHelper.ToDegrees(fov)
            : 70.0;
    }

    static bool TryGetNearestPlanet(
        Entity contextEntity,
        Vector3D observerPosition,
        out VoxelPlanetComponent planetComponent,
        out WorldTransform planetTransform,
        out double distance)
    {
        planetComponent = null!;
        planetTransform = default;
        double closestDistanceSquared = double.MaxValue;

        PlanetTrackerSessionComponent? planetTracker = contextEntity.GetSession().TryGet<PlanetTrackerSessionComponent>();
        PlanetTrackerSessionComponent.Planet? trackedPlanet = planetTracker?.TryFindPlanet(
            observerPosition,
            FindClosestTrackedPlanetIndex);
        if (trackedPlanet != null)
        {
            planetComponent = trackedPlanet.Component;
            planetTransform = trackedPlanet.Transform;
            distance = Vector3D.Distance(in planetTransform.Position, in observerPosition);
            return true;
        }

        foreach (Entity candidate in contextEntity.GetSession().GetEntitiesOfType<VoxelPlanetComponent>())
        {
            if (!candidate.Data.Has<WorldTransform>())
            {
                continue;
            }

            WorldTransform candidateTransform = candidate.Data.GetWorldTransform();
            double distanceSquared = Vector3D.DistanceSquared(
                in candidateTransform.Position,
                in observerPosition);

            if (distanceSquared >= closestDistanceSquared)
            {
                continue;
            }

            closestDistanceSquared = distanceSquared;
            planetComponent = candidate.Get<VoxelPlanetComponent>();
            planetTransform = candidateTransform;
        }

        if (planetComponent == null)
        {
            distance = 0.0;
            return false;
        }

        distance = Math.Sqrt(closestDistanceSquared);
        return true;
    }

    static int FindClosestTrackedPlanetIndex(Vector3D observerPosition, ReadOnlySpan<PlanetTrackerSessionComponent.Planet> planets)
    {
        int closestIndex = -1;
        double closestDistanceSquared = double.MaxValue;

        for (int i = 0; i < planets.Length; i++)
        {
            double distanceSquared = Vector3D.DistanceSquared(
                planets[i].Transform.Position,
                in observerPosition);
            if (distanceSquared >= closestDistanceSquared)
            {
                continue;
            }

            closestDistanceSquared = distanceSquared;
            closestIndex = i;
        }

        return closestIndex;
    }

    static double CalculateBearing(
        Vector3D observerPosition,
        WorldTransform cameraTransform,
        WorldTransform planetTransform,
        Vector3D northPoleAxis)
    {
        Vector3D up = observerPosition - planetTransform.Position;
        if (up.LengthSquared() <= VECTOR_EPSILON_SQUARED)
        {
            return 0.0;
        }

        up.Normalize();

        Vector3D north = ProjectOntoTangentPlane(northPoleAxis, up);
        if (north.LengthSquared() <= VECTOR_EPSILON_SQUARED)
        {
            // At the poles the sun-axis projection is undefined; keep the same meridian as the default sun setup.
            north = CreateFallbackNorth(up);
        }

        north.Normalize();

        Vector3D east = Vector3D.Cross(in north, in up);
        if (east.LengthSquared() <= VECTOR_EPSILON_SQUARED)
        {
            return 0.0;
        }

        east.Normalize();

        Vector3D heading = ProjectOntoTangentPlane(
            cameraTransform.Orientation.GetForward(),
            up);
        if (heading.LengthSquared() <= VECTOR_EPSILON_SQUARED)
        {
            heading = ProjectOntoTangentPlane(
                cameraTransform.Orientation.GetUp(),
                up);
        }

        if (heading.LengthSquared() <= VECTOR_EPSILON_SQUARED)
        {
            return 0.0;
        }

        heading.Normalize();

        double radians = Math.Atan2(
            Vector3D.Dot(in heading, in east),
            Vector3D.Dot(in heading, in north));
        return MathHelper.ToDegrees(radians);
    }

    static Vector3D ProjectOntoTangentPlane(Vector3D vector, Vector3D up)
    {
        return vector - up * Vector3D.Dot(in vector, in up);
    }

    static Vector3D CreateFallbackNorth(Vector3D up)
    {
        Vector3D north = ProjectOntoTangentPlane(DefaultSunStartDirection, up);
        if (north.LengthSquared() > VECTOR_EPSILON_SQUARED)
        {
            return north;
        }

        north = ProjectOntoTangentPlane(DefaultSunAxis, up);
        if (north.LengthSquared() > VECTOR_EPSILON_SQUARED)
        {
            return north;
        }

        return Vector3D.CalculatePerpendicularVector(in up);
    }

    static Vector3D CreateDefaultSunStartDirection(SunSessionComponentObjectBuilder sunState)
    {
        Vector3D axis = NormalizeOrDefault(sunState.Axis, Vector3D.Forward);
        Vector3D startDirection = NormalizeOrDefault(sunState.StartAngle, Vector3D.Right);
        Vector3D projected = ProjectOntoTangentPlane(startDirection, axis);
        return NormalizeOrDefault(projected, Vector3D.Right);
    }

    static Vector3D NormalizeOrDefault(Vector3D vector, Vector3D fallback)
    {
        return vector.LengthSquared() > VECTOR_EPSILON_SQUARED
            ? Vector3D.Normalize(vector)
            : fallback;
    }

    static double NormalizeDegrees(double value)
    {
        double normalized = value % 360.0;
        return normalized < 0.0 ? normalized + 360.0 : normalized;
    }

    static double Clamp(double value, double minimum, double maximum)
    {
        if (double.IsNaN(value))
            return minimum;

        return Math.Clamp(value, minimum, maximum);
    }

    void OnPlanetHudStateChanged()
    {
        OnHudStateChanged();
    }

    void OnHudStateChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowPlanetHud)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowSpaceReticle)));
    }

    void SetProperty<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(storage, value))
        {
            return;
        }

        storage = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
