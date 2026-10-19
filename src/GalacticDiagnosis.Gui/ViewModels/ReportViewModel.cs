using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GalacticDiagnosis.Core;
using GalacticDiagnosis.Gui.Services;
using GalacticDiagnosis.Report;

namespace GalacticDiagnosis.Gui.ViewModels;

/// <summary>One page shown in the report viewer. FilePath is the saved HTML, when there is one.</summary>
public sealed record ReportTab(string Header, string Html, string? FilePath)
{
    // Screen readers read a list item by its ToString.
    public override string ToString() => Header;
}

public sealed partial class ReportViewModel : ObservableObject
{
    public ReportViewModel(string title, IEnumerable<ReportTab> tabs, SavedRun? run)
    {
        Title = title;
        Run = run;
        foreach (var t in tabs) Tabs.Add(t);
        // An after run with a comparison opens on the comparison: it's the page the technician wants first.
        selected = Tabs.LastOrDefault();
    }

    public string Title { get; }
    public SavedRun? Run { get; }
    public ObservableCollection<ReportTab> Tabs { get; } = [];
    public bool WebViewMissing => !WebViewHost.Available;

    [ObservableProperty]
    private ReportTab? selected;

    [RelayCommand]
    private void OpenInBrowser()
    {
        if (Selected is { } tab) Shell.Open(FileFor(tab));
    }

    [RelayCommand]
    private void ShowInFolder()
    {
        if (Selected?.FilePath is { } p && File.Exists(p)) Shell.ShowInFolder(p);
        else if (Run is not null) Shell.ShowInFolder(Run.JsonPath);
    }

    /// <summary>The saved HTML if it exists, otherwise a copy in %TEMP% so a browser can open it.</summary>
    static string FileFor(ReportTab tab)
    {
        if (tab.FilePath is { } p && File.Exists(p)) return p;
        var temp = Path.Combine(Path.GetTempPath(), $"GalacticDiagnosis-{Guid.NewGuid():N}.html");
        File.WriteAllText(temp, tab.Html);
        return temp;
    }

    /// <summary>Opens a saved run (.json) or report (.html). A run is re-rendered so older runs get the current
    /// layout; its saved comparison page, if any, opens alongside it.</summary>
    public static ReportViewModel FromFile(string path)
    {
        path = Path.GetFullPath(path);
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext is ".html" or ".htm")
        {
            var json = Path.ChangeExtension(path, ".json");
            var afterJson = Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileName(path).Replace("_comparison", "_after"));
            afterJson = Path.ChangeExtension(afterJson, ".json");
            if (File.Exists(json)) return FromRun(new SavedRun(json, RunFile.Load(json)));
            if (path.Contains("_comparison") && File.Exists(afterJson)) return FromRun(new SavedRun(afterJson, RunFile.Load(afterJson)));
            var header = path.Contains("_comparison") ? "Before vs after" : "Report";
            return new ReportViewModel(Path.GetFileNameWithoutExtension(path), [new(header, File.ReadAllText(path), path)], null);
        }
        return FromRun(new SavedRun(path, RunFile.Load(path)));
    }

    public static ReportViewModel FromRun(SavedRun saved, string? comparisonPath = null)
    {
        var tabs = new List<ReportTab> { new("Report", HtmlReport.Render(saved.Run), saved.HtmlPath) };
        comparisonPath ??= ReportStore.ComparisonPath(saved.JsonPath);
        if (saved.Run.Intake.RunType == "after" && File.Exists(comparisonPath))
            tabs.Add(new("Before vs after", File.ReadAllText(comparisonPath), comparisonPath));
        return new ReportViewModel(TitleFor(saved.Run), tabs, saved);
    }

    public static string TitleFor(DiagnosticRun run)
    {
        var who = string.IsNullOrWhiteSpace(run.Intake.CustomerName) ? "Unnamed customer" : run.Intake.CustomerName;
        var type = run.Intake.RunType == "after" ? "After service" : "Before service";
        return $"{who}  ·  {type}  ·  {run.StartedAt:MMM d, yyyy h:mm tt}";
    }
}
