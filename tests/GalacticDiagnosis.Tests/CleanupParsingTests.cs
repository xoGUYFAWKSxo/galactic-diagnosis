using GalacticDiagnosis.Core;
using GalacticDiagnosis.Core.Collectors;

namespace GalacticDiagnosis.Tests;

public class BootTimeTests
{
    // Trimmed Diagnostics-Performance event 100; real events carry ~40 Data fields in this order-independent form.
    const string Event = """
        <Event xmlns="http://schemas.microsoft.com/win/2004/08/events/event">
          <System><EventID>100</EventID></System>
          <EventData>
            <Data Name="BootTsVersion">2</Data>
            <Data Name="BootTime">94213</Data>
            <Data Name="MainPathBootTime">31002</Data>
            <Data Name="BootPostBootTime">63211</Data>
          </EventData>
        </Event>
        """;

    [Fact]
    public void Reads_fields_by_name()
    {
        var b = BootTimeCollector.ParseBootEvent(Event, new DateTime(2026, 10, 1))!;
        Assert.Equal(94213, b.TotalMs);
        Assert.Equal(31002, b.MainPathMs);
    }

    [Fact]
    public void Missing_boot_time_is_null() =>
        Assert.Null(BootTimeCollector.ParseBootEvent(Event.Replace("BootTime\"", "Other\""), DateTime.Now));
}

public class StartupTests
{
    [Theory]
    [InlineData("\"C:\\Program Files\\App\\app.exe\" --min", @"C:\Program Files\App\app.exe")]
    [InlineData(@"C:\Program Files\Docker\Docker\Docker Desktop.exe", @"C:\Program Files\Docker\Docker\Docker Desktop.exe")]
    [InlineData(@"C:\Program Files\Proton\VPN\ProtonVPN.Launcher.exe ""----ms-protocol:x""", @"C:\Program Files\Proton\VPN\ProtonVPN.Launcher.exe")]
    [InlineData(@"C:\ProgramData\SquirrelMachineInstalls\Discord.exe --checkInstall", @"C:\ProgramData\SquirrelMachineInstalls\Discord.exe")]
    public void Extracts_exe(string command, string expected) => Assert.Equal(expected, StartupCollector.ExtractExe(command));

    [Fact]
    public void Expands_environment_variables() =>
        Assert.Equal(Environment.GetFolderPath(Environment.SpecialFolder.Windows) + @"\system32\x.exe",
            StartupCollector.ExtractExe(@"%windir%\system32\x.exe"), ignoreCase: true);

    [Theory]
    [InlineData(2, true)]
    [InlineData(6, true)]
    [InlineData(3, false)]
    [InlineData(7, false)]
    public void Approved_byte_decides_enabled(byte first, bool expected)
    {
        var approved = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase) { ["Steam"] = [first, 0, 0, 0] };
        Assert.Equal(expected, StartupCollector.IsEnabled(approved, "steam"));
        Assert.True(StartupCollector.IsEnabled(approved, "NotListed"));
    }

    [Fact]
    public void Temp_folder_is_flagged_even_when_signed()
    {
        var exe = Path.Combine(Path.GetTempPath(), "thing.exe");
        Assert.NotEmpty(StartupCollector.Concerns(exe, "Some Corp"));
        Assert.Empty(StartupCollector.Concerns(@"C:\Program Files\App\app.exe", "Some Corp"));
        Assert.NotEmpty(StartupCollector.Concerns(@"C:\Program Files\App\app.exe", null));
    }
}

public class AntivirusTests
{
    [Theory]
    [InlineData(0x061100, true, true)]   // Defender, on, current
    [InlineData(0x060100, false, true)]  // off
    [InlineData(0x061110, true, false)]  // on, definitions out of date
    [InlineData(0x041000, true, true)]
    public void Decodes_product_state(long state, bool enabled, bool upToDate)
    {
        var p = AntivirusCollector.Decode("AV", state);
        Assert.Equal(enabled, p.Enabled);
        Assert.Equal(upToDate, p.UpToDate);
    }

    [Fact]
    public void Verdicts()
    {
        Assert.Equal(Verdict.Bad, AntivirusCollector.Evaluate([]).Verdict);
        Assert.Equal(Verdict.Bad, AntivirusCollector.Evaluate([new("Defender", false, true)]).Verdict);
        Assert.Equal(Verdict.Good, AntivirusCollector.Evaluate([new("Defender", false, true), new("Norton", true, true)]).Verdict);
        Assert.Equal(Verdict.Warning, AntivirusCollector.Evaluate([new("Norton", true, false)]).Verdict);
        Assert.Equal(Verdict.Warning, AntivirusCollector.Evaluate([new("Norton", true, true), new("McAfee", true, true)]).Verdict);
    }
}

public class ActivationTests
{
    [Theory]
    [InlineData("Windows(R), Core edition", "Windows Home")]
    [InlineData("Windows(R), CoreSingleLanguage edition", "Windows Home Single Language")]
    [InlineData("Windows(R), Professional edition", "Windows Pro")]
    [InlineData("Windows(R), Enterprise edition", "Windows Enterprise")]
    [InlineData(null, "Windows")]
    public void Maps_license_names_to_retail_names(string? name, string expected) =>
        Assert.Equal(expected, ActivationCollector.EditionName(name));
}

public class UpdatesTests
{
    [Theory]
    [InlineData("10/13/2026", 2026, 10, 13)]
    [InlineData("3/5/2025", 2025, 3, 5)]
    [InlineData("20250305", 2025, 3, 5)]
    public void Parses_qfe_dates(string s, int y, int m, int d) =>
        Assert.Equal(new DateTime(y, m, d), UpdatesCollector.ParseQfeDate(s));

    [Fact]
    public void Parses_hex_filetime()
    {
        var ft = new DateTime(2024, 6, 1, 12, 0, 0, DateTimeKind.Utc).ToFileTimeUtc();
        Assert.Equal(new DateTime(2024, 6, 1), UpdatesCollector.ParseQfeDate(ft.ToString("X"))!.Value.ToUniversalTime().Date);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("garbage")]
    public void Bad_input_is_null(string? s) => Assert.Null(UpdatesCollector.ParseQfeDate(s));
}
