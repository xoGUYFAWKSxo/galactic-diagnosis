using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using GalacticDiagnosis.Core;

namespace GalacticDiagnosis.Gui.ViewModels;

public sealed partial class ChecklistEntry(string text) : ObservableObject
{
    public string Text { get; } = text;

    [ObservableProperty]
    private bool isChecked;

    /// <summary>The before check found a problem this job usually fixes.</summary>
    [ObservableProperty]
    private bool isSuggested;
}

/// <summary>The technician checklist, used on the after-check form and when editing notes.</summary>
public sealed class ChecklistViewModel
{
    public ObservableCollection<ChecklistEntry> Entries { get; } = [.. Checklist.Items.Select(i => new ChecklistEntry(i.Text))];

    public List<string> Ticked => [.. Entries.Where(e => e.IsChecked).Select(e => e.Text)];

    public void Suggest(DiagnosticRun? before)
    {
        var suggested = Checklist.Suggested(before);
        foreach (var e in Entries) e.IsSuggested = suggested.Contains(e.Text);
    }

    /// <summary>Ticks saved jobs. A saved job that's no longer on the standard list is kept as an extra entry.</summary>
    public void Load(IEnumerable<string> ticked)
    {
        foreach (var text in ticked)
        {
            var entry = Entries.FirstOrDefault(e => e.Text == text);
            if (entry is null) Entries.Add(entry = new ChecklistEntry(text));
            entry.IsChecked = true;
        }
    }
}
