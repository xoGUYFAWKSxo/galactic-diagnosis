using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GalacticDiagnosis.Core;
using GalacticDiagnosis.Report;

namespace GalacticDiagnosis.Gui.ViewModels;

/// <summary>The intake form. For an after check it also picks the before check to compare with.</summary>
public sealed partial class NewCheckViewModel : ObservableObject
{
    bool _pickingBefore;
    bool _userPickedBefore;

    public NewCheckViewModel(MainViewModel main)
    {
        Main = main;
        technicianName = main.Settings.TechnicianName;
        BeforeRuns = main.Store.ListRuns()
            .Where(r => r.Run.Intake.RunType == "before" && r.Run.MachineName.Equals(main.MachineName, StringComparison.OrdinalIgnoreCase))
            .Take(30)
            .ToList();
        PickBefore();
    }

    public MainViewModel Main { get; }
    public ChecklistViewModel Checklist { get; } = new();

    /// <summary>Before checks from this PC, newest first.</summary>
    public IReadOnlyList<SavedRun> BeforeRuns { get; }
    public bool HasBeforeRuns => BeforeRuns.Count > 0;

    [ObservableProperty]
    private string customerName = "";

    [ObservableProperty]
    private string jobNumber = "";

    [ObservableProperty]
    private string technicianName;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBefore))]
    private bool isAfter;

    public bool IsBefore
    {
        get => !IsAfter;
        set => IsAfter = !value;
    }

    [ObservableProperty]
    private string complaint = "";

    [ObservableProperty]
    private string workDone = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CompareNote))]
    private SavedRun? selectedBefore;

    public string CompareNote => SelectedBefore is null
        ? HasBeforeRuns ? "No comparison will be made. Pick a before check above to compare with it." : "No before check from this PC was found in the Reports folder, so the after report will be saved without a comparison."
        : $"The report will compare this PC now with the before check from {SelectedBefore.Run.StartedAt:MMM d, h:mm tt}.";

    partial void OnCustomerNameChanged(string value)
    {
        if (!_userPickedBefore) PickBefore();
    }

    partial void OnSelectedBeforeChanged(SavedRun? value)
    {
        if (!_pickingBefore) _userPickedBefore = true;
        Checklist.Suggest(value?.Run);
    }

    /// <summary>Same rule as the console: latest before check from this PC, preferring the same customer.</summary>
    void PickBefore()
    {
        _pickingBefore = true;
        SelectedBefore = BeforeRuns
            .OrderByDescending(r => CustomerName.Trim().Length > 0 && r.Run.Intake.CustomerName.Equals(CustomerName.Trim(), StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(r => r.Run.StartedAt)
            .FirstOrDefault();
        _pickingBefore = false;
    }

    [RelayCommand]
    private void NoComparison() => SelectedBefore = null;

    [RelayCommand]
    private void Start()
    {
        Main.Settings.TechnicianName = TechnicianName.Trim();
        Main.Settings.Save();
        var intake = new Intake
        {
            CustomerName = CustomerName.Trim(),
            JobNumber = JobNumber.Trim(),
            TechnicianName = TechnicianName.Trim(),
            RunType = IsAfter ? "after" : "before",
            Complaint = IsAfter ? "" : Complaint.Trim(),
            WorkDone = IsAfter ? WorkDone.Trim() : "",
            WorkItems = IsAfter ? Checklist.Ticked : [],
        };
        Main.StartCheck(intake, IsAfter ? SelectedBefore : null, this);
    }
}
