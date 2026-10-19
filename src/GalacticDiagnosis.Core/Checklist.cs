namespace GalacticDiagnosis.Core;

/// <summary>A common repair job the technician can tick on an after check. SuggestedBy lists the report sections
/// whose problems this job usually fixes.</summary>
public sealed record ChecklistItem(string Text, params string[] SuggestedBy);

public static class Checklist
{
    public static IReadOnlyList<ChecklistItem> Items { get; } =
    [
        new("Turned off unneeded startup programs", "startup", "boot_time"),
        new("Installed Windows updates", "updates"),
        new("Cleaned up disk space", "disk_space"),
        new("Removed malware or unwanted software", "startup", "antivirus"),
        new("Turned on or updated antivirus", "antivirus"),
        new("Updated drivers", "hardware", "crashes"),
        new("Fixed the cause of crashes", "crashes"),
        new("Activated Windows", "activation"),
        new("Replaced or upgraded hardware", "drive_health", "battery", "hardware"),
        new("Backed up the customer's files", "drive_health"),
    ];

    /// <summary>Jobs that match problems found by the before check. Only a hint: nothing is ticked for the technician.</summary>
    public static IReadOnlyList<string> Suggested(DiagnosticRun? before)
    {
        if (before is null) return [];
        var problems = before.Sections
            .Where(s => !s.NotApplicable && s.Verdict is Verdict.Warning or Verdict.Bad)
            .Select(s => s.Id)
            .ToHashSet();
        return [.. Items.Where(i => i.SuggestedBy.Any(problems.Contains)).Select(i => i.Text)];
    }

    /// <summary>Ticked jobs plus notes as one plain-English paragraph, for plain-text summaries.</summary>
    public static string AsText(Intake intake)
    {
        var parts = new List<string>();
        if (intake.WorkItems.Count > 0) parts.Add(string.Join("; ", intake.WorkItems) + ".");
        if (!string.IsNullOrWhiteSpace(intake.WorkDone)) parts.Add(intake.WorkDone.Trim());
        return string.Join(" ", parts);
    }
}
