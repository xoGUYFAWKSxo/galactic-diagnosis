using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GalacticDiagnosis.Core;
using GalacticDiagnosis.Gui.Services;
using GalacticDiagnosis.Report;

namespace GalacticDiagnosis.Gui.ViewModels;

public enum TabKind { Report, Summary, Comparison }

/// <summary>One page shown in the report viewer. FilePath is the saved HTML, when there is one.</summary>
public sealed record ReportTab(TabKind Kind, string Header, string Html, string? FilePath)
{
    // Screen readers read a list item by its ToString.
    public override string ToString() => Header;
}

/// <summary>The report viewer: a saved run (report, customer summary, comparison) or an ad-hoc comparison of two runs.</summary>
public sealed partial class ReportViewModel : ObservableObject
{
    ReportViewModel(string title, SavedRun? run, SavedRun? before)
    {
        this.title = title;
        Run = run;
        Before = before;
    }

    public MainViewModel? Main { get; set; }
    public SavedRun? Run { get; }
    public SavedRun? Before { get; private set; }
    public ObservableCollection<ReportTab> Tabs { get; } = [];
    public bool WebViewMissing => !WebViewHost.Available;
    public bool CanPdf => !WebViewMissing;
    public bool CanEditNotes => Run is not null;
    public bool CanLeaveCopy => CanPdf && Tabs.Any(t => t.Kind == TabKind.Summary);
    public bool IsAfterRun => Run?.Run.Intake.RunType == "after";

    [ObservableProperty]
    private string title;

    [ObservableProperty]
    private ReportTab? selected;

    ComparisonResult? _comparison;

    // ---- Fix it ----

    /// <summary>Sections that need attention, with shortcuts. Only for a run made on this PC: the tools act on this PC.</summary>
    public ObservableCollection<FixItemViewModel> FixItems { get; } = [];
    public bool HasFixItems => FixItems.Count > 0;

    [ObservableProperty]
    private bool showFixPanel;

    [RelayCommand]
    private void ToggleFixPanel() => ShowFixPanel = !ShowFixPanel;

    // ---- Copy summary ----

    public bool CanCopySummary => Run is not null || _comparison is not null;

    [RelayCommand]
    private void CopySummary()
    {
        var run = Run?.Run ?? _comparison?.After;
        if (run is null) return;
        try
        {
            System.Windows.Clipboard.SetText(SummaryText.Render(run, _comparison));
            if (Main is not null) Main.Notice = "Summary copied. Paste it into an invoice, ticket or message.";
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            if (Main is not null) Main.Error = "Couldn't copy to the clipboard. Another program may be using it; try again.";
        }
    }

    // ---- Edit notes ----

    [ObservableProperty]
    private bool isEditing;

    [ObservableProperty]
    private string editComplaint = "";

    [ObservableProperty]
    private string editNotes = "";

    public ChecklistViewModel EditChecklist { get; } = new();

    [RelayCommand]
    private void StartEdit()
    {
        if (Run is null) return;
        var intake = Run.Run.Intake;
        EditComplaint = intake.Complaint;
        EditNotes = intake.WorkDone;
        foreach (var e in EditChecklist.Entries) e.IsChecked = false;
        EditChecklist.Load(intake.WorkItems);
        EditChecklist.Suggest(Before?.Run);
        IsEditing = true;
    }

    [RelayCommand]
    private void CancelEdit() => IsEditing = false;

    /// <summary>Rewrites the run's JSON and HTML (and its comparison) with the edited notes.</summary>
    [RelayCommand]
    private void SaveNotes()
    {
        if (Run is null) return;
        var intake = Run.Run.Intake;
        if (IsAfterRun)
        {
            intake.WorkItems = EditChecklist.Ticked;
            intake.WorkDone = EditNotes.Trim();
        }
        else intake.Complaint = EditComplaint.Trim();

        try
        {
            ReportStore.Write(Run.Run, Run.JsonPath);
            if (IsAfterRun && Before is not null) ReportStore.SaveComparison(Before.Run, Run.Run, Run.JsonPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (Main is not null) Main.Error = $"Couldn't save the notes: {ex.Message}";
            return;
        }
        var keep = Selected?.Kind;
        BuildRunTabs();
        Selected = Tabs.FirstOrDefault(t => t.Kind == keep) ?? Tabs.FirstOrDefault();
        IsEditing = false;
        if (Main is not null) Main.Notice = "Notes saved. The report files were updated.";
    }

    // ---- Toolbar ----

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

    /// <summary>Where a PDF of this tab goes by default: next to the run, named after it.</summary>
    public (string Folder, string FileName) PdfTarget(ReportTab tab)
    {
        var folder = Run is not null ? Path.GetDirectoryName(Run.JsonPath)! : ReportStore.Default.Folder;
        var stem = Run is not null ? Path.GetFileNameWithoutExtension(Run.JsonPath) : "Comparison";
        var name = tab.Kind switch
        {
            TabKind.Summary => stem + "_summary",
            TabKind.Comparison => tab.FilePath is { } f ? Path.GetFileNameWithoutExtension(f) : stem.Replace("_after", "") + "_comparison",
            _ => stem,
        };
        return (folder, name + ".pdf");
    }

    /// <summary>Customer handout: the summary, plus the before/after page when there is one, named for a customer's Desktop.</summary>
    public IReadOnlyList<(ReportTab Tab, string FileName)> CustomerCopies()
    {
        var run = Run?.Run;
        var who = run is null || run.Intake.CustomerName.Length == 0 ? "" : ReportStore.FileSafe(run.Intake.CustomerName) + " - ";
        var date = (run?.StartedAt ?? DateTimeOffset.Now).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var list = new List<(ReportTab, string)>();
        if (Tabs.FirstOrDefault(t => t.Kind == TabKind.Summary) is { } s) list.Add((s, $"{who}PC check summary {date}.pdf"));
        if (Tabs.FirstOrDefault(t => t.Kind == TabKind.Comparison) is { } c) list.Add((c, $"{who}PC before and after {date}.pdf"));
        return list;
    }

    // ---- Building ----

    void BuildRunTabs()
    {
        Tabs.Clear();
        var saved = Run!;
        var run = saved.Run;
        Tabs.Add(new(TabKind.Report, "Report", HtmlReport.Render(run), saved.HtmlPath));

        Before ??= LoadComparedWith(saved);
        var comparison = _comparison = run.Intake.RunType == "after" && Before is not null ? Comparison.Build(Before.Run, run) : null;
        Tabs.Add(new(TabKind.Summary, "Customer summary", SummaryReport.Render(run, comparison), null));

        var cmpPath = ReportStore.ComparisonPath(saved.JsonPath);
        if (comparison is not null)
            Tabs.Add(new(TabKind.Comparison, "Before vs after", ComparisonReport.Render(comparison), File.Exists(cmpPath) ? cmpPath : null));
        else if (run.Intake.RunType == "after" && File.Exists(cmpPath))
            Tabs.Add(new(TabKind.Comparison, "Before vs after", File.ReadAllText(cmpPath), cmpPath)); // older runs without compared_with
        Title = TitleFor(run);

        FixItems.Clear();
        if (run.MachineName.Equals(AppInfo.MachineName, StringComparison.OrdinalIgnoreCase))
            foreach (var s in run.Sections.Where(s => !s.NotApplicable && s.Verdict is Verdict.Warning or Verdict.Bad).OrderByDescending(s => s.Verdict))
                FixItems.Add(new FixItemViewModel(s));
        ShowFixPanel = FixItems.Count > 0;
        OnPropertyChanged(nameof(HasFixItems));
        OnPropertyChanged(nameof(CanLeaveCopy));
    }

    /// <summary>The before run an after run was compared with, if its file is still next to it.</summary>
    static SavedRun? LoadComparedWith(SavedRun saved)
    {
        if (saved.Run.ComparedWith is not { Length: > 0 } name) return null;
        var path = Path.Combine(Path.GetDirectoryName(saved.JsonPath)!, name);
        try { return File.Exists(path) ? new SavedRun(path, RunFile.Load(path)) : null; }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) { return null; }
    }

    public static ReportViewModel FromRun(SavedRun saved, SavedRun? before = null)
    {
        var vm = new ReportViewModel("", saved, before);
        vm.BuildRunTabs();
        // An after run with a comparison opens on the comparison: it's the page the technician wants first.
        vm.Selected = vm.Tabs.FirstOrDefault(t => t.Kind == TabKind.Comparison) ?? vm.Tabs[0];
        return vm;
    }

    /// <summary>Compares any two saved runs: the older one counts as "before".</summary>
    public static ReportViewModel Compare(SavedRun a, SavedRun b)
    {
        var (older, newer) = a.Run.StartedAt <= b.Run.StartedAt ? (a, b) : (b, a);
        var comparison = Comparison.Build(older.Run, newer.Run);
        var vm = new ReportViewModel($"Comparing {older.Run.StartedAt:MMM d, h:mm tt} with {newer.Run.StartedAt:MMM d, h:mm tt}", null, older)
        {
            _comparison = comparison,
        };
        vm.Tabs.Add(new(TabKind.Comparison, "Before vs after", ComparisonReport.Render(comparison), null));
        vm.Tabs.Add(new(TabKind.Summary, "Customer summary", SummaryReport.Render(newer.Run, comparison), null));
        vm.Selected = vm.Tabs[0];
        return vm;
    }

    /// <summary>Opens a saved run (.json) or report (.html). A run is re-rendered so older runs get the current layout.</summary>
    public static ReportViewModel FromFile(string path)
    {
        path = Path.GetFullPath(path);
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext is ".html" or ".htm")
        {
            var json = Path.ChangeExtension(path, ".json");
            var afterJson = Path.ChangeExtension(Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileName(path).Replace("_comparison", "_after")), ".json");
            if (File.Exists(json)) return FromRun(new SavedRun(json, RunFile.Load(json)));
            if (path.Contains("_comparison") && File.Exists(afterJson)) return FromRun(new SavedRun(afterJson, RunFile.Load(afterJson)));
            var kind = path.Contains("_comparison") ? TabKind.Comparison : TabKind.Report;
            var vm = new ReportViewModel(Path.GetFileNameWithoutExtension(path), null, null);
            vm.Tabs.Add(new(kind, kind == TabKind.Comparison ? "Before vs after" : "Report", File.ReadAllText(path), path));
            vm.Selected = vm.Tabs[0];
            return vm;
        }
        return FromRun(new SavedRun(path, RunFile.Load(path)));
    }

    public static string TitleFor(DiagnosticRun run)
    {
        var who = string.IsNullOrWhiteSpace(run.Intake.CustomerName) ? "Unnamed customer" : run.Intake.CustomerName;
        var type = run.Intake.RunType == "after" ? "After service" : "Before service";
        var job = string.IsNullOrWhiteSpace(run.Intake.JobNumber) ? "" : $"  ·  Job {run.Intake.JobNumber}";
        return $"{who}  ·  {type}  ·  {run.StartedAt:MMM d, yyyy h:mm tt}{job}";
    }
}
