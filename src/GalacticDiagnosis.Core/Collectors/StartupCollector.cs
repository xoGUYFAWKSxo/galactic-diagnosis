using Microsoft.Win32;

namespace GalacticDiagnosis.Core.Collectors;

/// <summary>
/// Programs that start with Windows: Run keys (HKLM, HKLM 32-bit, HKCU) and the two Startup folders.
/// Entries switched off in Task Manager stay in place and are marked in Explorer\StartupApproved, so those are honoured.
/// </summary>
public sealed class StartupCollector : ICollector
{
    public string Id => "startup";
    public string Title => "Startup programs";

    const string RunPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    const string Run32Path = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run";
    const string ApprovedPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved";
    const int TooMany = 12;

    public sealed record Entry(string Name, string Source, string Command, bool Enabled);

    public SectionResult Collect(CancellationToken ct)
    {
        var r = new SectionResult { Id = Id, Title = Title };
        var entries = new List<Entry>();
        entries.AddRange(FromRunKey(Registry.LocalMachine, RunPath, "Run", "All users"));
        entries.AddRange(FromRunKey(Registry.LocalMachine, Run32Path, "Run32", "All users"));
        entries.AddRange(FromRunKey(Registry.CurrentUser, RunPath, "Run", "This user"));
        entries.AddRange(FromFolder(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), Registry.LocalMachine, "All users"));
        entries.AddRange(FromFolder(Environment.GetFolderPath(Environment.SpecialFolder.Startup), Registry.CurrentUser, "This user"));

        var windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var enabled = entries.Where(e => e.Enabled).ToList();
        var flagged = new List<string>();
        int missing = 0;

        foreach (var e in enabled.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();
            var exe = ExtractExe(e.Command);
            if (exe is null || !File.Exists(exe))
            {
                missing++;
                r.Items.Add(new(e.Name, "Program file not found", Verdict.Info, "A leftover entry from something that was uninstalled. Harmless, but can be removed."));
                continue;
            }

            var inWindows = exe.StartsWith(windowsDir, StringComparison.OrdinalIgnoreCase);
            // Windows' own files are catalog-signed, which this embedded-signature check can't see.
            var signer = inWindows ? null : Authenticode.Signer(exe);
            var concerns = inWindows ? [] : Concerns(exe, signer);
            var v = concerns.Count > 0 ? Verdict.Warning : Verdict.Info;
            if (concerns.Count > 0) flagged.Add(e.Name);
            r.Items.Add(new(e.Name, inWindows ? "Part of Windows or a device driver" : signer is null ? "Not signed by its maker" : $"Signed by {signer}", v,
                (concerns.Count > 0 ? string.Join(" ", concerns) + " " : "") + exe));
        }

        var off = entries.Count - enabled.Count;
        if (off > 0) r.Items.Add(new("Turned off", $"{off} more startup entr{(off == 1 ? "y is" : "ies are")} already switched off"));

        var running = enabled.Count - missing;
        r.Metrics["startup_enabled"] = running;
        r.Metrics["startup_flagged"] = flagged.Count;

        var sentences = new List<string>();
        if (running >= TooMany)
            sentences.Add($"{running} programs start automatically with Windows. Turning off the ones that aren't needed will make startup noticeably faster.");
        if (flagged.Count > 0)
            sentences.Add($"{Count(flagged.Count, "startup program")} ({string.Join(", ", flagged.Take(3))}{(flagged.Count > 3 ? ", …" : "")}) {(flagged.Count == 1 ? "isn't" : "aren't")} signed by a known maker or {(flagged.Count == 1 ? "runs" : "run")} from an unusual folder. Worth confirming {(flagged.Count == 1 ? "it's" : "they're")} wanted.");

        r.Verdict = sentences.Count > 0 ? Verdict.Warning : Verdict.Good;
        r.Summary = sentences.Count > 0
            ? string.Join(" ", sentences)
            : running == 0 ? "No programs start automatically with Windows." : $"{Count(running, "program")} start{(running == 1 ? "s" : "")} with Windows, which is a reasonable number, and nothing looks suspicious.";
        return r;
    }

    static string Count(int n, string noun) => $"{n} {noun}{(n == 1 ? "" : "s")}";

    /// <summary>Unsigned code is common in legitimate apps, so it's only flagged with context; Temp is never a normal home.</summary>
    public static List<string> Concerns(string exe, string? signer)
    {
        var list = new List<string>();
        var temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd('\\');
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (exe.StartsWith(temp + "\\", StringComparison.OrdinalIgnoreCase) || exe.Contains(@"\Temp\", StringComparison.OrdinalIgnoreCase))
            list.Add("Runs from a temporary folder, which is unusual for a real program.");
        else if (signer is null && (exe.StartsWith(local, StringComparison.OrdinalIgnoreCase) || exe.StartsWith(roaming, StringComparison.OrdinalIgnoreCase)))
            list.Add("Unsigned and runs from the user's AppData folder.");
        else if (signer is null)
            list.Add("Not signed by its maker.");
        return list;
    }

    /// <summary>Pulls the executable path out of a Run-key command line.</summary>
    public static string? ExtractExe(string command)
    {
        var c = Environment.ExpandEnvironmentVariables(command.Trim());
        if (c.Length == 0) return null;
        if (c[0] == '"')
        {
            var end = c.IndexOf('"', 1);
            return end > 1 ? c[1..end] : null;
        }
        // Unquoted: paths may contain spaces, so cut after the first known executable extension.
        foreach (var ext in new[] { ".exe", ".com", ".bat", ".cmd", ".lnk" })
        {
            var i = c.IndexOf(ext + " ", StringComparison.OrdinalIgnoreCase);
            if (i < 0 && c.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) i = c.Length - ext.Length;
            if (i >= 0) return c[..(i + ext.Length)];
        }
        return c.Split(' ')[0];
    }

    static IEnumerable<Entry> FromRunKey(RegistryKey hive, string path, string approvedName, string scope)
    {
        using var key = hive.OpenSubKey(path);
        if (key is null) yield break;
        var approved = Approved(hive, approvedName);
        foreach (var name in key.GetValueNames().Where(n => n.Length > 0))
        {
            if (key.GetValue(name) is not string cmd || string.IsNullOrWhiteSpace(cmd)) continue;
            yield return new Entry(name, scope, cmd, IsEnabled(approved, name));
        }
    }

    static IEnumerable<Entry> FromFolder(string dir, RegistryKey hive, string scope)
    {
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) yield break;
        var approved = Approved(hive, "StartupFolder");
        foreach (var f in Directory.GetFiles(dir))
        {
            var file = Path.GetFileName(f);
            if (file.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
            var target = f.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ? ShortcutTarget(f) ?? f : f;
            yield return new Entry(Path.GetFileNameWithoutExtension(f), scope, $"\"{target}\"", IsEnabled(approved, file));
        }
    }

    static Dictionary<string, byte[]> Approved(RegistryKey hive, string sub)
    {
        var map = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        using var key = hive.OpenSubKey($@"{ApprovedPath}\{sub}");
        if (key is null) return map;
        foreach (var n in key.GetValueNames())
            if (key.GetValue(n) is byte[] { Length: > 0 } b) map[n] = b;
        return map;
    }

    /// <summary>StartupApproved's first byte is even (2, 6) when enabled and odd (3, 7) when switched off. No entry means enabled.</summary>
    public static bool IsEnabled(IReadOnlyDictionary<string, byte[]> approved, string name) =>
        !approved.TryGetValue(name, out var b) || b[0] % 2 == 0;

    static string? ShortcutTarget(string lnk)
    {
        try
        {
            var type = Type.GetTypeFromProgID("WScript.Shell");
            if (type is null) return null;
            dynamic shell = Activator.CreateInstance(type)!;
            var target = (string)shell.CreateShortcut(lnk).TargetPath;
            return string.IsNullOrWhiteSpace(target) ? null : target;
        }
        catch { return null; }
    }
}
