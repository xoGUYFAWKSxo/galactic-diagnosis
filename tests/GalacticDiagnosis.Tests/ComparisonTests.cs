using GalacticDiagnosis.Core;

namespace GalacticDiagnosis.Tests;

public class ComparisonTests
{
    static SectionResult Section(string id, Verdict v, params (string Key, double Value)[] metrics)
    {
        var s = new SectionResult { Id = id, Title = id, Verdict = v, Summary = $"{id} summary" };
        foreach (var (k, val) in metrics) s.Metrics[k] = val;
        return s;
    }

    static DiagnosticRun Run(string type, string machine, params SectionResult[] sections) => new()
    {
        Intake = new Intake { CustomerName = "Pat Smith", Complaint = type == "before" ? "Slow" : "", RunType = type },
        MachineName = machine,
        StartedAt = type == "before" ? new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero) : new DateTimeOffset(2026, 10, 2, 9, 0, 0, TimeSpan.Zero),
        RanAsAdmin = true,
        ToolVersion = "0.1.0",
        Sections = [.. sections],
    };

    [Fact]
    public void Boot_time_drop_is_better_and_worded()
    {
        var c = Comparison.Build(
            Run("before", "PC", Section("boot_time", Verdict.Warning, ("boot_ms", 94_000))),
            Run("after", "PC", Section("boot_time", Verdict.Good, ("boot_ms", 22_400))));
        var m = Assert.Single(c.Metrics);
        Assert.Equal("Startup time (last boot)", m.Label);
        Assert.Equal(("94 s", "22 s", "72 s faster", Change.Better), (m.BeforeText, m.AfterText, m.DeltaText, m.Change));
        Assert.Equal(Change.Better, Assert.Single(c.Sections).Change);
    }

    [Fact]
    public void Small_wobble_is_same()
    {
        var c = Comparison.Build(
            Run("before", "PC", Section("boot_time", Verdict.Good, ("boot_ms", 30_000))),
            Run("after", "PC", Section("boot_time", Verdict.Good, ("boot_ms", 31_500))));
        Assert.Equal(Change.Same, c.Metrics[0].Change);
    }

    [Fact]
    public void Direction_respects_higher_is_better()
    {
        var c = Comparison.Build(
            Run("before", "PC", Section("disk_space", Verdict.Bad, ("C:.free_gb", 4.2), ("C:.free_pct", 2)), Section("startup", Verdict.Warning, ("startup_enabled", 15))),
            Run("after", "PC", Section("disk_space", Verdict.Good, ("C:.free_gb", 61.0), ("C:.free_pct", 30)), Section("startup", Verdict.Good, ("startup_enabled", 6))));
        var free = c.Metrics.Single(m => m.Label == "Free space on drive C");
        Assert.Equal((Change.Better, "+56.8 GB"), (free.Change, free.DeltaText));
        var startup = c.Metrics.Single(m => m.Label == "Programs that start with Windows");
        Assert.Equal((Change.Better, "9 fewer"), (startup.Change, startup.DeltaText));
        Assert.DoesNotContain(c.Metrics, m => m.Label.Contains("pct"));
    }

    [Fact]
    public void Metric_present_only_once_is_unknown()
    {
        var c = Comparison.Build(
            Run("before", "PC", Section("boot_time", Verdict.Unavailable)),
            Run("after", "PC", Section("boot_time", Verdict.Good, ("boot_ms", 20_000))));
        Assert.Equal(("—", "20 s", Change.Unknown), (c.Metrics[0].BeforeText, c.Metrics[0].AfterText, c.Metrics[0].Change));
        Assert.Equal(Change.Unknown, c.Sections[0].Change);
    }

    [Fact]
    public void Not_applicable_sections_are_dropped_and_machine_mismatch_flagged()
    {
        var battery = Section("battery", Verdict.Info);
        battery.NotApplicable = true;
        var c = Comparison.Build(Run("before", "PC-A", battery), Run("after", "PC-B", battery));
        Assert.Empty(c.Sections);
        Assert.False(c.SameMachine);
    }

    [Fact]
    public void Run_round_trips_through_json()
    {
        var s = Section("drive_health", Verdict.Warning, ("disk0.temp_c", 29));
        s.Items.Add(new Item("WDC", "Hard drive", Verdict.Warning, "57,686 hours"));
        var run = Run("before", "PC", s);
        run.Intake.WorkDone = "Removed startup junk";

        var back = RunFile.Deserialize(RunFile.Serialize(run));

        Assert.Equal("Pat Smith", back.Intake.CustomerName);
        Assert.Equal("Removed startup junk", back.Intake.WorkDone);
        var bs = Assert.Single(back.Sections);
        Assert.Equal(Verdict.Warning, bs.Verdict);
        Assert.Equal(29, bs.Metrics["disk0.temp_c"]);
        Assert.Equal(new Item("WDC", "Hard drive", Verdict.Warning, "57,686 hours"), Assert.Single(bs.Items));
        Assert.Contains("\"verdict\": \"Warning\"", RunFile.Serialize(run));
    }
}
