using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GalacticDiagnosis.Core;
using GalacticDiagnosis.Gui.Services;
using GalacticDiagnosis.Report;

namespace GalacticDiagnosis.Gui.ViewModels;

public sealed class HistoryRow(SavedRun saved)
{
    public SavedRun Saved { get; } = saved;
    public string Customer { get; } = saved.Run.Intake.CustomerName.Length > 0 ? saved.Run.Intake.CustomerName : "Unnamed customer";
    public string Job { get; } = saved.Run.Intake.JobNumber;
    public string Date { get; } = saved.Run.StartedAt.ToString("MMM d, yyyy  h:mm tt");
    public string Type { get; } = saved.Run.Intake.RunType == "after" ? "After" : "Before";
    public string Machine { get; } = saved.Run.MachineName;
    public Verdict Verdict { get; } = Verdicts.Worst(saved.Run.Sections.Where(s => !s.NotApplicable).Select(s => s.Verdict));
    public string Label => HtmlReport.Label(Verdict);

    // Screen readers read a list item by its ToString.
    public override string ToString() => $"{Customer}, {Type}, {Date}";

    public bool Matches(string text) =>
        text.Length == 0 || new[] { Customer, Job, Machine, Date, Type, Saved.Run.Intake.Complaint }
            .Any(f => f.Contains(text, StringComparison.OrdinalIgnoreCase));
}

/// <summary>Every saved run in the Reports folder: search, open one, or compare any two.</summary>
public sealed partial class HistoryViewModel : ObservableObject
{
    readonly List<HistoryRow> _all;
    List<HistoryRow> _selection = [];

    public HistoryViewModel(MainViewModel main)
    {
        Main = main;
        _all = [.. main.Store.ListRuns().Select(r => new HistoryRow(r))];
        Filter();
    }

    public MainViewModel Main { get; }
    public ObservableCollection<HistoryRow> Rows { get; } = [];
    public string Folder => Main.Store.Folder;
    public bool IsEmpty => _all.Count == 0;
    public string CountText => Rows.Count == _all.Count ? $"{_all.Count} saved checks" : $"{Rows.Count} of {_all.Count} saved checks";

    [ObservableProperty]
    private string search = "";

    partial void OnSearchChanged(string value) => Filter();

    void Filter()
    {
        Rows.Clear();
        foreach (var r in _all.Where(r => r.Matches(Search.Trim()))) Rows.Add(r);
        OnPropertyChanged(nameof(CountText));
    }

    /// <summary>Called by the view; a ListBox's SelectedItems can't be bound.</summary>
    public void SetSelection(IEnumerable<HistoryRow> rows)
    {
        _selection = [.. rows];
        OpenCommand.NotifyCanExecuteChanged();
        CompareCommand.NotifyCanExecuteChanged();
        ShowInFolderCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(OneSelected))]
    private void Open() => Main.Show(ReportViewModel.FromRun(_selection[0].Saved));

    bool OneSelected() => _selection.Count == 1;

    [RelayCommand(CanExecute = nameof(TwoSelected))]
    private void Compare() => Main.Show(ReportViewModel.Compare(_selection[0].Saved, _selection[1].Saved));

    bool TwoSelected() => _selection.Count == 2;

    [RelayCommand(CanExecute = nameof(AnySelected))]
    private void ShowInFolder() => Shell.ShowInFolder(_selection[0].Saved.HtmlPath);

    bool AnySelected() => _selection.Count > 0;
}
