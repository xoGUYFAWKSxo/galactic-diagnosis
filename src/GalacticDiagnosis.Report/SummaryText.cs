using System.Text;
using GalacticDiagnosis.Core;

namespace GalacticDiagnosis.Report;

/// <summary>The customer summary as plain text, for pasting into an invoice, ticket or text message.</summary>
public static class SummaryText
{
    public static string Render(DiagnosticRun run, ComparisonResult? comparison = null)
    {
        var sb = new StringBuilder();
        var i = run.Intake;
        var who = i.CustomerName.Length > 0 ? i.CustomerName : "Customer";
        var job = i.JobNumber.Length > 0 ? $" (job {i.JobNumber})" : "";
        var isAfter = i.RunType == "after";
        sb.AppendLine($"PC check: {who}{job}");
        sb.AppendLine($"{run.StartedAt:MMM d, yyyy}, {run.MachineName}, {(isAfter ? "after service" : "before service")}");

        var complaint = comparison?.Before.Intake.Complaint is { Length: > 0 } c ? c : i.Complaint;
        if (complaint.Length > 0) sb.AppendLine($"Reported problem: {complaint}");
        if (isAfter && Checklist.AsText(i) is { Length: > 0 } work) sb.AppendLine($"Work done: {work}");

        if (comparison is not null)
        {
            var headlines = ComparisonReport.Headlines(comparison);
            sb.AppendLine();
            sb.AppendLine("What changed:");
            if (headlines.Count == 0) sb.AppendLine("- No measured changes.");
            foreach (var h in headlines)
                sb.AppendLine($"- {h.Label}: {h.Change}{(h.Delta is null ? "" : $" ({h.Delta})")}");
        }

        var sections = run.Sections.Where(s => !s.NotApplicable).ToList();
        var problems = sections.Where(s => s.Verdict is Verdict.Warning or Verdict.Bad).OrderByDescending(s => s.Verdict).ToList();
        sb.AppendLine();
        sb.AppendLine(isAfter ? "Still needs attention:" : "What we found:");
        if (problems.Count == 0) sb.AppendLine(isAfter ? "- Nothing else." : "- Everything we checked looks healthy.");
        foreach (var s in problems) sb.AppendLine($"- {s.Title} ({HtmlReport.Label(s.Verdict)}): {s.Summary}");

        var fine = sections.Where(s => s.Verdict is Verdict.Good or Verdict.Info).Select(s => s.Title).ToList();
        if (fine.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"Good: {string.Join(", ", fine)}");
        }
        return sb.ToString().TrimEnd() + Environment.NewLine;
    }
}
