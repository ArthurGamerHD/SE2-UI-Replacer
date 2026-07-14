using System;
using System.Windows.Input;
using Avalonia.Controls;
using HarmonyLib;
using Keen.Game2.Client.UI.Menu;
using Keen.VRage.Library.Localization;
using Keen.VRage.Library.Utils;
using Keen.VRage.UI.Shared.Extensions;
using Keen.VRage.UI.Shared.Helpers;

namespace PreviewHelper.PreviewPatches;

[HarmonyPatch(typeof(GameMenu))]
[HarmonyPatch("UpdateButtons")]
public class GameMenuPatches
{
    public static bool Prefix(GameMenu __instance)
    {
        var buttonsPanel = __instance.FindChildOfType<StackPanel>("PART_ButtonsPanel") as StackPanel;
        if(buttonsPanel == null)
            return false;
        
        buttonsPanel.Children.Clear();

        var emptyAction = new Action(() => { });
        
        Controls children1 = buttonsPanel.Children;
        Separator separator1 = new Separator();
        separator1.Height = 20.0;
        children1.Add(separator1);
        buttonsPanel.Children.Add(CreateButton("New Game",emptyAction));
        buttonsPanel.Children.Add(CreateButton("Load ", emptyAction));
        buttonsPanel.Children.Add(CreateButton("Multiplayer"));
        buttonsPanel.Children.Add(CreateButton("Workshop", emptyAction));
        buttonsPanel.Children.Add(CreateButton("Character"));
        buttonsPanel.Children.Add(CreateButton("Settings", emptyAction));
        Controls children2 = buttonsPanel.Children;
        Separator separator2 = new Separator();
        separator2.Height = 10.0;
        children2.Add(separator2);
        buttonsPanel.Children.Add(CreateButton("Quit", emptyAction));
        
        return false;

        Button CreateButton(string name, Action? action = null)
        {
            SimpleCommand.DelegateCommand? saveGameCommand = action != null ? SimpleCommand.Create(action) : null;
            Button button = new Button();
            button.Classes.Add("Menu");
            button.Content = (LocKey.FromString(name));
            button.Command = saveGameCommand;
            return button;
        }
    }
}