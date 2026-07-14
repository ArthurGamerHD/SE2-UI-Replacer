using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using System;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.Styling;
using PreviewHelper.Views;

namespace PreviewHelper;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        LoadGameStyles();
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Avoid duplicate validations from both Avalonia and the CommunityToolkit. 
            // More info: https://docs.avaloniaui.net/docs/guides/development-guides/data-validation#manage-validationplugins
            DisableAvaloniaDataAnnotationValidation();
            desktop.MainWindow = new  Views.PreviewHelper();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void DisableAvaloniaDataAnnotationValidation()
    {
        // Get an array of plugins to remove
        var dataValidationPluginsToRemove =
            BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();

        // remove each entry found
        foreach (var plugin in dataValidationPluginsToRemove)
        {
            BindingPlugins.DataValidators.Remove(plugin);
        }
    }

    private void LoadGameStyles()
    {
        if (Design.IsDesignMode || !RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return;

        var baseUri = new Uri("avares://PreviewHelper/App.axaml");
        var styleUris = new[]
        {
            "avares://Game2.Client/UI/Library/Styles/SharedStyles.axaml",
            "avares://Game2.Client/UI/Library/Controls/LabelledControls/LabelledControlStyles.axaml",
            "avares://Game2.Client/UI/Menu/Styles/MainMenuStyles.axaml",
            "avares://Game2.Client/UI/Menu/LoadMenu/LoadMenuStyles.axaml",
            "avares://Game2.Client/UI/TerminalScreen/ControlPanel/Styles/ControlPanelStyles.axaml",
            "avares://Game2.Client/UI/TerminalScreen/GScreen/Styles/GScreenStyles.axaml",
            "avares://Game2.Client/UI/HUD/Styles/HUDStyles.axaml",
            "avares://Game2.Client/UI/HUD/Toolbar/Styles/ToolbarStyles.axaml",
        };

        foreach (var styleUri in styleUris)
            Styles.Add(new StyleInclude(baseUri) { Source = new Uri(styleUri) });
    }
}
