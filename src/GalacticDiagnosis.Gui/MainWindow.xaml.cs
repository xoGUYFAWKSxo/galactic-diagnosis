using System.Windows;
using GalacticDiagnosis.Gui.Services;

namespace GalacticDiagnosis.Gui;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);
    }
}
