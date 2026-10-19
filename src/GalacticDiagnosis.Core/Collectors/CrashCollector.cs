using System.Diagnostics.Eventing.Reader;
using System.Globalization;

namespace GalacticDiagnosis.Core.Collectors;

/// <summary>Blue screens, unexpected shutdowns and app crashes over the last 30 days, from the event log and minidumps.</summary>
public sealed class CrashCollector : ICollector
{
    public string Id => "crashes";
    public string Title => "Crashes";
    public TimeSpan Timeout => TimeSpan.FromSeconds(30);

    const int Days = 30;
    const long WindowMs = Days * 24L * 60 * 60 * 1000;

    public SectionResult Collect(CancellationToken ct)
    {
        var r = new SectionResult { Id = Id, Title = Title };
        var since = DateTime.Now.AddDays(-Days);

        // System log: 41 = Kernel-Power unexpected restart, 6008 = previous shutdown was unexpected, 1001 = BugCheck (blue screen).
        var system = Read("System", $"*[System[(EventID=41 or EventID=6008 or EventID=1001) and TimeCreated[timediff(@SystemTime) <= {WindowMs}]]]", ct);
        var bugchecks = system
            .Where(e => e.Id == 1001 && e.Provider.Contains("WER-SystemErrorReporting", StringComparison.OrdinalIgnoreCase))
            .Select(e => (e.Time, Code: ParseBugcheck(e.Props.FirstOrDefault())))
            .ToList();
        var kernelPower = system.Where(e => e.Id == 41 && e.Provider.Contains("Kernel-Power", StringComparison.OrdinalIgnoreCase)).ToList();
        var unexpected = system.Count(e => e.Id == 6008);
        ct.ThrowIfCancellationRequested();

        // Application log: 1000 = Application Error; property 0 is the program's file name.
        var appCrashes = Read("Application", $"*[System[Provider[@Name='Application Error'] and EventID=1000 and TimeCreated[timediff(@SystemTime) <= {WindowMs}]]]", ct)
            .GroupBy(e => e.Props.FirstOrDefault() ?? "unknown", StringComparer.OrdinalIgnoreCase)
            .Select(g => (App: g.Key, Count: g.Count()))
            .OrderByDescending(g => g.Count)
            .ToList();

        var (dumps, dumpError) = RecentMinidumps(since);

        // A blue screen logs both a 41 (with its bugcheck code) and a 1001; count each crash once.
        var bsodCount = Math.Max(bugchecks.Count, kernelPower.Count(e => e.Props.FirstOrDefault() is { } c && c != "0"));
        bsodCount = Math.Max(bsodCount, dumps.Count);
        // 41s without a bugcheck code are power cuts, hard resets or freezes.
        var hardResets = Math.Max(kernelPower.Count(e => e.Props.FirstOrDefault() is null or "0"), unexpected - bsodCount);
        hardResets = Math.Max(0, hardResets);
        var appTotal = appCrashes.Sum(a => a.Count);

        r.Metrics["bsod_30d"] = bsodCount;
        r.Metrics["unexpected_shutdowns_30d"] = hardResets;
        r.Metrics["app_crashes_30d"] = appTotal;

        var sentences = new List<string>();

        var bsodVerdict = bsodCount switch { 0 => Verdict.Good, < 3 => Verdict.Warning, _ => Verdict.Bad };
        var codes = bugchecks.Where(b => b.Code is not null).GroupBy(b => b.Code!.Value).OrderByDescending(g => g.Count()).ToList();
        var codeText = codes.Count > 0 ? string.Join(", ", codes.Select(g => $"{BugcheckName(g.Key)}{(g.Count() > 1 ? $" ×{g.Count()}" : "")}")) : null;
        r.Items.Add(new("Blue screens", bsodCount == 0 ? "None" : $"{bsodCount}" + (bugchecks.Count > 0 ? $", last on {bugchecks.Max(b => b.Time):yyyy-MM-dd}" : ""),
            bsodVerdict, codeText));
        if (bsodCount > 0)
        {
            var s = $"Windows crashed with a blue screen {Times(bsodCount)} in the last {Days} days.";
            if (codes.Count > 0 && BugcheckHint(codes[0].Key) is { } hint) s += $" The most common error ({BugcheckName(codes[0].Key)}) {hint}";
            sentences.Add(s);
        }

        var resetVerdict = hardResets switch { 0 => Verdict.Good, < 3 => Verdict.Info, _ => Verdict.Warning };
        r.Items.Add(new("Unexpected shutdowns", hardResets == 0 ? "None" : $"{hardResets}", resetVerdict,
            hardResets > 0 ? "Power cuts, holding the power button, or the PC freezing and restarting." : null));
        if (hardResets >= 3)
            sentences.Add($"The PC shut off without warning {Times(hardResets)} in the last {Days} days. That points to freezing, overheating, or a power problem, unless the power was cut on purpose.");

        var topApp = appCrashes.FirstOrDefault();
        var appVerdict = topApp.Count >= 5 ? Verdict.Warning : Verdict.Good;
        r.Items.Add(new("Program crashes", appTotal == 0 ? "None" : $"{appTotal} across {appCrashes.Count} program{(appCrashes.Count == 1 ? "" : "s")}", appVerdict));
        foreach (var (app, count) in appCrashes.Take(5))
            r.Items.Add(new($"  {app}", $"{count} crash{(count == 1 ? "" : "es")}", count >= 5 ? Verdict.Warning : Verdict.Info));
        if (topApp.Count >= 5)
            sentences.Add($"{FriendlyApp(topApp.App)} crashed {Times(topApp.Count)}. Reinstalling or updating it usually fixes this.");

        if (dumpError is not null)
            r.Items.Add(new("Crash dump files", "Need administrator rights to read", Verdict.Unavailable));
        else
            r.Items.Add(new("Crash dump files", dumps.Count == 0 ? "None" : $"{dumps.Count} in C:\\Windows\\Minidump", Verdict.Info,
                dumps.Count == 0 ? null : string.Join(", ", dumps.Take(5).Select(f => f.Name))));

        r.Verdict = Verdicts.Worst([bsodVerdict, resetVerdict, appVerdict]);
        r.Summary = sentences.Count > 0
            ? string.Join(" ", sentences)
            : $"No blue screens or repeated crashes in the last {Days} days.";
        return r;
    }

    sealed record Ev(int Id, string Provider, DateTime Time, List<string?> Props);

    static List<Ev> Read(string log, string xpath, CancellationToken ct, int max = 2000)
    {
        var list = new List<Ev>();
        using var reader = new EventLogReader(new EventLogQuery(log, PathType.LogName, xpath) { ReverseDirection = true });
        for (EventRecord? e; list.Count < max && (e = reader.ReadEvent()) is not null;)
        {
            using (e)
            {
                ct.ThrowIfCancellationRequested();
                list.Add(new Ev(e.Id, e.ProviderName ?? "", e.TimeCreated ?? DateTime.MinValue,
                    e.Properties.Select(p => p.Value?.ToString()).ToList()));
            }
        }
        return list;
    }

    static (List<FileInfo> Files, string? Error) RecentMinidumps(DateTime since)
    {
        var dir = new DirectoryInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Minidump"));
        try
        {
            if (!dir.Exists) return ([], null);
            return (dir.GetFiles("*.dmp").Where(f => f.LastWriteTime >= since).OrderByDescending(f => f.LastWriteTime).ToList(), null);
        }
        catch (UnauthorizedAccessException ex) { return ([], ex.Message); }
    }

    /// <summary>Event 1001's first property looks like "0x0000009f (0x..., 0x..., ...)".</summary>
    public static uint? ParseBugcheck(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var hex = s.Trim().Split(' ', '(')[0];
        if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) hex = hex[2..];
        return uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code) ? code : null;
    }

    static readonly Dictionary<uint, (string Name, string? Hint)> Bugchecks = new()
    {
        [0x0A] = ("IRQL_NOT_LESS_OR_EQUAL", "usually means a faulty driver or memory."),
        [0x19] = ("BAD_POOL_HEADER", "usually means a faulty driver."),
        [0x1A] = ("MEMORY_MANAGEMENT", "often points to faulty memory (RAM). A memory test is the next step."),
        [0x1E] = ("KMODE_EXCEPTION_NOT_HANDLED", "usually means a faulty driver."),
        [0x3B] = ("SYSTEM_SERVICE_EXCEPTION", "usually means a faulty driver."),
        [0x50] = ("PAGE_FAULT_IN_NONPAGED_AREA", "often points to faulty memory or a driver."),
        [0x7A] = ("KERNEL_DATA_INPAGE_ERROR", "often points to a failing drive or loose drive cable."),
        [0x7E] = ("SYSTEM_THREAD_EXCEPTION_NOT_HANDLED", "usually means a faulty driver."),
        [0x9F] = ("DRIVER_POWER_STATE_FAILURE", "usually means a driver that doesn't handle sleep properly."),
        [0xC2] = ("BAD_POOL_CALLER", "usually means a faulty driver."),
        [0xD1] = ("DRIVER_IRQL_NOT_LESS_OR_EQUAL", "usually means a faulty driver, often network or graphics."),
        [0xEF] = ("CRITICAL_PROCESS_DIED", "often points to damaged Windows files or a failing drive."),
        [0xF4] = ("CRITICAL_OBJECT_TERMINATION", "often points to a failing drive."),
        [0x101] = ("CLOCK_WATCHDOG_TIMEOUT", "often points to a processor problem or unstable overclock."),
        [0x116] = ("VIDEO_TDR_FAILURE", "points to the graphics driver or graphics card."),
        [0x117] = ("VIDEO_TDR_TIMEOUT_DETECTED", "points to the graphics driver or graphics card."),
        [0x124] = ("WHEA_UNCORRECTABLE_ERROR", "is a hardware error, often the processor, memory, or an unstable overclock."),
        [0x133] = ("DPC_WATCHDOG_VIOLATION", "usually means a faulty driver or drive firmware."),
        [0x139] = ("KERNEL_SECURITY_CHECK_FAILURE", "usually means a faulty driver or memory."),
        [0x13A] = ("KERNEL_MODE_HEAP_CORRUPTION", "usually means a faulty driver."),
        [0x1E0] = ("ATTEMPTED_SWITCH_FROM_DPC", "usually means a faulty driver."),
    };

    public static string BugcheckName(uint code) => Bugchecks.TryGetValue(code, out var b) ? b.Name : $"0x{code:X}";
    static string? BugcheckHint(uint code) => Bugchecks.TryGetValue(code, out var b) ? b.Hint : null;

    static string Times(int n) => n == 1 ? "once" : n == 2 ? "twice" : $"{n} times";

    static string FriendlyApp(string exe) => exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? $"The program {exe[..^4]}" : exe;
}
