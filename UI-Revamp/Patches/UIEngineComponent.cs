using HarmonyLib;
using Keen.Game2.Client.GameSystems.PlayerControl;
using Keen.VRage.Core.Input;
using Keen.VRage.Input;
using Keen.VRage.Library.Diagnostics;
using Keen.VRage.Library.Utils;
using Keen.VRage.UI.EngineComponents;
using System;
using System.Collections.Generic;

namespace UI_Revamp.Patches;

[HarmonyPatch(typeof(UIEngineComponent))]
[HarmonyPatch("PostInit")]
public class UiEngineComponentPatches  
{
    [HarmonyPostfix]
    // ReSharper disable once InconsistentNaming
    public static void Postfix(UIEngineComponent __instance)
    {
        Plugin.UiEngineComponent = __instance;
        Plugin.UpdateHudResources();
#if DEBUG
        DumpHotkeyInputPatch.Install();
#endif
        Log.Default.Info($"[{Plugin.PluginId}] UIEngineComponent captured");
    }
}

[HarmonyPatch(typeof(UIEngineComponent))]
[HarmonyPatch("UIManagerTick")]
public class HudWobbleTickPatch
{
    [HarmonyPostfix]
    public static void Postfix()
    {
        HudWobbleController.UiFrame();
    }
}

[HarmonyPatch(typeof(ClientPlayersSessionComponent), nameof(ClientPlayersSessionComponent.SetupInitialPlayer))]
public class ClientPlayersSetupInitialPlayerPatch
{
    [HarmonyPostfix]
    // ReSharper disable once InconsistentNaming
    public static void Postfix(ClientPlayersSessionComponent __instance)
    {
        Plugin.ClientPlayers = __instance;
    }
}

[HarmonyPatch(typeof(ClientPlayersSessionComponent), nameof(ClientPlayersSessionComponent.SetLocalPlayer))]
public class ClientPlayersSetLocalPlayerPatch
{
    [HarmonyPostfix]
    // ReSharper disable once InconsistentNaming
    public static void Postfix(ClientPlayersSessionComponent __instance)
    {
        Plugin.ClientPlayers = __instance;
    }
}

#if DEBUG
[HarmonyPatch(typeof(UIEngineComponent))]
[HarmonyPatch("UIManagerTick")]
public class NativeDevToolsMessagePumpPatch
{
    [HarmonyPostfix]
    public static void Postfix()
    {
        NativeDevToolsWindowContext.PumpWin32Messages();
    }
}

public static class DumpHotkeyInputPatch
{
    static readonly HashSet<InputId> ActiveInputs = new();
    static readonly HashSet<InputId> ChangedInputs = new();
    static bool _installed;
    static bool _loggedInputFailure;

    public static void Install()
    {
        if (_installed)
        {
            return;
        }

        try
        {
            Singleton<InputDeviceManager>.Instance.OnBeforeProcessInput += ProcessInput;
            _installed = true;
            Log.Default.Info($"[{Plugin.PluginId}] Visual tree dump hotkey registered through VRage input.");
        }
        catch (Exception e)
        {
            Log.Default.Error($"[{Plugin.PluginId}] Failed to register visual tree dump hotkey through VRage input: {e}");
        }
    }

    static void ProcessInput(InputDeviceManager deviceManager)
    {
        if (WasDumpHotkeyPressed(deviceManager))
        {
            Log.Default.Info($"[{Plugin.PluginId}] Visual tree dump hotkey detected through VRage input.");
            VisualTreeDump.WriteCurrent();
        }
    }

    static bool WasDumpHotkeyPressed(InputDeviceManager deviceManager)
    {
        try
        {
            var keyboard = deviceManager.Keyboard;
            if (keyboard == null)
            {
                return false;
            }

            ActiveInputs.Clear();
            ChangedInputs.Clear();
            keyboard.FillActive(ActiveInputs);
            keyboard.FillChanged(ChangedInputs);

            var isPressed = keyboard.GetDigitalState(KeyboardInputs.F10);
            var changed = ChangedInputs.Contains(KeyboardInputs.F10);

            return isPressed && changed;
        }
        catch (Exception e)
        {
            // Input singletons can be unavailable during startup/shutdown ticks.
            if (!_loggedInputFailure)
            {
                _loggedInputFailure = true;
                Log.Default.Error($"[{Plugin.PluginId}] Failed to read visual tree dump hotkey from VRage input: {e}");
            }

            return false;
        }
    }
}
#endif
