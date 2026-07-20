using Avalonia.Controls;

namespace UI_Revamp.CurvedHud;

public partial class CurvedHudWindow : Window
{
    public CurvedHudWindow()
    {
        InitializeComponent();

#if IS_DESIGN
        ShowDesignPreview();
#else
        if (Design.IsDesignMode)
            ShowDesignPreview();
#endif
    }

    void ShowDesignPreview()
    {
        PART_DesignPreview.IsVisible = true;
    }
}
