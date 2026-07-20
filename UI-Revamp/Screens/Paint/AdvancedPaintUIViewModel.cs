using System.Collections.ObjectModel;
using System.ComponentModel;
using Keen.Game2.Client.WorldObjects.Tools;
using Keen.Game2.Simulation.WorldObjects.Tools;
using Keen.VRage.Core.Game.Components;
using Keen.VRage.Library.Definitions;
using Keen.VRage.Library.Mathematics;
using Keen.VRage.Library.Utils;
using Keen.VRage.UI.Screens;

namespace UI_Revamp.Screens.Paint;

public sealed class AdvancedPaintUIViewModel : ScreenViewModel
{
    readonly PaintToolControllableComponent _paintToolControllable;

    public AdvancedPaintUIViewModel(PaintToolControllableComponent paintToolControllable)
    {
        _paintToolControllable = paintToolControllable;
        _paintToolControllable.PaintData.PropertyChanged += OnPaintDataPropertyChanged;
        InitializeInputContext();
    }

    public ColorHSV Color
    {
        get => _paintToolControllable.PaintData.Color;
        set => PaintToolPerPlayerData.SetPaintColor(_paintToolControllable.Entity.GetSession(), value).SkipWait();
    }

    public ObservableCollection<ColorHSV> Palette => _paintToolControllable.PaintData.Palette;

    public int SelectedIndex
    {
        get => _paintToolControllable.PaintData.PaletteIndex;
        set
        {
            if (value < 0 || value >= _paintToolControllable.PaintData.Palette.Count)
            {
                return;
            }

            _paintToolControllable.PaintData.PaletteIndex = value;
            Color = _paintToolControllable.PaintData.Palette[value];
        }
    }

    public void ResetIndex(int index)
    {
        if (index < 0 || index >= _paintToolControllable.PaintData.Palette.Count)
        {
            return;
        }

        _paintToolControllable.PaintData.Palette[index] = DefinitionManager.Instance
            .GetConfiguration<PaintToolConfiguration>().DefaultColors[index];
    }

    public void ResetPalette()
    {
        var defaultPalette = new PaintToolPerPlayerData().Palette;
        for (var i = 0; i < defaultPalette.Count && i < _paintToolControllable.PaintData.Palette.Count; i++)
        {
            _paintToolControllable.PaintData.Palette[i] = defaultPalette[i];
        }
    }

    protected override void OnDispose()
    {
        _paintToolControllable.PaintData.PropertyChanged -= OnPaintDataPropertyChanged;
        base.OnDispose();
    }

    void OnPaintDataPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        InvokePropertyChanged(nameof(Palette));
        InvokePropertyChanged(nameof(Color));
        InvokePropertyChanged(nameof(SelectedIndex));
    }
}
