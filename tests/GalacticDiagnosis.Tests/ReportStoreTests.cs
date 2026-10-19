using GalacticDiagnosis.Core;
using GalacticDiagnosis.Report;

namespace GalacticDiagnosis.Tests;

public sealed class ReportStoreTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "gd-tests-" + Guid.NewGuid().ToString("N"));
    readonly ReportStore _store;

    public ReportStoreTests() => _store = new ReportStore(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    static DiagnosticRun Run(string customer, string type, string machine, int day, int hour = 9) => new()
    {
        Intake = new Intake { CustomerName = customer, Complaint = type == "before" ? "Slow" : "", RunType = type },
        MachineName = machine,
        StartedAt = new DateTimeOffset(2026, 10, day, hour, 0, 0, TimeSpan.Zero),
        RanAsAdmin = true,
        ToolVersion = "test",
        Sections = [new SectionResult { Id = "hardware", Title = "Hardware", Summary = "ok" }],
    };

    [Theory]
    [InlineData("Pat Smith", "Smith")]
    [InlineData("  Madonna ", "Madonna")]
    [InlineData("", "Customer")]
    public void Last_name(string name, string expected) => Assert.Equal(expected, ReportStore.LastName(name));

    [Fact]
    public void File_safe_drops_invalid_characters() =>
        Assert.Equal("OBrienJr", ReportStore.FileSafe("O'Brien/Jr".Replace("'", "")));

    [Fact]
    public void Save_names_files_and_avoids_overwriting()
    {
        var a = _store.Save(Run("Pat Smith", "before", "PC1", 18));
        var b = _store.Save(Run("Pat Smith", "before", "PC1", 18, 14));
        Assert.Equal("Smith_2026-10-18_before.json", Path.GetFileName(a.JsonPath));
        Assert.Equal("Smith_2026-10-18_before_2.json", Path.GetFileName(b.JsonPath));
        Assert.True(File.Exists(a.HtmlPath));
        Assert.True(File.Exists(b.HtmlPath));
    }

    [Fact]
    public void Job_number_goes_in_the_file_name()
    {
        var run = Run("Pat Smith", "after", "PC1", 18);
        run.Intake.JobNumber = " J 1042/b ";
        Assert.Equal("Smith_J-1042b_2026-10-18_after", ReportStore.StemFor(run));
        Assert.EndsWith("Smith_J-1042b_2026-10-18_comparison.html", ReportStore.ComparisonPath(@"C:\R\Smith_J-1042b_2026-10-18_after.json"));
    }

    [Fact]
    public void Shop_details_print_on_both_reports()
    {
        var before = Run("Pat Smith", "before", "PC1", 16);
        var after = Run("Pat Smith", "after", "PC1", 18);
        after.Intake.JobNumber = "J1042";
        after.Intake.TechnicianName = "Sam Tech";
        after.Intake.WorkDone = "Removed startup programs";
        var single = HtmlReport.Render(after);
        Assert.Contains("J1042", single);
        Assert.Contains("Sam Tech", single);
        Assert.Contains("Removed startup programs", single);
        var cmp = ComparisonReport.Render(Comparison.Build(before, after));
        Assert.Contains("Checked by", cmp);
        Assert.DoesNotContain("Checked by", HtmlReport.Render(before));
    }

    [Fact]
    public void Comparison_path_replaces_after()
    {
        Assert.EndsWith("Smith_2026-10-18_comparison.html", ReportStore.ComparisonPath(@"C:\R\Smith_2026-10-18_after.json"));
        Assert.EndsWith("odd_comparison.html", ReportStore.ComparisonPath(@"C:\R\odd.json"));
    }

    [Fact]
    public void List_runs_is_newest_first_and_skips_bad_files()
    {
        _store.Save(Run("A One", "before", "PC1", 10));
        _store.Save(Run("B Two", "before", "PC1", 12));
        File.WriteAllText(Path.Combine(_dir, "broken.json"), "{ not json");
        var runs = _store.ListRuns();
        Assert.Equal(["B Two", "A One"], runs.Select(r => r.Run.Intake.CustomerName));
    }

    [Fact]
    public void Missing_folder_lists_nothing() => Assert.Empty(new ReportStore(Path.Combine(_dir, "nope")).ListRuns());

    [Fact]
    public void Before_run_prefers_same_customer_then_latest_on_this_machine()
    {
        _store.Save(Run("Pat Smith", "before", "PC1", 10));
        _store.Save(Run("Lee Jones", "before", "PC1", 14));
        _store.Save(Run("Pat Smith", "before", "OTHER", 16));
        _store.Save(Run("Pat Smith", "after", "PC1", 17));

        Assert.Equal(10, _store.FindBeforeRun("pc1", "pat smith")!.Run.StartedAt.Day);
        Assert.Equal(14, _store.FindBeforeRun("PC1", "New Person")!.Run.StartedAt.Day);
        Assert.Equal(14, _store.FindBeforeRun("PC1", "")!.Run.StartedAt.Day);
        Assert.Null(_store.FindBeforeRun("PC9", "Pat Smith"));
    }

    [Fact]
    public void Write_rewrites_in_place()
    {
        var saved = _store.Save(Run("Pat Smith", "after", "PC1", 18));
        saved.Run.Intake.Complaint = "Fan noise";
        ReportStore.Write(saved.Run, saved.JsonPath);
        Assert.Equal("Fan noise", RunFile.Load(saved.JsonPath).Intake.Complaint);
        Assert.Contains("Fan noise", File.ReadAllText(saved.HtmlPath));
    }
}
