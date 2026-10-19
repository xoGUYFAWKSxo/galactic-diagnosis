using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GalacticDiagnosis.Core;
using GalacticDiagnosis.Report;

namespace GalacticDiagnosis.Gui.ViewModels;

/// <summary>One check on the Running screen: a spinner until its collector reports back.</summary>
public sealed partial class CheckRow(string id, string title) : ObservableObject
{
    public string Id { get; } = id;
    public string Title { get; } = title;

    [ObservableProperty]
    private bool done;

    [ObservableProperty]
    private Verdict verdict = Verdict.Info;

    [ObservableProperty]
    private string label = "";

    [ObservableProperty]
    private string summary = "Checking…";

    [ObservableProperty]
    private string elapsed = "";

    public void Complete(SectionResult s)
    {
        Verdict = s.NotApplicable ? Verdict.Info : s.Verdict;
        Label = s.NotApplicable ? "Doesn't apply" : HtmlReport.Label(s.Verdict);
        Summary = s.Summary;
        Elapsed = $"{s.ElapsedMs / 1000:0.0} s";
        Done = true;
    }
}

public sealed partial class RunningViewModel : ObservableObject
{
    readonly CancellationTokenSource _cts = new();
    readonly Intake _intake;
    readonly SavedRun? _before;
    readonly NewCheckViewModel _form;

    public RunningViewModel(MainViewModel main, Intake intake, SavedRun? before, NewCheckViewModel form)
    {
        Main = main;
        _intake = intake;
        _before = before;
        _form = form;
        foreach (var c in RunSession.Collectors()) Rows.Add(new CheckRow(c.Id, c.Title));
        Heading = intake.CustomerName.Length > 0 ? $"Checking this PC for {intake.CustomerName}" : "Checking this PC";
    }

    public MainViewModel Main { get; }
    public string Heading { get; }
    public string SubHeading => _intake.RunType == "after"
        ? _before is null ? "After service, no comparison" : $"After service, compared with the before check from {_before.Run.StartedAt:MMM d}"
        : "Before service";
    public ObservableCollection<CheckRow> Rows { get; } = [];
    public int Total => Rows.Count;

    [ObservableProperty]
    private int doneCount;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private bool cancelling;

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        Cancelling = true;
        _cts.Cancel();
    }

    bool CanCancel() => !Cancelling;

    public async Task RunAsync()
    {
        var ui = Application.Current.Dispatcher;
        try
        {
            var outcome = await new RunSession(Main.Store).RunAsync(_intake, _before,
                s => ui.BeginInvoke(() => OnSectionDone(s)), _cts.Token);
            Main.IsBusy = false;
            Main.Show(ReportViewModel.FromRun(outcome.Saved, outcome.Before));
        }
        catch (OperationCanceledException)
        {
            BackToForm("Check cancelled. Nothing was saved.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            BackToForm($"The check finished but couldn't be saved to {Main.Store.Folder}: {ex.Message}");
        }
    }

    void OnSectionDone(SectionResult s)
    {
        Rows.FirstOrDefault(r => r.Id == s.Id)?.Complete(s);
        DoneCount = Rows.Count(r => r.Done);
    }

    void BackToForm(string message)
    {
        Main.IsBusy = false;
        Main.Page = _form;
        Main.Error = message;
    }
}
