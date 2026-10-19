using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GalacticDiagnosis.Core;
using GalacticDiagnosis.Gui.Services;
using GalacticDiagnosis.Report;

namespace GalacticDiagnosis.Gui.ViewModels;

public sealed record RecheckResult(Verdict Verdict, string Text, string Summary);

/// <summary>A section that needs attention, with its fix-it shortcuts and a Check again button.
/// Checking again is shown live only: it never changes the saved report.</summary>
public sealed partial class FixItemViewModel : ObservableObject
{
    public FixItemViewModel(SectionResult section)
    {
        Id = section.Id;
        Title = section.Title;
        Verdict = section.Verdict;
        Label = HtmlReport.Label(section.Verdict);
        Summary = section.Summary;
        Actions = FixIts.For(section.Id);
        Note = FixIts.NoteFor(section.Id);
    }

    public string Id { get; }
    public string Title { get; }
    public Verdict Verdict { get; }
    public string Label { get; }
    public string Summary { get; }
    public IReadOnlyList<FixAction> Actions { get; }
    public string? Note { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckAgainCommand))]
    private bool checking;

    /// <summary>The result of Check again, shown under the original finding.</summary>
    [ObservableProperty]
    private RecheckResult? recheck;

    [RelayCommand]
    private void Launch(FixAction action) => action.Launch();

    [RelayCommand(CanExecute = nameof(CanCheckAgain))]
    private async Task CheckAgain()
    {
        var collector = RunSession.Collectors().FirstOrDefault(c => c.Id == Id);
        if (collector is null) return;
        Checking = true;
        try
        {
            var result = (await CollectorRunner.RunAllAsync([collector], CollectorRunner.IsAdmin()))[0];
            Recheck = new RecheckResult(result.Verdict, $"Now: {HtmlReport.Label(result.Verdict)}  ·  checked {DateTime.Now:h:mm tt}", result.Summary);
        }
        finally
        {
            Checking = false;
        }
    }

    bool CanCheckAgain() => !Checking;
}
