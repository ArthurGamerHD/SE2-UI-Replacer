using HarmonyLib;
using Keen.Game2.Client.UI.Menu.News;
using Keen.VRage.Library.Definitions;

namespace PreviewHelper.PreviewPatches;

public class ManualPatches
{
    public static void ApplyPatch(Harmony harmony)
    {
        DefinitionManagerPatches.Apply(harmony);
    }
}