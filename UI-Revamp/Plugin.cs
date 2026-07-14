using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using HarmonyLib;
using Keen.Game2.Client.UI.Library;
using Keen.VRage.Core;
using Keen.VRage.Core.Plugins;
using Keen.VRage.Library.Diagnostics;
using Keen.VRage.Library.Utils;
using Keen.VRage.UI.EngineComponents;
using UI_Revamp.Patches;

namespace SE2PluginLoader;

public class Plugin : IPlugin
{
    public const string PluginId = "SE2-UI-Revamp";
    private Harmony _harmony = new(PluginId);

    private string _optionsDirectory = Path.Combine(Singleton<VRageCore>.Instance.AppDataPath, "PluginsOptions\\Settings");
    string PluginSettings => Path.Combine(_optionsDirectory, "UI-Revamp.json");

    public Plugin(PluginHost host)
    {
        Log.Default.Info($"[{PluginId}] Initializing.");

        try
        {
            _harmony.PatchAll(Assembly.GetExecutingAssembly());
        }
        catch (Exception e)
        {
            Log.Default.Error($"[{PluginId}] Fail to initialize: {e}");
        }
        
        try
        {
            Log.Default.Info($"[{PluginId}] Loading Settings");
            if (!Directory.Exists(_optionsDirectory)) 
                Directory.CreateDirectory(_optionsDirectory);

            if (!File.Exists(PluginSettings)) 
                File.Create(PluginSettings);
            else
            {
                try
                {
                    JsonSerializer.Deserialize<Dictionary<string, bool>>(File.ReadAllText(PluginSettings));
                }
                catch (Exception e)
                {
                    Log.Default.Error($"[{PluginId}] Fail to load settings\n" + e.Message);
                }
            }
        }
        catch (Exception e)
        {
            Log.Default.Error(e.Message);
            throw;
        }

        Log.Default.Info($"[{PluginId}] Initialized");
    }

    public static SharedUIComponent? SharedUi { get; internal set; }
    public static UIEngineComponent? UiEngineComponent { get; internal set; }
}
