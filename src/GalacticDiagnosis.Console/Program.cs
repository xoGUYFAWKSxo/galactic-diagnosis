using System.Text;
using GalacticDiagnosis.Core;
using GalacticDiagnosis.Report;

namespace GalacticDiagnosis.Cli;

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
        Console.WriteLine($"Galactic Diagnosis {RunSession.ToolVersion}\n");

        var compareAt = Array.IndexOf(args, "--compare");
        if (compareAt >= 0)
        {
            if (compareAt + 2 >= args.Length)
            {
                Console.WriteLine("Usage: GalacticDiagnosis --compare <before.json> <after.json>");
                return 2;
            }
            var file = ReportStore.SaveComparison(RunFile.Load(args[compareAt + 1]), RunFile.Load(args[compareAt + 2]), args[compareAt + 2]);
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

        if (!CollectorRunner.IsAdmin())
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Not running as administrator. Drive health, boot time and some event logs will be missing.");
            Console.WriteLine("Right-click the tool and choose \"Run as administrator\" for a full report.\n");
            Console.ResetColor();
        }

        var intake = ReadIntake(args);
        var store = ReportStore.Default;
        var before = intake.RunType == "after" ? store.FindBeforeRun(Environment.MachineName, intake.CustomerName) : null;

        Console.WriteLine($"\nChecking {RunSession.Collectors().Length} areas...");
        var outcome = await new RunSession(store).RunAsync(intake, before, s =>
        {
            lock (Console.Out)
                Console.WriteLine($"  {s.Title,-20} {s.Verdict,-12} {s.ElapsedMs,6:0} ms");
        });
        Console.WriteLine($"\nReport saved:\n  {outcome.Saved.HtmlPath}\n  {outcome.Saved.JsonPath}");

        if (intake.RunType == "after")
        {
            if (outcome.ComparisonPath is not null)
                Console.WriteLine($"\nCompared with the before check from {before!.Run.StartedAt:yyyy-MM-dd HH:mm}:\n  {outcome.ComparisonPath}");
            else
                Console.WriteLine("\nNo before check for this customer on this PC was found in the Reports folder, so no comparison was made.");
        }

        Pause(args);
        return 0;
    }

    /// <summary>Prompts for intake, or takes it from --name / --complaint / --work / --after (plus optional --job / --tech) for scripted runs.</summary>
    static Intake ReadIntake(string[] args)
    {
        var intake = new Intake
        {
            CustomerName = Arg(args, "--name") ?? "",
            Complaint = Arg(args, "--complaint") ?? "",
            WorkDone = Arg(args, "--work") ?? "",
            JobNumber = Arg(args, "--job") ?? "",
            TechnicianName = Arg(args, "--tech") ?? "",
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

    /// <summary>Keeps the window open when launched by double-click.</summary>
    static void Pause(string[] args)
    {
        if (args.Contains("--no-prompt") || Console.IsInputRedirected) return;
        Console.WriteLine("\nPress Enter to close.");
        Console.ReadLine();
    }
}
