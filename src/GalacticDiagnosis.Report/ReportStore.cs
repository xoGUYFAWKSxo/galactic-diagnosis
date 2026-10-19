using GalacticDiagnosis.Core;

namespace GalacticDiagnosis.Report;

/// <summary>A run saved as a JSON + HTML pair.</summary>
public sealed record SavedRun(string JsonPath, DiagnosticRun Run)
{
    public string HtmlPath => Path.ChangeExtension(JsonPath, ".html");
}

/// <summary>The Reports folder: file naming, saving, listing and before/after pairing.</summary>
public sealed class ReportStore(string folder)
{
    /// <summary>Reports\ next to the exe, so results stay on the USB stick.</summary>
    public static ReportStore Default { get; } = new(Path.Combine(AppContext.BaseDirectory, "Reports"));

    public string Folder { get; } = folder;

    /// <summary>Writes Smith_2026-10-18_before.html/.json, adding _2, _3 for repeat runs on the same day.</summary>
    public SavedRun Save(DiagnosticRun run)
    {
        Directory.CreateDirectory(Folder);
        var stem = UniqueStem(Folder, StemFor(run));
        var json = Path.Combine(Folder, stem + ".json");
        Write(run, json);
        return new SavedRun(json, run);
    }

    /// <summary>Rewrites an existing run in place, e.g. after the technician edits the notes.</summary>
    public static void Write(DiagnosticRun run, string jsonPath)
    {
        File.WriteAllText(Path.ChangeExtension(jsonPath, ".html"), HtmlReport.Render(run));
        File.WriteAllText(jsonPath, RunFile.Serialize(run));
    }

    /// <summary>Writes the comparison next to the after file: Smith_2026-10-18_after.json → Smith_2026-10-18_comparison.html.</summary>
    public static string SaveComparison(DiagnosticRun before, DiagnosticRun after, string afterPath)
    {
        var path = ComparisonPath(afterPath);
        File.WriteAllText(path, ComparisonReport.Render(Comparison.Build(before, after)));
        return path;
    }

    public static string ComparisonPath(string afterPath)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(afterPath))!;
        var stem = Path.GetFileNameWithoutExtension(afterPath);
        stem = stem.Contains("_after") ? stem.Replace("_after", "_comparison") : stem + "_comparison";
        return Path.Combine(dir, stem + ".html");
    }

    /// <summary>Every readable run in the folder, newest first. Unreadable files are skipped.</summary>
    public IReadOnlyList<SavedRun> ListRuns()
    {
        if (!Directory.Exists(Folder)) return [];
        var runs = new List<SavedRun>();
        foreach (var f in Directory.GetFiles(Folder, "*.json"))
        {
            try { runs.Add(new SavedRun(f, RunFile.Load(f))); }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) { }
        }
        return [.. runs.OrderByDescending(r => r.Run.StartedAt)];
    }

    /// <summary>Latest "before" run from this PC, preferring one for the same customer.</summary>
    public SavedRun? FindBeforeRun(string machineName, string customer) =>
        ListRuns()
            .Where(r => r.Run.Intake.RunType == "before" && r.Run.MachineName.Equals(machineName, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(r => customer.Length > 0 && r.Run.Intake.CustomerName.Equals(customer, StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(r => r.Run.StartedAt)
            .FirstOrDefault();

    /// <summary>Smith_2026-10-18_before, or Smith_J1042_2026-10-18_before when a job number is given.</summary>
    public static string StemFor(DiagnosticRun run)
    {
        var job = string.IsNullOrWhiteSpace(run.Intake.JobNumber) ? "" : FileSafe(run.Intake.JobNumber.Trim().Replace(' ', '-')) + "_";
        return $"{FileSafe(LastName(run.Intake.CustomerName))}_{job}{run.StartedAt:yyyy-MM-dd}_{run.Intake.RunType}";
    }

    public static string LastName(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 ? parts[^1] : "Customer";
    }

    public static string FileSafe(string s)
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
}
