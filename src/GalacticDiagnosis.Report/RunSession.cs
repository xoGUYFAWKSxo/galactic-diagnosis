using GalacticDiagnosis.Core;
using GalacticDiagnosis.Core.Collectors;

namespace GalacticDiagnosis.Report;

/// <summary>What a finished check produced. Comparison is set for an after run that had a before run to pair with.</summary>
public sealed record RunOutcome(SavedRun Saved, SavedRun? Before, string? ComparisonPath);

/// <summary>One full check: run every collector, save the report, and compare an after run with its before run.
/// Shared by the console and the GUI so both write identical files.</summary>
public sealed class RunSession(ReportStore store)
{
    public static string ToolVersion { get; } =
        typeof(RunSession).Assembly.GetName().Version?.ToString(3) ?? "dev";

    public static ICollector[] Collectors() =>
    [
        new HardwareCollector(), new DriveHealthCollector(), new BatteryCollector(), new CrashCollector(),
        new BootTimeCollector(), new StartupCollector(), new DiskSpaceCollector(),
        new UpdatesCollector(), new AntivirusCollector(), new ActivationCollector(),
    ];

    public async Task<RunOutcome> RunAsync(Intake intake, SavedRun? before,
        Action<SectionResult>? onDone = null, CancellationToken ct = default)
    {
        // An after run carries the original complaint forward so the comparison can show it.
        if (before is not null && intake.Complaint.Length == 0) intake.Complaint = before.Run.Intake.Complaint;

        var isAdmin = CollectorRunner.IsAdmin();
        var startedAt = DateTimeOffset.Now;
        var sections = await CollectorRunner.RunAllAsync(Collectors(), isAdmin, onDone, ct);
        var run = new DiagnosticRun
        {
            Intake = intake,
            MachineName = Environment.MachineName,
            StartedAt = startedAt,
            RanAsAdmin = isAdmin,
            ToolVersion = ToolVersion,
            Sections = sections,
            ComparedWith = intake.RunType == "after" && before is not null ? Path.GetFileName(before.JsonPath) : null,
        };

        var saved = store.Save(run);
        var comparison = intake.RunType == "after" && before is not null
            ? ReportStore.SaveComparison(before.Run, run, saved.JsonPath)
            : null;
        return new RunOutcome(saved, before, comparison);
    }
}
