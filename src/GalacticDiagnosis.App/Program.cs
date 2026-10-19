using System.Reflection;
using System.Text;
using GalacticDiagnosis.Core;
using GalacticDiagnosis.Core.Collectors;
using GalacticDiagnosis.Report;

namespace GalacticDiagnosis.App;

static class Program
{
    static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        try
        {
            return await RunAsync(args);
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\nSomething went wrong: {ex.Message}");
            Console.ResetColor();
            Pause(args);
            return 1;
        }
    }

    static async Task<int> RunAsync(string[] args)
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "dev";
        Console.WriteLine($"Galactic Diagnosis {version}\n");

        var compareAt = Array.IndexOf(args, "--compare");
        if (compareAt >= 0)
        {
            if (compareAt + 2 >= args.Length)
            {
                Console.WriteLine("Usage: GalacticDiagnosis --compare <before.json> <after.json>");
                return 2;
            }
            var file = SaveComparison(RunFile.Load(args[compareAt + 1]), RunFile.Load(args[compareAt + 2]), args[compareAt + 2]);
            Console.WriteLine($"Comparison saved:\n  {file}");
            return 0;
        }

        // Rebuilds the HTML for a saved run, e.g. after a report wording change.
        var renderAt = Array.IndexOf(args, "--render");
        if (renderAt >= 0)
        {
            if (renderAt + 1 >= args.Length)
            {
                Console.WriteLine("Usage: GalacticDiagnosis --render <run.json>");
                return 2;
            }
            var src = args[renderAt + 1];
            var page = Path.ChangeExtension(Path.GetFullPath(src), ".html");
            File.WriteAllText(page, HtmlReport.Render(RunFile.Load(src)));
            Console.WriteLine($"Report saved:\n  {page}");
            return 0;
        }

        var isAdmin = CollectorRunner.IsAdmin();
        if (!isAdmin)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Not running as administrator. Drive health, boot time and some event logs will be missing.");
            Console.WriteLine("Right-click the tool and choose \"Run as administrator\" for a full report.\n");
            Console.ResetColor();
        }

        var intake = ReadIntake(args);
        var dir = Path.Combine(AppContext.BaseDirectory, "Reports");
        var before = intake.RunType == "after" ? FindBeforeRun(dir, intake.CustomerName) : null;
        if (before is not null && intake.Complaint.Length == 0) intake.Complaint = before.Intake.Complaint;

        ICollector[] collectors =
        [
            new HardwareCollector(), new DriveHealthCollector(), new BatteryCollector(), new CrashCollector(),
            new BootTimeCollector(), new StartupCollector(), new DiskSpaceCollector(),
            new UpdatesCollector(), new AntivirusCollector(), new ActivationCollector(),
        ];
        Console.WriteLine($"\nChecking {collectors.Length} areas...");
        var startedAt = DateTimeOffset.Now;
        var sections = await CollectorRunner.RunAllAsync(collectors, isAdmin, s =>
        {
            lock (Console.Out)
                Console.WriteLine($"  {s.Title,-20} {s.Verdict,-12} {s.ElapsedMs,6:0} ms");
        });

        var run = new DiagnosticRun
        {
            Intake = intake,
            MachineName = Environment.MachineName,
            StartedAt = startedAt,
            RanAsAdmin = isAdmin,
            ToolVersion = version,
            Sections = sections,
        };

        Directory.CreateDirectory(dir);
        var stem = UniqueStem(dir, $"{FileSafe(LastName(intake.CustomerName))}_{startedAt:yyyy-MM-dd}_{intake.RunType}");
        var html = Path.Combine(dir, stem + ".html");
        var json = Path.Combine(dir, stem + ".json");
        await File.WriteAllTextAsync(html, HtmlReport.Render(run));
        await File.WriteAllTextAsync(json, RunFile.Serialize(run));
        Console.WriteLine($"\nReport saved:\n  {html}\n  {json}");

        if (intake.RunType == "after")
        {
            if (before is not null)
                Console.WriteLine($"\nCompared with the before check from {before.StartedAt:yyyy-MM-dd HH:mm}:\n  {SaveComparison(before, run, json)}");
            else
                Console.WriteLine("\nNo before check for this customer on this PC was found in the Reports folder, so no comparison was made.");
        }

        Pause(args);
        return 0;
    }

    /// <summary>Writes the comparison next to the after file: Smith_2026-10-18_after.json → Smith_2026-10-18_comparison.html.</summary>
    static string SaveComparison(DiagnosticRun before, DiagnosticRun after, string afterPath)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(afterPath))!;
        var stem = Path.GetFileNameWithoutExtension(afterPath);
        stem = stem.Contains("_after") ? stem.Replace("_after", "_comparison") : stem + "_comparison";
        var path = Path.Combine(dir, stem + ".html");
        File.WriteAllText(path, ComparisonReport.Render(Comparison.Build(before, after)));
        return path;
    }

    /// <summary>Latest "before" run from this PC, preferring one for the same customer.</summary>
    static DiagnosticRun? FindBeforeRun(string dir, string customer)
    {
        if (!Directory.Exists(dir)) return null;
        var runs = new List<DiagnosticRun>();
        foreach (var f in Directory.GetFiles(dir, "*.json"))
        {
            try { runs.Add(RunFile.Load(f)); }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) { }
        }
        return runs
            .Where(r => r.Intake.RunType == "before" && r.MachineName.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(r => customer.Length > 0 && r.Intake.CustomerName.Equals(customer, StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(r => r.StartedAt)
            .FirstOrDefault();
    }

    /// <summary>Prompts for intake, or takes it from --name / --complaint / --work / --after for scripted runs.</summary>
    static Intake ReadIntake(string[] args)
    {
        var intake = new Intake
        {
            CustomerName = Arg(args, "--name") ?? "",
            Complaint = Arg(args, "--complaint") ?? "",
            WorkDone = Arg(args, "--work") ?? "",
            RunType = args.Contains("--after") ? "after" : "before",
        };
        if (args.Contains("--no-prompt")) return intake;

        if (intake.CustomerName.Length == 0) intake.CustomerName = Ask("Customer name");
        if (!args.Contains("--after") && !args.Contains("--before"))
            intake.RunType = Ask("Before or after service? [B/a]").StartsWith("a", StringComparison.OrdinalIgnoreCase) ? "after" : "before";
        // An after run takes the complaint from the matching before run, so ask what was done instead.
        if (intake.RunType == "before" && intake.Complaint.Length == 0) intake.Complaint = Ask("Problem they reported");
        if (intake.RunType == "after" && intake.WorkDone.Length == 0) intake.WorkDone = Ask("What was done");
        return intake;
    }

    static string Ask(string prompt)
    {
        Console.Write($"{prompt}: ");
        return Console.ReadLine()?.Trim() ?? "";
    }

    static string? Arg(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    static string LastName(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 ? parts[^1] : "Customer";
    }

    static string FileSafe(string s)
    {
        var bad = Path.GetInvalidFileNameChars();
        var clean = new string(s.Where(c => !bad.Contains(c)).ToArray());
        return clean.Length > 0 ? clean : "Customer";
    }

    /// <summary>Avoids overwriting an earlier report from the same day.</summary>
    static string UniqueStem(string dir, string stem)
    {
        var candidate = stem;
        for (var n = 2; File.Exists(Path.Combine(dir, candidate + ".html")); n++) candidate = $"{stem}_{n}";
        return candidate;
    }

    /// <summary>Keeps the window open when launched by double-click.</summary>
    static void Pause(string[] args)
    {
        if (args.Contains("--no-prompt") || Console.IsInputRedirected) return;
        Console.WriteLine("\nPress Enter to close.");
        Console.ReadLine();
    }
}
