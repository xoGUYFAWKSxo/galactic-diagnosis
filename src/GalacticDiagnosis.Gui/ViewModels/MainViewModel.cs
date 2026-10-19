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
        page = new NewCheckViewModel(this);
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

    /// <summary>A one-line confirmation (saved, copied) shown under the header until the next page change.</summary>
    [ObservableProperty]
    private string? notice;

    /// <summary>True while a check runs; navigation waits until it finishes or is cancelled.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NewCheckCommand), nameof(OpenReportCommand), nameof(HistoryCommand))]
    private bool isBusy;

    partial void OnIsDarkChanged(bool value)
    {
        ThemeManager.Apply(value);
        Settings.Theme = value ? "dark" : "light";
        Settings.Save();
    }

    partial void OnPageChanged(object value) => (Error, Notice) = (null, null);

    partial void OnErrorChanged(string? value)
    {
        if (value is not null) Notice = null;
    }

    partial void OnNoticeChanged(string? value)
    {
        if (value is not null) Error = null;
    }

    bool CanNavigate() => !IsBusy;

    [RelayCommand]
    private void ToggleTheme() => IsDark = !IsDark;

    [RelayCommand(CanExecute = nameof(CanNavigate))]
    private void NewCheck() => Page = new NewCheckViewModel(this);

    [RelayCommand(CanExecute = nameof(CanNavigate))]
    private void History() => Page = new HistoryViewModel(this);

    [RelayCommand(CanExecute = nameof(CanNavigate))]
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
        report.Main = this;
        Page = report;
        // No WebView2 on this PC: the browser is the viewer.
        if (!WebViewHost.Available) report.OpenInBrowserCommand.Execute(null);
    }

    public void StartCheck(Intake intake, SavedRun? before, NewCheckViewModel form)
    {
        var running = new RunningViewModel(this, intake, before, form);
        IsBusy = true;
        Page = running;
        _ = running.RunAsync();
    }
}
