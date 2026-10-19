using System.Text.RegularExpressions;

namespace GalacticDiagnosis.Core;

public enum Change { Better, Worse, Same, Unknown }

/// <summary>One measured number, before and after, e.g. "Startup time: 94 s → 22 s".</summary>
public sealed record MetricChange(string Section, string Label, string BeforeText, string AfterText, string DeltaText, Change Change);

/// <summary>One report section's verdict before and after.</summary>
public sealed record SectionChange(string Id, string Title, Verdict? Before, Verdict? After, string AfterSummary, Change Change);

public sealed class ComparisonResult
{
    public required DiagnosticRun Before { get; init; }
    public required DiagnosticRun After { get; init; }
    public List<MetricChange> Metrics { get; } = [];
    public List<SectionChange> Sections { get; } = [];
    public bool SameMachine => string.Equals(Before.MachineName, After.MachineName, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Turns two saved runs into the before/after comparison the customer sees.</summary>
public static class Comparison
{
    /// <param name="Tolerance">Differences this small count as "Same" (boot times wobble by a second or two).</param>
    sealed record Def(string Section, Regex Key, Func<Match, string> Label, bool HigherIsBetter, double Tolerance,
        Func<double, string> Format, Func<double, string> Delta);

    static Regex K(string pattern) => new($"^{pattern}$", RegexOptions.CultureInvariant);

    static string Secs(double ms) => $"{ms / 1000:0} s";
    static string Gb(double gb) => gb >= 100 ? $"{gb:0} GB" : $"{gb:0.#} GB";

    // Order here is the order rows appear in the report. Metrics without a definition (temperatures, crash counts
    // over 30 days that a repair can't change) are left out; their sections still show verdict changes.
    static readonly Def[] Defs =
    [
        new("boot_time", K("boot_ms"), _ => "Startup time (last boot)", false, 2000, Secs, d => $"{Math.Abs(d) / 1000:0} s {(d < 0 ? "faster" : "slower")}"),
        new("boot_time", K("boot_median_ms"), _ => "Startup time (typical)", false, 2000, Secs, d => $"{Math.Abs(d) / 1000:0} s {(d < 0 ? "faster" : "slower")}"),
        new("boot_time", K("boot_main_path_ms"), _ => "Time to reach the desktop", false, 2000, Secs, d => $"{Math.Abs(d) / 1000:0} s {(d < 0 ? "faster" : "slower")}"),
        new("startup", K("startup_enabled"), _ => "Programs that start with Windows", false, 0, v => $"{v:0}", d => $"{Math.Abs(d):0} {(d < 0 ? "fewer" : "more")}"),
        new("startup", K("startup_flagged"), _ => "Questionable startup entries", false, 0, v => $"{v:0}", d => d < 0 ? $"{-d:0} removed" : $"{d:0} more"),
        new("disk_space", K(@"(?<d>[A-Z]):\.free_gb"), m => $"Free space on drive {m.Groups["d"].Value}", true, 0.5, Gb, d => $"{(d > 0 ? "+" : "−")}{Gb(Math.Abs(d))}"),
        new("hardware", K("ram_gb"), _ => "Memory", true, 0.5, Gb, d => $"{(d > 0 ? "+" : "−")}{Gb(Math.Abs(d))}"),
        new("battery", K(@"battery\d*\.health_pct"), _ => "Battery health", true, 1, v => $"{v:0}%", d => $"{(d > 0 ? "+" : "−")}{Math.Abs(d):0} points"),
        new("updates", K("days_since_update"), _ => "Days since the last Windows update", false, 0, v => $"{v:0}", d => d < 0 ? "Updated" : $"{d:0} days older"),
    ];

    public static ComparisonResult Build(DiagnosticRun before, DiagnosticRun after)
    {
        var result = new ComparisonResult { Before = before, After = after };
        var b = Flatten(before);
        var a = Flatten(after);

        foreach (var def in Defs)
        {
            var keys = b.Keys.Concat(a.Keys).Distinct().Where(k => k.Section == def.Section && def.Key.IsMatch(k.Metric))
                .OrderBy(k => k.Metric, StringComparer.Ordinal);
            foreach (var key in keys)
            {
                var label = def.Label(def.Key.Match(key.Metric));
                double? bv = b.TryGetValue(key, out var x) ? x : null, av = a.TryGetValue(key, out var y) ? y : null;
                if (bv is null || av is null)
                {
                    result.Metrics.Add(new(def.Section, label, bv is null ? "—" : def.Format(bv.Value), av is null ? "—" : def.Format(av.Value), "", Change.Unknown));
                    continue;
                }
                var diff = av.Value - bv.Value;
                var change = Math.Abs(diff) <= def.Tolerance ? Change.Same
                    : (diff > 0) == def.HigherIsBetter ? Change.Better : Change.Worse;
                result.Metrics.Add(new(def.Section, label, def.Format(bv.Value), def.Format(av.Value),
                    change == Change.Same ? "No change" : def.Delta(diff), change));
            }
        }

        var beforeSections = before.Sections.ToDictionary(s => s.Id);
        var afterIds = after.Sections.Select(s => s.Id).ToHashSet();
        foreach (var s in after.Sections.Concat(before.Sections.Where(s => !afterIds.Contains(s.Id))))
        {
            var bs = beforeSections.GetValueOrDefault(s.Id);
            var aS = afterIds.Contains(s.Id) ? s : null;
            if ((bs?.NotApplicable ?? true) && (aS?.NotApplicable ?? true)) continue;
            Verdict? bvd = bs is { NotApplicable: false } ? bs.Verdict : null;
            Verdict? avd = aS is { NotApplicable: false } ? aS.Verdict : null;
            result.Sections.Add(new(s.Id, s.Title, bvd, avd, aS?.Summary ?? "", Compare(bvd, avd)));
        }
        return result;
    }

    /// <summary>Good and Info rank the same; Unavailable can't be compared.</summary>
    static int? Rank(Verdict? v) => v switch
    {
        Verdict.Good or Verdict.Info => 0,
        Verdict.Warning => 1,
        Verdict.Bad => 2,
        _ => null,
    };

    static Change Compare(Verdict? before, Verdict? after) => (Rank(before), Rank(after)) switch
    {
        (int b, int a) when a < b => Change.Better,
        (int b, int a) when a > b => Change.Worse,
        (int, int) => Change.Same,
        _ => Change.Unknown,
    };

    static Dictionary<(string Section, string Metric), double> Flatten(DiagnosticRun run) =>
        run.Sections.SelectMany(s => s.Metrics.Select(m => (Key: (s.Id, m.Key), m.Value)))
            .GroupBy(x => x.Key).ToDictionary(g => g.Key, g => g.First().Value);
}
