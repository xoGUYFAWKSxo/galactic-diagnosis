using System.Globalization;
using Microsoft.Win32;

namespace GalacticDiagnosis.Core.Collectors;

/// <summary>How recently Windows Update installed something, from Win32_QuickFixEngineering. Rough, but catches PCs months behind.</summary>
public sealed class UpdatesCollector : ICollector
{
    public string Id => "updates";
    public string Title => "Windows updates";

    public SectionResult Collect(CancellationToken ct)
    {
        var r = new SectionResult { Id = Id, Title = Title };

        using (var cv = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
        {
            var display = cv?.GetValue("DisplayVersion") as string;
            var build = cv?.GetValue("CurrentBuild") as string;
            var ubr = cv?.GetValue("UBR") is int u ? $".{u}" : "";
            if (build is not null)
                r.Items.Add(new("Windows version", (display is null ? "" : $"{display}, ") + $"build {build}{ubr}"));
        }

        var fixes = Wmi.Query("SELECT HotFixID, Description, InstalledOn FROM Win32_QuickFixEngineering")
            .Select(f => (Id: f.Str("HotFixID"), Desc: f.Str("Description"), Date: ParseQfeDate(f.Str("InstalledOn"))))
            .Where(f => f.Date is not null)
            .OrderByDescending(f => f.Date)
            .ToList();
        ct.ThrowIfCancellationRequested();

        var pendingRestart = RestartPending();
        if (fixes.Count == 0)
        {
            r.Verdict = Verdict.Unavailable;
            r.Summary = "Windows didn't report any installed updates with dates, so update status couldn't be checked.";
            return r;
        }

        var latest = fixes[0];
        var days = (int)(DateTime.Today - latest.Date!.Value.Date).TotalDays;
        r.Metrics["days_since_update"] = days;
        r.Verdict = days switch { <= 45 => Verdict.Good, <= 90 => Verdict.Warning, _ => Verdict.Bad };
        r.Items.Add(new("Last update installed", $"{latest.Date:yyyy-MM-dd} ({Ago(days)}), {latest.Id}", r.Verdict, latest.Desc));
        r.Items.Add(new("Updates listed", $"{fixes.Count}"));
        if (pendingRestart)
            r.Items.Add(new("Restart needed", "An installed update is waiting for a restart", Verdict.Warning));

        r.Summary = r.Verdict switch
        {
            Verdict.Good => "Windows is installing updates regularly.",
            Verdict.Warning => $"Windows hasn't installed an update in {days} days. It may be falling behind on security fixes.",
            _ => $"Windows hasn't installed an update in {days} days, so it's missing months of security fixes. Windows Update needs attention.",
        };
        if (pendingRestart)
        {
            r.Summary += " An update is waiting for the PC to restart.";
            r.Verdict = Verdicts.Worst([r.Verdict, Verdict.Warning]);
        }
        return r;
    }

    static string Ago(int days) => days switch { 0 => "today", 1 => "yesterday", _ => $"{days} days ago" };

    /// <summary>InstalledOn is a string: usually M/d/yyyy, sometimes a hex FILETIME on older systems.</summary>
    public static DateTime? ParseQfeDate(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        if (DateTime.TryParseExact(s, ["M/d/yyyy", "MM/dd/yyyy", "yyyyMMdd"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            return d;
        if (long.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var ft) && ft > 0)
            try { return DateTime.FromFileTimeUtc(ft).ToLocalTime(); } catch (ArgumentOutOfRangeException) { }
        return DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out d) ? d : null;
    }

    static bool RestartPending()
    {
        using var wu = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired");
        if (wu is not null) return true;
        using var cbs = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending");
        return cbs is not null;
    }
}
