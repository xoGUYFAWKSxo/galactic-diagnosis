using System.Text;
using GalacticDiagnosis.Core;
using static GalacticDiagnosis.Report.HtmlReport;

namespace GalacticDiagnosis.Report;

/// <summary>The before/after page handed to the customer: what got better, area by area, and what still needs attention.</summary>
public static class ComparisonReport
{
    public static string Render(ComparisonResult c)
    {
        var sb = new StringBuilder();
        var name = c.After.Intake.CustomerName.Length > 0 ? c.After.Intake.CustomerName : c.Before.Intake.CustomerName;
        var title = name.Length > 0 ? $"Before & After — {name}" : "Before & After";
        var complaint = c.Before.Intake.Complaint.Length > 0 ? c.Before.Intake.Complaint : c.After.Intake.Complaint;

        sb.Append($"""
            <!doctype html>
            <html lang="en">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>{E(title)}</title>
            <style>{Css}{ExtraCss}</style>
            </head>
            <body>
            <main>
            <header>
              <h1>{E(title)}</h1>
              <dl class="meta">
                <div><dt>Before</dt><dd>{c.Before.StartedAt:MMMM d, yyyy}</dd></div>
                <div><dt>After</dt><dd>{c.After.StartedAt:MMMM d, yyyy}</dd></div>
                <div><dt>Computer</dt><dd>{E(c.After.MachineName)}</dd></div>
              {ShopMeta(c.After.Intake)}</dl>
            """);
        if (complaint.Length > 0)
            sb.Append($"""<p class="complaint"><strong>Reported problem:</strong> {E(complaint)}</p>""");
        sb.Append(WorkDone(c.After.Intake));
        if (!c.SameMachine)
            sb.Append($"""<p class="mismatch">These two checks were run on different computers ({E(c.Before.MachineName)} and {E(c.After.MachineName)}), so the comparison may not be meaningful.</p>""");
        sb.Append("</header>");

        sb.Append("""<section class="glance"><h2>Results</h2>""");
        sb.Append(ResultsList(c));
        sb.Append("</section>");

        if (c.Metrics.Count > 0)
        {
            sb.Append("""<section class="detail"><h2>Measurements</h2><table class="cmp"><thead><tr><th>Measure</th><th>Before</th><th>After</th><th>Change</th></tr></thead><tbody>""");
            foreach (var m in c.Metrics)
                sb.Append($"""<tr class="{ChangeCls(m.Change)}"><th>{E(m.Label)}</th><td>{E(m.BeforeText)}</td><td>{E(m.AfterText)}</td><td>{ChangePill(m.Change, m.DeltaText)}</td></tr>""");
            sb.Append("</tbody></table></section>");
        }

        sb.Append("""<section class="detail"><h2>Area by area</h2><table class="cmp"><thead><tr><th>Area</th><th>Before</th><th>After</th></tr></thead><tbody>""");
        foreach (var s in c.Sections)
            sb.Append($"""<tr class="{ChangeCls(s.Change)}"><th>{E(s.Title)}</th><td>{VerdictPill(s.Before)}</td><td>{VerdictPill(s.After)}</td></tr>""");
        sb.Append("</tbody></table></section>");

        var open = c.Sections.Where(s => s.After is Verdict.Warning or Verdict.Bad).ToList();
        sb.Append("""<section class="detail"><h2>Still needs attention</h2>""");
        if (open.Count == 0)
            sb.Append("""<p class="summary">Nothing else needs attention.</p>""");
        else
        {
            sb.Append("<ul class=\"open\">");
            foreach (var s in open)
                sb.Append($"""<li><span class="pill {Cls(s.After!.Value)}">{Label(s.After!.Value)}</span><span><strong>{E(s.Title)}:</strong> {E(s.AfterSummary)}</span></li>""");
            sb.Append("</ul>");
        }
        sb.Append("</section>");

        sb.Append($"""
            <footer>
              <p>Both checks read this computer's own Windows records. Nothing was sent over the internet.</p>
              <p class="tech">Galactic Diagnosis {E(c.After.ToolVersion)}</p>
            </footer>
            </main>
            </body>
            </html>
            """);
        return sb.ToString();
    }

    /// <summary>One improvement: "Startup time (last boot)", "128 s → 34 s", "94 s faster" (Delta is null for a verdict change).</summary>
    public sealed record Headline(string Label, string Change, string? Delta);

    /// <summary>One headline per improved area: its first improved measurement, or else its verdict change.</summary>
    public static List<Headline> Headlines(ComparisonResult c)
    {
        var better = c.Metrics.Where(m => m.Change == Change.Better).GroupBy(m => m.Section).Select(g => g.First()).ToList();
        var fixedAreas = c.Sections.Where(s => s.Change == Change.Better && better.All(m => m.Section != s.Id));
        return
        [
            .. better.Select(m => new Headline(m.Label, $"{m.BeforeText} → {m.AfterText}", m.DeltaText)),
            .. fixedAreas.Select(s => new Headline(s.Title, $"{Label(s.Before!.Value)} → {Label(s.After!.Value)}", null)),
        ];
    }

    internal static string ResultsList(ComparisonResult c)
    {
        var lines = Headlines(c);
        if (lines.Count == 0)
            return """<p class="summary">No measured changes between the two checks.</p>""";
        var sb = new StringBuilder("""<ul class="wins">""");
        foreach (var h in lines)
        {
            var delta = h.Delta is null ? "" : $""" <span class="delta">({E(h.Delta)})</span>""";
            sb.Append($"""<li><span class="pill better">Better</span><span><strong>{E(h.Label)}:</strong> {E(h.Change)}{delta}</span></li>""");
        }
        sb.Append("</ul>");
        return sb.ToString();
    }

    static string ChangeCls(Change c) => $"row-{c.ToString().ToLowerInvariant()}";

    static string ChangePill(Change c, string delta) => c switch
    {
        Change.Better => $"""<span class="pill better">{E(delta)}</span>""",
        Change.Worse => $"""<span class="pill worse">{E(delta)}</span>""",
        Change.Same => """<span class="nochange">No change</span>""",
        _ => """<span class="nochange">Not measured both times</span>""",
    };

    static string VerdictPill(Verdict? v) => v is { } x
        ? $"""<span class="pill {Cls(x)}">{Label(x)}</span>"""
        : """<span class="nochange">Not checked</span>""";

    internal const string ExtraCss = """
        .wins,.open{list-style:none;margin:0;padding:0}
        .wins li,.open li{display:flex;gap:12px;align-items:baseline;padding:8px 0;border-top:1px solid var(--line)}
        .wins li:first-child,.open li:first-child{border-top:0}
        .delta,.nochange{color:var(--muted)}
        .mismatch{margin:14px 0 0;padding:8px 12px;border-radius:8px;color:var(--warning);background:var(--warning-bg)}
        table.cmp thead th{font-size:12px;color:var(--muted);text-transform:uppercase;letter-spacing:.04em;font-weight:600;border-top:0}
        table.cmp th:first-child{width:40%}
        table.cmp tr.row-same td,table.cmp tr.row-same th{color:var(--muted);font-weight:400}
        """;
}
