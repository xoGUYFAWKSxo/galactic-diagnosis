using System.Windows;
using GalacticDiagnosis.Gui.Services;
using GalacticDiagnosis.Gui.ViewModels;

namespace GalacticDiagnosis.Gui;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var settings = AppSettings.Load();
        // Hidden test switch: behave as if the WebView2 runtime were missing.
        WebViewHost.ForceUnavailable = e.Args.Contains("--no-webview2");
        ThemeManager.Apply(settings.Theme switch { "dark" => true, "light" => false, _ => ThemeManager.WindowsPrefersDark() });

        var vm = new MainViewModel(settings);
        var window = new MainWindow { DataContext = vm };
        settings.RestoreWindow(window);
        window.Show();

        var file = e.Args.FirstOrDefault(a => !a.StartsWith("--"));
        if (file is not null) vm.Open(file);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        WebViewHost.TryDeleteDataFolder();
        base.OnExit(e);
    }
}
