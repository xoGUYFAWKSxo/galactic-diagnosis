using System.Net;
using System.Text;
using GalacticDiagnosis.Core;

namespace GalacticDiagnosis.Report;

/// <summary>Renders a run as one self-contained HTML file (inline CSS, no scripts, no external files). Print to PDF from any browser.</summary>
public static class HtmlReport
{
    public static string Render(DiagnosticRun run)
    {
        var sb = new StringBuilder();
        var title = string.IsNullOrWhiteSpace(run.Intake.CustomerName)
            ? "PC Health Report"
            : $"PC Health Report — {run.Intake.CustomerName}";
        var sections = run.Sections.Where(s => !s.NotApplicable).ToList();
        var overall = Verdicts.Worst(sections.Select(s => s.Verdict));

        sb.Append($"""
            <!doctype html>
            <html lang="en">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>{E(title)}</title>
            <style>{Css}</style>
            </head>
            <body>
            <main>
            <header>
              <h1>{E(title)}</h1>
              <dl class="meta">
                <div><dt>Date</dt><dd>{run.StartedAt:MMMM d, yyyy h:mm tt}</dd></div>
                <div><dt>Computer</dt><dd>{E(run.MachineName)}</dd></div>
                <div><dt>Check</dt><dd>{(run.Intake.RunType == "after" ? "After service" : "Before service")}</dd></div>
              {ShopMeta(run.Intake)}</dl>
            """);
        if (!string.IsNullOrWhiteSpace(run.Intake.Complaint))
            sb.Append($"""<p class="complaint"><strong>Reported problem:</strong> {E(run.Intake.Complaint)}</p>""");
        if (run.Intake.RunType == "after") sb.Append(WorkDone(run.Intake));
        sb.Append("</header>");

        sb.Append($"""
            <section class="glance">
              <h2>At a glance <span class="pill {Cls(overall)}">{Label(overall)}</span></h2>
              <ul>
            """);
        foreach (var s in sections)
            sb.Append($"""<li><span class="pill {Cls(s.Verdict)}">{Label(s.Verdict)}</span><a href="#{E(s.Id)}">{E(s.Title)}</a><span class="sum">{E(s.Summary)}</span></li>""");
        sb.Append("</ul></section>");

        foreach (var s in sections)
        {
            sb.Append($"""
                <section class="detail" id="{E(s.Id)}">
                  <h2>{E(s.Title)} <span class="pill {Cls(s.Verdict)}">{Label(s.Verdict)}</span></h2>
                  <p class="summary">{E(s.Summary)}</p>
                """);
            if (s.Items.Count > 0)
            {
                sb.Append("<table>");
                foreach (var i in s.Items)
                {
                    var sub = i.Label.StartsWith("  ");
                    var mark = i.Verdict is Verdict.Info ? "" : $"""<span class="dot {Cls(i.Verdict)}" title="{Label(i.Verdict)}"></span>""";
                    sb.Append($"""<tr{(sub ? " class=\"sub\"" : "")}><th>{E(i.Label.Trim())}</th><td>{mark}{E(i.Value)}""");
                    if (i.Note is not null) sb.Append($"""<div class="note">{E(i.Note)}</div>""");
                    sb.Append("</td></tr>");
                }
                sb.Append("</table>");
            }
            if (s.Error is not null)
                sb.Append($"""<p class="tech">Technical detail: {E(s.Error)}</p>""");
            sb.Append("</section>");
        }

        sb.Append($"""
            <footer>
              <p>Everything in this report was read from this computer's own Windows records. Nothing was sent over the internet.</p>
              <p class="tech">Galactic Diagnosis {E(run.ToolVersion)}{(run.RanAsAdmin ? "" : " · run without administrator rights, so some sections may be incomplete")}</p>
            </footer>
            </main>
            </body>
            </html>
            """);
        return sb.ToString();
    }

    internal static string E(string? s) => WebUtility.HtmlEncode(s ?? "");

    /// <summary>"Work done": ticked checklist jobs as a list, then the technician's own notes. Empty when there's neither.</summary>
    internal static string WorkDone(Intake intake, bool labelled = true)
    {
        var notes = intake.WorkDone.Trim();
        var label = labelled ? "<strong>Work done:</strong> " : "";
        if (intake.WorkItems.Count == 0 && notes.Length == 0) return "";
        if (intake.WorkItems.Count == 0) return $"""<p class="complaint">{label}{E(notes)}</p>""";
        var sb = new StringBuilder(labelled ? """<p class="complaint"><strong>Work done:</strong></p>""" : "");
        sb.Append("""<ul class="work">""");
        foreach (var item in intake.WorkItems) sb.Append($"<li>{E(item)}</li>");
        sb.Append("</ul>");
        if (notes.Length > 0) sb.Append($"""<p class="notes">{E(notes)}</p>""");
        return sb.ToString();
    }

    /// <summary>Job number and "Checked by" header entries, when given.</summary>
    internal static string ShopMeta(Intake intake)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(intake.JobNumber))
            sb.Append($"""<div><dt>Job</dt><dd>{E(intake.JobNumber)}</dd></div>""");
        if (!string.IsNullOrWhiteSpace(intake.TechnicianName))
            sb.Append($"""<div><dt>Checked by</dt><dd>{E(intake.TechnicianName)}</dd></div>""");
        return sb.ToString();
    }

    internal static string Cls(Verdict v) => v.ToString().ToLowerInvariant();

    public static string Label(Verdict v) => v switch
    {
        Verdict.Good => "Good",
        Verdict.Warning => "Needs attention",
        Verdict.Bad => "Problem",
        Verdict.Unavailable => "Couldn't check",
        _ => "Info",
    };

    internal const string Css = """
        :root{--ink:#1d2433;--muted:#5d6678;--line:#e3e6ec;--bg:#f6f7f9;--card:#fff;
          --good:#1f7a4d;--good-bg:#e3f4ea;--warning:#8a5a00;--warning-bg:#fdf1d8;--bad:#b42318;--bad-bg:#fde7e5;
          --unavailable:#596273;--unavailable-bg:#eceef2;--info:#3b5bab;--info-bg:#e7edfb}
        *{box-sizing:border-box}
        body{margin:0;background:var(--bg);color:var(--ink);font:15px/1.5 "Segoe UI",system-ui,-apple-system,sans-serif}
        main{max-width:860px;margin:0 auto;padding:32px 16px 48px}
        h1{font-size:26px;margin:0 0 12px}
        h2{font-size:18px;margin:0 0 8px;display:flex;align-items:center;gap:10px;flex-wrap:wrap}
        header,section{background:var(--card);border:1px solid var(--line);border-radius:10px;padding:20px 22px;margin-bottom:16px}
        .meta{display:flex;flex-wrap:wrap;gap:8px 32px;margin:0}
        .meta dt{font-size:12px;color:var(--muted);text-transform:uppercase;letter-spacing:.04em}
        .meta dd{margin:0;font-weight:600}
        .complaint{margin:14px 0 0}
        ul.work{margin:4px 0 0;padding-left:22px} ul.work li{margin:2px 0} .notes{margin:6px 0 0}
        .pill{display:inline-block;font-size:12px;font-weight:600;padding:2px 10px;border-radius:999px;white-space:nowrap}
        .good{color:var(--good);background:var(--good-bg)} .warning{color:var(--warning);background:var(--warning-bg)}
        .bad{color:var(--bad);background:var(--bad-bg)} .unavailable{color:var(--unavailable);background:var(--unavailable-bg)}
        .info{color:var(--info);background:var(--info-bg)}
        .better{color:var(--good);background:var(--good-bg)} .worse{color:var(--bad);background:var(--bad-bg)} .same,.unknown{color:var(--unavailable);background:var(--unavailable-bg)}
        .glance ul{list-style:none;margin:0;padding:0}
        .glance li{display:grid;grid-template-columns:130px 1fr;gap:2px 12px;padding:10px 0;border-top:1px solid var(--line)}
        .glance li:first-child{border-top:0}
        .glance .pill{grid-row:span 2;align-self:start;justify-self:start}
        .glance a{color:var(--ink);font-weight:600;text-decoration:none}
        .glance .sum{color:var(--muted)}
        .summary{margin:0 0 12px}
        table{width:100%;border-collapse:collapse}
        th,td{text-align:left;vertical-align:top;padding:7px 0;border-top:1px solid var(--line)}
        th{width:34%;font-weight:600;padding-right:16px}
        tr.sub th{font-weight:400;color:var(--muted);padding-left:16px}
        tr.sub th,tr.sub td{border-top:0;padding-top:2px;padding-bottom:2px;font-size:14px}
        td{overflow-wrap:anywhere}
        .dot{display:inline-block;width:9px;height:9px;border-radius:50%;margin-right:8px;vertical-align:1px}
        .dot.good{background:var(--good)} .dot.warning{background:#d99100} .dot.bad{background:var(--bad)} .dot.unavailable{background:var(--unavailable)}
        .note{font-size:13px;color:var(--muted)}
        .tech{font-size:12px;color:var(--muted);margin:10px 0 0}
        footer{color:var(--muted);font-size:13px;padding:0 4px}
        @media (max-width:560px){.glance li{grid-template-columns:1fr}.glance .pill{grid-row:auto}th{width:42%}}
        @media screen and (prefers-color-scheme:dark){:root{color-scheme:dark;--ink:#ecf2fc;--muted:#8c9cb8;--line:#26305a;--bg:#0c1028;--card:#121a3a;
          --good:#5fd49a;--good-bg:#123d2c;--warning:#ffc552;--warning-bg:#3d3011;--bad:#ff7a8e;--bad-bg:#45182a;
          --unavailable:#a3adc2;--unavailable-bg:#232b48;--info:#8fb0ff;--info-bg:#1b2a55}}
        @media print{body{background:#fff;font-size:12px}main{padding:0;max-width:none}
          header,section{border:0;border-bottom:1px solid var(--line);border-radius:0;padding:12px 0;break-inside:avoid}
          .pill,.dot{-webkit-print-color-adjust:exact;print-color-adjust:exact}a{color:inherit}}
        """;
}
