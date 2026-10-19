using System.Text;
using GalacticDiagnosis.Core;
using static GalacticDiagnosis.Report.HtmlReport;

namespace GalacticDiagnosis.Report;

/// <summary>A one-page, non-technical summary for the customer: what we found, what we did, what changed,
/// and what still needs attention. The full report has the details.</summary>
public static class SummaryReport
{
    public static string Render(DiagnosticRun run, ComparisonResult? comparison = null)
    {
        var sb = new StringBuilder();
        var name = run.Intake.CustomerName;
        var title = name.Length > 0 ? $"Your PC check — {name}" : "Your PC check";
        var sections = run.Sections.Where(s => !s.NotApplicable).ToList();
        var problems = sections.Where(s => s.Verdict is Verdict.Warning or Verdict.Bad)
            .OrderByDescending(s => s.Verdict).ToList();
        var fine = sections.Where(s => s.Verdict is Verdict.Good or Verdict.Info).Select(s => s.Title).ToList();
        var isAfter = run.Intake.RunType == "after";
        var complaint = comparison?.Before.Intake.Complaint is { Length: > 0 } c ? c : run.Intake.Complaint;

        sb.Append($"""
            <!doctype html>
            <html lang="en">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>{E(title)}</title>
            <style>{Css}{ComparisonReport.ExtraCss}{SummaryCss}</style>
            </head>
            <body>
            <main class="summary-page">
            <header>
              <h1>{E(title)}</h1>
              <dl class="meta">
                <div><dt>Date</dt><dd>{run.StartedAt:MMMM d, yyyy}</dd></div>
                <div><dt>Computer</dt><dd>{E(run.MachineName)}</dd></div>
              {ShopMeta(run.Intake)}</dl>
            """);
        if (complaint.Length > 0)
            sb.Append($"""<p class="complaint"><strong>You told us:</strong> {E(complaint)}</p>""");
        sb.Append("</header>");

        if (isAfter && WorkDone(run.Intake, labelled: false) is { Length: > 0 } work)
            sb.Append($"""<section><h2>What we did</h2>{work}</section>""");

        if (comparison is not null)
            sb.Append($"""<section class="glance"><h2>What changed</h2>{ComparisonReport.ResultsList(comparison)}</section>""");

        sb.Append($"""<section><h2>{(isAfter ? "Still needs attention" : "What we found")}</h2>""");
        if (problems.Count == 0)
            sb.Append($"""<p class="summary">{(isAfter ? "Nothing else needs attention." : "Everything we checked looks healthy.")}</p>""");
        else
        {
            sb.Append("""<ul class="open">""");
            foreach (var s in problems)
                sb.Append($"""<li><span class="pill {Cls(s.Verdict)}">{Label(s.Verdict)}</span><span><strong>{E(s.Title)}:</strong> {E(s.Summary)}</span></li>""");
            sb.Append("</ul>");
        }
        if (fine.Count > 0)
            sb.Append($"""<p class="allgood"><span class="pill good">Good</span> {E(string.Join(", ", fine))}</p>""");
        sb.Append("</section>");

        sb.Append($"""
            <footer>
              <p>This page covers the main points. The full report has every detail we checked.</p>
              <p class="tech">Galactic Diagnosis {E(run.ToolVersion)}</p>
            </footer>
            </main>
            </body>
            </html>
            """);
        return sb.ToString();
    }

    const string SummaryCss = """
        .summary-page h1{font-size:28px}
        .allgood{margin:12px 0 0;color:var(--muted)} .allgood .pill{margin-right:8px}
        @media print{.summary-page{font-size:13px}}
        """;
}
