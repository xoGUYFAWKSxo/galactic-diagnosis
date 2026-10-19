using System.Reflection;
using System.Text;
using System.Text.Json;
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

        var isAdmin = CollectorRunner.IsAdmin();
        if (!isAdmin)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Not running as administrator. Drive health, boot time and some event logs will be missing.");
            Console.WriteLine("Right-click the tool and choose \"Run as administrator\" for a full report.\n");
            Console.ResetColor();
        }

        var intake = ReadIntake(args);

        ICollector[] collectors = [new HardwareCollector(), new DriveHealthCollector(), new BatteryCollector(), new CrashCollector(), new DiskSpaceCollector()];
        Console.WriteLine($"\nChecking {collectors.Length} areas...");
        var startedAt = DateTimeOffset.Now;
        var sections = await CollectorRunner.RunAllAsync(collectors, isAdmin, s =>
        {
            lock (Console.Out)
                Console.WriteLine($"  {s.Title,-14} {s.Verdict,-12} {s.ElapsedMs,6:0} ms");
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

        var dir = Path.Combine(AppContext.BaseDirectory, "Reports");
        Directory.CreateDirectory(dir);
        var stem = UniqueStem(dir, $"{FileSafe(LastName(intake.CustomerName))}_{startedAt:yyyy-MM-dd}_{intake.RunType}");
        var html = Path.Combine(dir, stem + ".html");
        var json = Path.Combine(dir, stem + ".json");
        await File.WriteAllTextAsync(html, HtmlReport.Render(run));
        await File.WriteAllTextAsync(json, JsonSerializer.Serialize(run, JsonOpts));

        Console.WriteLine($"\nReport saved:\n  {html}\n  {json}");
        Pause(args);
        return 0;
    }

    static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // keep "—" readable; file is never embedded in HTML
    };

    /// <summary>Prompts for intake, or takes it from --name / --complaint / --after for scripted runs.</summary>
    static Intake ReadIntake(string[] args)
    {
        var intake = new Intake
        {
            CustomerName = Arg(args, "--name") ?? "",
            Complaint = Arg(args, "--complaint") ?? "",
            RunType = args.Contains("--after") ? "after" : "before",
        };
        if (args.Contains("--no-prompt")) return intake;

        if (intake.CustomerName.Length == 0) intake.CustomerName = Ask("Customer name");
        if (intake.Complaint.Length == 0) intake.Complaint = Ask("Problem they reported");
        if (!args.Contains("--after") && !args.Contains("--before"))
            intake.RunType = Ask("Before or after service? [B/a]").StartsWith("a", StringComparison.OrdinalIgnoreCase) ? "after" : "before";
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
