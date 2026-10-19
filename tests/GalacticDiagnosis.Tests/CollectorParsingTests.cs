using System.Xml.Linq;
using GalacticDiagnosis.Core;
using GalacticDiagnosis.Core.Collectors;

namespace GalacticDiagnosis.Tests;

public class BatteryTests
{
    // Shape of `powercfg /batteryreport /xml` output, trimmed to the fields we read.
    static XDocument Report(string batteries) => XDocument.Parse($"""
        <?xml version="1.0" encoding="utf-8"?>
        <BatteryReport xmlns="http://schemas.microsoft.com/battery/2012">
          <ReportInformation><ReportVersion>1.0</ReportVersion></ReportInformation>
          <Batteries>{batteries}</Batteries>
        </BatteryReport>
        """);

    static string Battery(long design, long full, long cycles = 0) => $"""
        <Battery>
          <Id>DELL 1VX1H93</Id><Manufacturer>SMP</Manufacturer><Chemistry>LiP</Chemistry>
          <DesignCapacity>{design}</DesignCapacity><FullChargeCapacity>{full}</FullChargeCapacity><CycleCount>{cycles}</CycleCount>
        </Battery>
        """;

    [Theory]
    [InlineData(54000, 50000, Verdict.Good)]
    [InlineData(54000, 34000, Verdict.Warning)]
    [InlineData(54000, 20000, Verdict.Bad)]
    public void Health_thresholds(long design, long full, Verdict expected)
    {
        var r = BatteryCollector.FromReport(Report(Battery(design, full, 312)));
        Assert.Equal(expected, r.Verdict);
        Assert.Equal(Math.Round(full * 100.0 / design, 1), r.Metrics["battery.health_pct"]);
        Assert.Equal(312, r.Metrics["battery.cycles"]);
        Assert.Contains("SMP DELL 1VX1H93", r.Items[0].Label);
    }

    [Fact]
    public void Missing_capacity_is_reported_not_guessed()
    {
        var r = BatteryCollector.FromReport(Report(Battery(0, 0)));
        Assert.Equal(Verdict.Info, r.Verdict);
        Assert.False(r.Metrics.ContainsKey("battery.health_pct"));
        Assert.False(r.Metrics.ContainsKey("battery.cycles"));
    }

    [Fact]
    public void Two_batteries_get_separate_metrics()
    {
        var r = BatteryCollector.FromReport(Report(Battery(40000, 39000) + Battery(20000, 9000)));
        Assert.Equal(Verdict.Bad, r.Verdict);
        Assert.True(r.Metrics.ContainsKey("battery1.health_pct"));
        Assert.True(r.Metrics.ContainsKey("battery2.health_pct"));
    }

    [Fact]
    public void No_battery_elements_is_unavailable()
    {
        Assert.Equal(Verdict.Unavailable, BatteryCollector.FromReport(Report("")).Verdict);
    }
}

public class BugcheckTests
{
    [Theory]
    [InlineData("0x0000009f (0x0000000000000003, 0xffffb00b, 0x0, 0x0)", 0x9Fu)]
    [InlineData("0x00000124 (0x0, 0x1, 0x2, 0x3)", 0x124u)]
    [InlineData("0x1a", 0x1Au)]
    public void Parses_code_from_event_text(string text, uint expected) =>
        Assert.Equal(expected, CrashCollector.ParseBugcheck(text));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a code")]
    public void Garbage_is_null(string? text) => Assert.Null(CrashCollector.ParseBugcheck(text));

    [Fact]
    public void Names_known_and_unknown_codes()
    {
        Assert.Equal("MEMORY_MANAGEMENT", CrashCollector.BugcheckName(0x1A));
        Assert.Equal("0xDEAD", CrashCollector.BugcheckName(0xDEAD));
    }
}
