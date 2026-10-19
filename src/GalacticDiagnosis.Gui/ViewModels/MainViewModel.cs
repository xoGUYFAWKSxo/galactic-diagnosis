using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GalacticDiagnosis.Core;
using GalacticDiagnosis.Gui.Services;
using GalacticDiagnosis.Report;
using Microsoft.Win32;

namespace GalacticDiagnosis.Gui.ViewModels;

/// <summary>The window: header state, theme, and which page is showing.</summary>
public sealed partial class MainViewModel : ObservableObject
{
    public MainViewModel(AppSettings settings)
    {
        Settings = settings;
        isDark = ThemeManager.IsDark;
        page = new HomeViewModel(this);
    }

    public AppSettings Settings { get; }
    public ReportStore Store { get; } = ReportStore.Default;
    public string MachineName { get; } = Environment.MachineName;
    public bool IsAdmin { get; } = CollectorRunner.IsAdmin();
    public string VersionText { get; } = $"v{RunSession.ToolVersion}";

    [ObservableProperty]
    private object page;

    [ObservableProperty]
    private bool isDark;

    /// <summary>A one-line problem shown under the header until the next page change.</summary>
    [ObservableProperty]
    private string? error;

    partial void OnIsDarkChanged(bool value)
    {
        ThemeManager.Apply(value);
        Settings.Theme = value ? "dark" : "light";
        Settings.Save();
    }

    partial void OnPageChanged(object value) => Error = null;

    [RelayCommand]
    private void ToggleTheme() => IsDark = !IsDark;

    [RelayCommand]
    private void GoHome() => Page = new HomeViewModel(this);

    [RelayCommand]
    private void OpenReport()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Open a saved report",
            Filter = "Galactic Diagnosis reports (*.json;*.html)|*.json;*.html",
            InitialDirectory = Directory.Exists(Store.Folder) ? Store.Folder : AppContext.BaseDirectory,
        };
        if (dlg.ShowDialog() == true) Open(dlg.FileName);
    }

    public void Open(string path)
    {
        try
        {
            Show(ReportViewModel.FromFile(path));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        {
            Error = $"Couldn't open {Path.GetFileName(path)}: {ex.Message}";
        }
    }

    public void Show(ReportViewModel report)
    {
        Page = report;
        // No WebView2 on this PC: the browser is the viewer.
        if (!WebViewHost.Available) report.OpenInBrowserCommand.Execute(null);
    }
}
