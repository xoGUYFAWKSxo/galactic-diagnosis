using GalacticDiagnosis.Core;
using GalacticDiagnosis.Report;

namespace GalacticDiagnosis.Tests;

public class ChecklistTests
{
    static DiagnosticRun Run(string type, params (string Id, Verdict V)[] sections) => new()
    {
        Intake = new Intake { CustomerName = "Pat Smith", Complaint = "Slow", RunType = type },
        MachineName = "PC1",
        StartedAt = new DateTimeOffset(2026, 10, type == "before" ? 16 : 18, 9, 0, 0, TimeSpan.Zero),
        RanAsAdmin = true,
        ToolVersion = "test",
        Sections = [.. sections.Select(s => new SectionResult { Id = s.Id, Title = s.Id + " title", Verdict = s.V, Summary = s.Id + " summary" })],
    };

    [Fact]
    public void Suggests_jobs_for_problem_sections_only()
    {
        var before = Run("before", ("startup", Verdict.Warning), ("disk_space", Verdict.Bad), ("updates", Verdict.Good), ("battery", Verdict.Unavailable));
        var s = Checklist.Suggested(before);
        Assert.Contains("Turned off unneeded startup programs", s);
        Assert.Contains("Cleaned up disk space", s);
        Assert.Contains("Removed malware or unwanted software", s);
        Assert.DoesNotContain("Installed Windows updates", s);
        Assert.DoesNotContain("Replaced or upgraded hardware", s);
        Assert.Empty(Checklist.Suggested(null));
    }

    [Fact]
    public void Every_suggestion_points_at_a_real_section()
    {
        var ids = RunSession.Collectors().Select(c => c.Id).ToHashSet();
        Assert.All(Checklist.Items, i => Assert.All(i.SuggestedBy, id => Assert.Contains(id, ids)));
    }

    [Fact]
    public void Work_done_lists_ticked_jobs_then_notes()
    {
        var intake = new Intake { RunType = "after", WorkItems = ["Installed Windows updates", "Cleaned up disk space"], WorkDone = "Also replaced the fan." };
        Assert.Equal("Installed Windows updates; Cleaned up disk space. Also replaced the fan.", Checklist.AsText(intake));
        var after = Run("after", ("updates", Verdict.Good));
        after.Intake.WorkItems = intake.WorkItems;
        after.Intake.WorkDone = intake.WorkDone;
        var html = HtmlReport.Render(after);
        Assert.Contains("<li>Installed Windows updates</li>", html);
        Assert.Contains("Also replaced the fan.", html);
    }

    [Fact]
    public void Summary_shows_findings_for_a_before_run()
    {
        var html = SummaryReport.Render(Run("before", ("startup", Verdict.Warning), ("updates", Verdict.Good)));
        Assert.Contains("What we found", html);
        Assert.Contains("startup summary", html);
        Assert.Contains("updates title", html);
        Assert.DoesNotContain("What we did", html);
    }

    [Fact]
    public void Summary_shows_work_and_results_for_an_after_run()
    {
        var before = Run("before", ("startup", Verdict.Warning));
        var after = Run("after", ("startup", Verdict.Good));
        after.Intake.WorkItems = ["Turned off unneeded startup programs"];
        var html = SummaryReport.Render(after, Comparison.Build(before, after));
        Assert.Contains("What we did", html);
        Assert.Contains("What changed", html);
        Assert.Contains("Nothing else needs attention.", html);
        Assert.Contains("You told us:</strong> Slow", html);
    }
}
