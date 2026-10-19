using System.Diagnostics.Eventing.Reader;
using System.Xml.Linq;

namespace GalacticDiagnosis.Core.Collectors;

/// <summary>Boot duration from Diagnostics-Performance event 100. The log needs admin to read.</summary>
public sealed class BootTimeCollector : ICollector
{
    const string Log = "Microsoft-Windows-Diagnostics-Performance/Operational";
    const long WindowMs = 90L * 24 * 60 * 60 * 1000;

    public string Id => "boot_time";
    public string Title => "Startup time";
    public bool NeedsAdmin => true;

    public SectionResult Collect(CancellationToken ct)
    {
        var r = new SectionResult { Id = Id, Title = Title };
        var boots = new List<Boot>();
        using (var reader = new EventLogReader(new EventLogQuery(Log, PathType.LogName,
            $"*[System[EventID=100 and TimeCreated[timediff(@SystemTime) <= {WindowMs}]]]") { ReverseDirection = true }))
        {
            for (EventRecord? e; boots.Count < 10 && (e = reader.ReadEvent()) is not null;)
                using (e)
                {
                    ct.ThrowIfCancellationRequested();
                    if (ParseBootEvent(e.ToXml(), e.TimeCreated ?? DateTime.MinValue) is { } b) boots.Add(b);
                }
        }

        if (boots.Count == 0)
            return new SectionResult
            {
                Id = Id, Title = Title, Verdict = Verdict.Info,
                Summary = "Windows hasn't recorded a startup time in the last 90 days, so it couldn't be measured.",
            };

        var last = boots[0];
        var sec = last.TotalMs / 1000.0;
        r.Metrics["boot_ms"] = last.TotalMs;
        if (last.MainPathMs > 0) r.Metrics["boot_main_path_ms"] = last.MainPathMs;
        r.Verdict = Rate(last.TotalMs);

        r.Items.Add(new("Last startup", $"{sec:0} seconds, on {last.When:yyyy-MM-dd}", r.Verdict,
            last.MainPathMs > 0 ? $"{last.MainPathMs / 1000.0:0} s to the sign-in screen and desktop, then {(last.TotalMs - last.MainPathMs) / 1000.0:0} s for startup programs to settle." : null));
        if (boots.Count > 1)
        {
            var median = boots.Select(b => b.TotalMs).Order().ElementAt(boots.Count / 2);
            r.Metrics["boot_median_ms"] = median;
            r.Items.Add(new("Typical startup", $"{median / 1000.0:0} seconds (middle of the last {boots.Count} recorded)"));
        }

        r.Summary = r.Verdict switch
        {
            Verdict.Bad => $"Windows took {sec:0} seconds to start up last time. That's slow; a healthy PC is usually ready in under a minute.",
            Verdict.Warning => $"Windows took {sec:0} seconds to start up last time, which is slower than it should be.",
            _ => $"Windows took {sec:0} seconds to start up last time, which is fine.",
        };
        if (r.Verdict != Verdict.Good && last.MainPathMs > 0 && last.TotalMs - last.MainPathMs > last.MainPathMs)
            r.Summary += " Most of that time is spent after the desktop appears, which usually means too many programs start with Windows.";
        return r;
    }

    // Measured from Windows starting to load (after the BIOS) until the desktop is idle.
    static Verdict Rate(long ms) => ms switch { < 60_000 => Verdict.Good, < 120_000 => Verdict.Warning, _ => Verdict.Bad };

    public sealed record Boot(DateTime When, long TotalMs, long MainPathMs);

    /// <summary>Reads BootTime / MainPathBootTime from event 100's EventData by name, not position.</summary>
    public static Boot? ParseBootEvent(string xml, DateTime when)
    {
        var data = XDocument.Parse(xml).Descendants().Where(e => e.Name.LocalName == "Data")
            .ToDictionary(e => (string?)e.Attribute("Name") ?? "", e => e.Value, StringComparer.OrdinalIgnoreCase);
        static long N(Dictionary<string, string> d, string k) => d.TryGetValue(k, out var v) && long.TryParse(v, out var n) ? n : 0;
        var total = N(data, "BootTime");
        return total > 0 ? new Boot(when, total, N(data, "MainPathBootTime")) : null;
    }
}
