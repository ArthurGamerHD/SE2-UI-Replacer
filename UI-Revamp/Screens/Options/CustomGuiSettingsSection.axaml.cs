using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Keen.VRage.UI.AvaloniaInterface.Services;

namespace UI_Revamp.Screens.Options;

[NeedsWindowStyles]
public partial class CustomGuiSettingsSection : UserControl
{
    public CustomGuiSettingsSection()
    {
        InitializeComponent();
        DataContext = Plugin.Settings;

        PART_UIScale.AddHandler(
            InputElement.PointerReleasedEvent,
            (_, _) => ApplyUiScale(),
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble,
            handledEventsToo: true);
        PART_UIScale.ValueChangedByButton += ApplyUiScale;
    }

    private static void ApplyUiScale()
    {
        Plugin.ApplyUiScale(Plugin.Settings.UiScale);
    }
}
