using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using GalacticDiagnosis.Gui.Services;
using GalacticDiagnosis.Gui.ViewModels;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;

namespace GalacticDiagnosis.Gui.Views;

public partial class ReportView : UserControl
{
    // NavigateToString's documented limit is 2 MB; real reports are tens of KB.
    const int MaxInlineHtml = 1_500_000;

    ReportViewModel? _vm;

    public ReportView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) => Attach(e.NewValue as ReportViewModel);
        Loaded += async (_, _) => await ShowSelectedAsync();
        Unloaded += (_, _) => Attach(null);
    }

    MainViewModel? Main => _vm?.Main;

    void Attach(ReportViewModel? vm)
    {
        if (_vm is not null) _vm.PropertyChanged -= OnVmChanged;
        _vm = vm;
        if (_vm is not null) _vm.PropertyChanged += OnVmChanged;
    }

    async void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ReportViewModel.Selected)) await ShowSelectedAsync();
    }

    async Task ShowSelectedAsync()
    {
        if (_vm?.Selected is not { } tab || !IsLoaded || !WebViewHost.Available) return;
        try
        {
            await WebViewHost.InitAsync(Web);
            if (tab.Html.Length <= MaxInlineHtml) Web.CoreWebView2.NavigateToString(tab.Html);
            else _vm.OpenInBrowserCommand.Execute(null);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException
                                       or WebView2RuntimeNotFoundException)
        {
            // The runtime is there but won't start (damaged install, policy): fall back to the browser.
            if (Main is not null) Main.Error = $"The report viewer couldn't start ({ex.Message}). Opening the report in the web browser instead.";
            _vm.OpenInBrowserCommand.Execute(null);
        }
    }

    async void SavePdf_Click(object sender, RoutedEventArgs e)
    {
        if (_vm?.Selected is not { } tab) return;
        var (folder, name) = _vm.PdfTarget(tab);
        var dlg = new SaveFileDialog
        {
            Title = "Save as PDF",
            Filter = "PDF (*.pdf)|*.pdf",
            FileName = name,
            InitialDirectory = Directory.Exists(folder) ? folder : AppContext.BaseDirectory,
        };
        if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;
        var ok = await PrintAsync([(tab, dlg.FileName)]);
        if (Main is not null && ok == 1) Main.Notice = $"Saved {Path.GetFileName(dlg.FileName)}.";
    }

    /// <summary>Saves the customer's pages as PDFs on the Desktop of the account running the tool.</summary>
    async void LeaveCopy_Click(object sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var jobs = _vm.CustomerCopies().Select(c => (c.Tab, Unique(Path.Combine(desktop, c.FileName)))).ToList();
        var saved = await PrintAsync(jobs);
        if (Main is null) return;
        if (saved == jobs.Count) Main.Notice = $"Left on the Desktop: {string.Join(" and ", jobs.Select(j => Path.GetFileName(j.Item2)))}.";
        else if (Main.Error is null) Main.Error = "Couldn't save the PDFs to the Desktop.";
    }

    static string Unique(string path)
    {
        var stem = Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path));
        var candidate = path;
        for (var n = 2; File.Exists(candidate); n++) candidate = $"{stem} ({n}).pdf";
        return candidate;
    }

    /// <summary>Prints each page to its PDF through the report view, then puts the selected page back.
    /// Returns how many PDFs were written.</summary>
    async Task<int> PrintAsync(IReadOnlyList<(ReportTab Tab, string Path)> jobs)
    {
        var count = 0;
        try
        {
            await WebViewHost.InitAsync(Web);
            var core = Web.CoreWebView2;
            var settings = core.Environment.CreatePrintSettings();
            settings.ShouldPrintBackgrounds = true;
            settings.ShouldPrintHeaderAndFooter = false;
            settings.MarginTop = settings.MarginBottom = 0.5;
            settings.MarginLeft = settings.MarginRight = 0.5;
            // A4 where the region is metric, US Letter elsewhere.
            (settings.PageWidth, settings.PageHeight) = RegionInfo.CurrentRegion.IsMetric ? (8.27, 11.69) : (8.5, 11.0);

            foreach (var (tab, path) in jobs)
            {
                await NavigateAsync(core, tab.Html);
                if (await core.PrintToPdfAsync(path, settings)) count++;
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException
                                       or IOException or UnauthorizedAccessException)
        {
            if (Main is not null) Main.Error = $"Couldn't save the PDF: {ex.Message}";
        }
        await ShowSelectedAsync();
        return count;
    }

    static Task NavigateAsync(CoreWebView2 core, string html)
    {
        var done = new TaskCompletionSource();
        void OnDone(object? s, CoreWebView2NavigationCompletedEventArgs e)
        {
            core.NavigationCompleted -= OnDone;
            done.TrySetResult();
        }
        core.NavigationCompleted += OnDone;
        core.NavigateToString(html);
        return done.Task;
    }
}
