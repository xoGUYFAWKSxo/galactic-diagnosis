using CommunityToolkit.Mvvm.ComponentModel;

namespace GalacticDiagnosis.Gui.ViewModels;

/// <summary>Start page. Phase 8 turns this into the New check form.</summary>
public sealed partial class HomeViewModel(MainViewModel main) : ObservableObject
{
    public MainViewModel Main { get; } = main;
}
