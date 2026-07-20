using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Keen.Game2.Client.UI.InGame;
using Keen.Game2.Client.UI.Library.Dialogs.ThreeOptionsDialog;
using Keen.Game2.Client.UI.Library.Dialogs.TwoOptionsDialog;
using Keen.VRage.Core;
using Keen.VRage.Library.Diagnostics;
using Keen.VRage.Library.Localization;
using Keen.VRage.Library.Utils;

namespace UI_Revamp;

internal static class DarkModeStyleController
{
    static readonly Uri DarkModeStyleUri = new("avares://UI-Revamp/Styles/DarkMode/DarkMode.axaml");
    static StyleInclude? DarkModeStyle;
    static readonly Dictionary<string, object?> OriginalStyleRootResources = new();
    static readonly Dictionary<string, object?> OriginalApplicationResources = new();

    static readonly (string Name, object Value)[] PaletteOverrides =
    {
        ("Primary", C("#95B0B7")),
        ("Secondary", C("#627C83")),
        ("Highlight", C("#E08B0E")),
        ("HighContrast", C("#FFFFFF")),
        ("Disabled", C("#636363")),
        ("Passive", C("#093B49")),
        ("PassiveDark", C("#132122")),
        ("Background", C("#00202B")),
        ("BackgroundDark", C("#080E11")),
        ("Black", C("#000000")),
        ("Success", C("#2FA25A")),
        ("Warning", C("#FFDB1D")),
        ("Error", C("#D0252D")),
        ("BackgroundDarkAlpha95", C("#F2080E11")),
        ("HighlightAlpha95", C("#F2E08B0E")),

        ("Border-Background", B("#00202B")),
        ("Border-Brush", B("#627C83")),
        ("Border-Brush-Active", B("#FFFFFF")),
        ("Decorative-Border-Brush", B("#535353")),

        ("Button-Background", B("#00202B")),
        ("Button-Background-Focus", B("#00202B")),
        ("Button-Background-Disabled", B("#132122")),
        ("Button-Background-Active", B("#E08B0E")),
        ("Menu-Button-Background", B("#080E11")),
        ("Menu-Button-Background-Focus", B("#080E11")),
        ("Menu-Button-Background-Disabled", B("#080E11")),
        ("Menu-Button-Background-Active", B("#E08B0E")),
        ("Button-Stroke", B("#627C83")),
        ("Button-Stroke-Focus", B("#E08B0E")),
        ("Button-Stroke-Disabled", B("#00202B")),
        ("Button-Stroke-Active", B("#E08B0E")),
        ("Button-Text", B("#FFFFFF")),
        ("Button-Text-Focus", B("#FFFFFF")),
        ("Button-Text-Disabled", B("#627C83")),
        ("Button-Text-Active", B("#000000")),

        ("ListboxItem-Background", B("Transparent")),
        ("ListboxItem-Background-Focus", B("#093B49")),
        ("ListboxItem-Background-Disabled", B("Transparent")),
        ("ListboxItem-Background-Active", B("#E08B0E")),
        ("ListboxItem-Text", B("#FFFFFF")),
        ("ListboxItem-Text-Focus", B("#FFFFFF")),
        ("ListboxItem-Text-Disabled", B("#627C83")),
        ("ListboxItem-Text-Active", B("#000000")),

        ("Popup-Background", B("#093B49")),
        ("Popup-Border-Brush", B("#95B0B7")),

        ("PreciseControl-Background", B("#00202B")),
        ("PreciseControl-Background-Focus", B("#00202B")),
        ("PreciseControl-Background-Disabled", B("#132122")),
        ("PreciseControl-Background-Active", B("#E08B0E")),
        ("PreciseControl-Stroke", B("#627C83")),
        ("PreciseControl-Stroke-Focus", B("#E08B0E")),
        ("PreciseControl-Stroke-Disabled", B("#00202B")),
        ("PreciseControl-Stroke-Active", B("#627C83")),
        ("PreciseControl-Text", B("#FFFFFF")),
        ("PreciseControl-Text-Focus", B("#FFFFFF")),
        ("PreciseControl-Text-Disabled", B("#627C83")),
        ("PreciseControl-Text-Active", B("#000000")),

        ("Tab-Background", B("#080E11")),
        ("Tab-Background-Focus", B("#080E11")),
        ("Tab-Background-Disabled", B("#080E11")),
        ("Tab-Background-Active", B("#080E11")),
        ("Tab-Stroke", B("#627C83")),
        ("Tab-Stroke-Focus", B("#E08B0E")),
        ("Tab-Stroke-Disabled", B("#00202B")),
        ("Tab-Stroke-Active", B("#E08B0E")),
        ("Tab-Text", B("#95B0B7")),
        ("Tab-Text-Focus", B("#FFFFFF")),
        ("Tab-Text-Disabled", B("#627C83")),
        ("Tab-Text-Active", B("#95B0B7")),

        ("TextBox-Background", B("#00202B")),
        ("TextBox-Background-Focus", B("#00202B")),
        ("TextBox-Background-Disabled", B("#132122")),
        ("TextBox-Background-Active", B("#E08B0E")),
        ("TextBox-Stroke", B("#627C83")),
        ("TextBox-Stroke-Focus", B("#E08B0E")),
        ("TextBox-Stroke-Disabled", B("#00202B")),
        ("TextBox-Stroke-Active", B("#E08B0E")),
        ("TextBox-Text", B("#95B0B7")),
        ("TextBox-Text-Focus", B("#95B0B7")),
        ("TextBox-Text-Disabled", B("#627C83")),
        ("TextBox-Text-Active", B("#000000")),

        ("ToggleSwitch-Background", B("#00202B")),
        ("ToggleSwitch-Background-Focus", B("#E08B0E")),
        ("ToggleSwitch-Background-Disabled", B("#132122")),
        ("ToggleSwitch-Background-Active", B("#95B0B7")),
        ("ToggleSwitch-Stroke", B("#627C83")),
        ("ToggleSwitch-Stroke-Focus", B("#E08B0E")),
        ("ToggleSwitch-Stroke-Disabled", B("#00202B")),
        ("ToggleSwitch-Stroke-Active", B("#E08B0E")),
        ("ToggleSwitch-Text", B("#FFFFFF")),
        ("ToggleSwitch-Text-Focus", B("#000000")),
        ("ToggleSwitch-Text-Disabled", B("#627C83")),
        ("ToggleSwitch-Text-Active", B("#FFFFFF")),

        ("CheckBoxCheckGlyphForeground", B("#FFFFFF")),
        ("CheckBoxForegroundUnchecked", B("#E08B0E")),
        ("CheckBoxForegroundChecked", B("#E08B0E")),
        ("CheckBoxBackground", B("#00202B")),
        ("CheckBoxBorder", B("#627C83")),
        ("CheckBoxBorderHover", B("#E08B0E")),
        ("CheckBoxBorderPressed", B("#E08B0E")),
        ("TextBlockHyperlinkActive", B("#E08B0E"))
    };

    static Color C(string text) => Color.Parse(text);

    static SolidColorBrush B(string text) => new(C(text));

    public static void Reload()
    {
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                var styleRoot = GetStyleRoot();
                if (styleRoot == null)
                {
                    return;
                }

                if (DarkModeStyle != null)
                {
                    styleRoot.Styles.Remove(DarkModeStyle);
                    DarkModeStyle = null;
                }
                RestorePaletteResources(styleRoot);

                if (!Plugin.Settings.UseDarkMode)
                {
                    Log.Default.Info($"[{Plugin.PluginId}] Dark mode styles unloaded.");
                    InvalidateMainWindow();
                    return;
                }

                ApplyPaletteResources(styleRoot);

                DarkModeStyle = new StyleInclude(DarkModeStyleUri)
                {
                    Source = DarkModeStyleUri
                };
                styleRoot.Styles.Add(DarkModeStyle);

                Log.Default.Info($"[{Plugin.PluginId}] Dark mode styles reloaded.");
                InvalidateMainWindow();
            }
            catch (Exception e)
            {
                DarkModeStyle = null;
                RestorePaletteResources(GetStyleRoot());
                Log.Default.Error($"[{Plugin.PluginId}] Failed to reload dark mode styles: {e}");
            }
        });
    }

    static void ApplyPaletteResources(Control styleRoot)
    {
        foreach (var (name, value) in PaletteOverrides)
        {
            SetResource(styleRoot.Resources, OriginalStyleRootResources, name, value);

            var application = Application.Current;
            if (application != null)
            {
                SetResource(application.Resources, OriginalApplicationResources, name, value);
            }
        }
    }

    static void RestorePaletteResources(Control? styleRoot)
    {
        if (styleRoot != null)
        {
            RestoreResources(styleRoot.Resources, OriginalStyleRootResources);
        }

        var application = Application.Current;
        if (application != null)
        {
            RestoreResources(application.Resources, OriginalApplicationResources);
        }
    }

    static void SetResource(IResourceDictionary resources, Dictionary<string, object?> originals, string name, object value)
    {
        if (!originals.ContainsKey(name))
        {
            originals[name] = resources.TryGetResource(name, null, out var existing) ? existing : null;
        }

        resources[name] = value;
    }

    static void RestoreResources(IResourceDictionary resources, Dictionary<string, object?> originals)
    {
        foreach (var (name, value) in originals)
        {
            if (value == null)
            {
                resources.Remove(name);
            }
            else
            {
                resources[name] = value;
            }
        }

        originals.Clear();
    }

    public static void ShowRestartRequiredDialog()
    {
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                var sharedUi = Plugin.SharedUi;
                if (sharedUi == null)
                {
                    Log.Default.Info($"[{Plugin.PluginId}] Dark mode changed. Game restart required.");
                    return;
                }

                var definition = new TwoOptionsDialogDefinition();
                definition.Init(new TwoOptionsDialogDefinitionObjectBuilder
                {
                    Title = LocKey.FromString("Restart required"),
                    Content = LocKey.FromString("Dark mode changes require a game restart.\n\nDo you want to restart now?"),
                    ConfirmOption = LocKey.FromString("Restart"),
                    CancelOption = LocKey.FromString("Later"),
                    SelectedOption = TwoOptionsDialogSelectedOption.Cancel
                });

                sharedUi.ShowDialog(new TwoOptionsDialogViewModel(definition)
                {
                    ConfirmAction = RequestRestart
                });
            }
            catch (Exception e)
            {
                Log.Default.Error($"[{Plugin.PluginId}] Failed to show restart dialog: {e}");
            }
        });
    }

    static void RequestRestart()
    {
        var inGameUi = Plugin.InGameUi;
        if (inGameUi == null)
        {
            RestartGame();
            return;
        }

        ShowSaveBeforeRestartDialog(inGameUi);
    }

    static void ShowSaveBeforeRestartDialog(InGameUI inGameUi)
    {
        var sharedUi = Plugin.SharedUi;
        if (sharedUi == null)
        {
            Log.Default.Info($"[{Plugin.PluginId}] In-game restart requested, but SharedUI is unavailable. Restarting without save prompt.");
            RestartGame();
            return;
        }

        var definition = new ThreeOptionsDialogDefinition();
        definition.Init(new ThreeOptionsDialogDefinitionObjectBuilder
        {
            Title = LocKey.FromString("Save before restart?"),
            Content = LocKey.FromString("Do you want to save the current game before restarting?"),
            ConfirmOption = LocKey.FromString("Save"),
            DefaultOption = LocKey.FromString("Don't Save"),
            CancelOption = LocKey.FromString("Cancel"),
            SelectedOption = ThreeOptionsDialogSelectedOption.Cancel
        });

        sharedUi.ShowDialog(new ThreeOptionsDialogViewModel(definition)
        {
            ConfirmAction = () => _ = inGameUi.SaveAndExecute(RestartGame),
            DefaultAction = RestartGame
        });
    }

    static void RestartGame()
    {
        try
        {
            var executablePath = Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrWhiteSpace(executablePath))
            {
                Log.Default.Error($"[{Plugin.PluginId}] Failed to restart game: current executable path is unavailable.");
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = executablePath,
                UseShellExecute = true
            });

            Singleton<VRageCore>.Instance.Exit();
        }
        catch (Exception e)
        {
            Log.Default.Error($"[{Plugin.PluginId}] Failed to restart game: {e}");
        }
    }

    static Control? GetStyleRoot()
    {
        var mainWindow = Plugin.MainWindow;
        if (mainWindow == null)
        {
            return null;
        }

        return mainWindow
            .GetType()
            .GetField("StyleRoot", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?.GetValue(mainWindow) as Control;
    }

    static void InvalidateMainWindow()
    {
        var mainWindow = Plugin.MainWindow;
        if (mainWindow == null)
        {
            return;
        }

        mainWindow.InvalidateMeasure();
        mainWindow.InvalidateVisual();

        foreach (var visual in mainWindow.GetVisualDescendants())
        {
            if (visual is Control control)
            {
                control.InvalidateMeasure();
                control.InvalidateVisual();
            }
        }
    }
}
