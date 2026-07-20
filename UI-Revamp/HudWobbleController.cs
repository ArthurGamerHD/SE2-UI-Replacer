using System;
using Keen.VRage.Core;
using Keen.VRage.Library.Diagnostics;
using Keen.VRage.Library.Mathematics;
using UI_Revamp.CurvedHud;

namespace UI_Revamp;

internal static class HudWobbleController
{
    const double ACCELERATION_TO_PIXELS = 0.35;
    const double ACCELERATION_TO_SCALE = 0.0025;
    const double MAX_OFFSET_PIXELS = 16;
    const double MIN_SCALE = 0.99;
    const double MAX_SCALE = 1.01;
    const double NEUTRAL_SCALE = 1.0;
    const double RETURN_ALPHA_PER_UI_FRAME = 0.08;
    const double WOBBLE_FOLLOW_ALPHA_PER_UI_FRAME = 0.18;
    const double ACCELERATION_RETURN_ALPHA_PER_UI_FRAME = 0.12;
    const double POSITION_EPSILON_SQUARED = 0.0000000001;
    const float SIMULATION_TICKS_PER_SECOND = 60.0f;
    static Vector3D _lastSimulationPosition;
    static Vector3 _lastSimulationVelocity;
    static Vector3 _accelerationWorld;
    static double _offsetX;
    static double _offsetY;
    static double _scale = 1.0;
    static bool _hasPositionSample;
    static bool _hasVelocitySample;
    static bool _loggedFailure;

    public static void UiFrame()
    {
        try
        {
            var multiplier = Plugin.Settings.HudWobbleMultiplier;
            if (multiplier <= 0.0)
            {
                ResetAccelerationSamples();
                ReturnToNeutral();
                return;
            }

            if (!TryGetCameraTransform(out var cameraTransform))
            {
                ResetAccelerationSamples();
                ReturnToNeutral();
                return;
            }

            UpdateAccelerationSample(cameraTransform.Position);
            var acceleration = WorldTransform.TransformDirectionInv(_accelerationWorld, cameraTransform);

            ApplySmoothedWobble(
                EaseOffset(acceleration.X * ACCELERATION_TO_PIXELS * multiplier),
                EaseOffset(-acceleration.Y * ACCELERATION_TO_PIXELS * multiplier),
                EaseScale(NEUTRAL_SCALE + acceleration.Z * ACCELERATION_TO_SCALE * multiplier));
        }
        catch (Exception e)
        {
            if (!_loggedFailure)
            {
                _loggedFailure = true;
                Log.Default.Error($"[{Plugin.PluginId}] Failed to update HUD wobble: {e}");
            }
        }
    }

    static void UpdateAccelerationSample(Vector3D position)
    {
        if (!_hasPositionSample)
        {
            _lastSimulationPosition = position;
            _lastSimulationVelocity = Vector3.Zero;
            _accelerationWorld = Vector3.Zero;
            _hasPositionSample = true;
            _hasVelocitySample = false;
            return;
        }

        var positionDelta = position - _lastSimulationPosition;
        if (positionDelta.LengthSquared() <= POSITION_EPSILON_SQUARED)
        {
            ReturnAccelerationToNeutral();
            return;
        }

        var velocity = (Vector3)(positionDelta * SIMULATION_TICKS_PER_SECOND);
        _lastSimulationPosition = position;

        if (!_hasVelocitySample)
        {
            _lastSimulationVelocity = velocity;
            _accelerationWorld = Vector3.Zero;
            _hasVelocitySample = true;
            return;
        }

        _accelerationWorld = (velocity - _lastSimulationVelocity) * SIMULATION_TICKS_PER_SECOND;
        _lastSimulationVelocity = velocity;
    }

    static void ReturnAccelerationToNeutral()
    {
        _accelerationWorld += (Vector3.Zero - _accelerationWorld) * (float)ACCELERATION_RETURN_ALPHA_PER_UI_FRAME;
    }

    static void ResetAccelerationSamples()
    {
        _accelerationWorld = Vector3.Zero;
        _lastSimulationVelocity = Vector3.Zero;
        _hasPositionSample = false;
        _hasVelocitySample = false;
    }

    static bool TryGetCameraTransform(out WorldTransform transform)
    {
        transform = default;

        var cameraEntity = Plugin.ClientPlayers?.LocalPlayerController?.CameraSystem?.RenderCameraEntity;
        if (cameraEntity == null || !cameraEntity.Data.Has<WorldTransform>())
        {
            return false;
        }

        transform = cameraEntity.Data.Get<WorldTransform>();
        return true;
    }

    static void ReturnToNeutral()
    {
        ApplySmoothedWobble(
            _offsetX + (0.0 - _offsetX) * RETURN_ALPHA_PER_UI_FRAME,
            _offsetY + (0.0 - _offsetY) * RETURN_ALPHA_PER_UI_FRAME,
            _scale + (NEUTRAL_SCALE - _scale) * RETURN_ALPHA_PER_UI_FRAME);
    }

    static void ApplySmoothedWobble(double targetX, double targetY, double targetScale)
    {
        ApplyWobble(
            _offsetX + (targetX - _offsetX) * WOBBLE_FOLLOW_ALPHA_PER_UI_FRAME,
            _offsetY + (targetY - _offsetY) * WOBBLE_FOLLOW_ALPHA_PER_UI_FRAME,
            _scale + (targetScale - _scale) * WOBBLE_FOLLOW_ALPHA_PER_UI_FRAME);
    }

    static void ApplyWobble(double x, double y, double scale)
    {
        _offsetX = x;
        _offsetY = y;
        _scale = scale;

        // The curved HUD is composed as one generated texture, so its motion
        // is forwarded to the composition sprite; the custom pixel shader
        // decodes and applies it before curvature.
        CurvedHudController.SetWobble(_offsetX, _offsetY, _scale);
    }

    static double EaseOffset(double value)
    {
        return EaseSignedMagnitude(value, MAX_OFFSET_PIXELS);
    }

    static double EaseScale(double value)
    {
        var delta = value - NEUTRAL_SCALE;
        var maxMagnitude = delta < 0.0 ? NEUTRAL_SCALE - MIN_SCALE : MAX_SCALE - NEUTRAL_SCALE;
        return NEUTRAL_SCALE + EaseSignedMagnitude(delta, maxMagnitude);
    }

    static double EaseSignedMagnitude(double value, double maxMagnitude)
    {
        if (value == 0.0)
        {
            return 0.0;
        }

        var easedMagnitude = maxMagnitude * (1.0 - Math.Exp(-Math.Abs(value) / maxMagnitude));
        return Math.CopySign(easedMagnitude, value);
    }
}
