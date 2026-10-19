using System.Text.Json.Serialization;

namespace PcDiagnostic.Core;

[JsonConverter(typeof(JsonStringEnumConverter<Verdict>))]
public enum Verdict
{
    /// <summary>Facts with no judgement attached (e.g. CPU model).</summary>
    Info,
    Good,
    Warning,
    Bad,
    /// <summary>The collector failed, timed out, or needed rights it didn't have.</summary>
    Unavailable,
}

/// <summary>One row in a report section.</summary>
public sealed record Item(string Label, string Value, Verdict Verdict = Verdict.Info, string? Note = null);

/// <summary>What a collector hands back: display rows, a verdict, a plain-English sentence, and raw numbers.</summary>
public sealed class SectionResult
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public Verdict Verdict { get; set; } = Verdict.Info;

    /// <summary>The sentence a customer reads, e.g. "This drive has used 87% of its rated life. Replace it soon."</summary>
    public string Summary { get; set; } = "";

    public List<Item> Items { get; } = [];

    /// <summary>Numbers kept for before/after comparison, e.g. "boot_ms" or "C:.free_gb".</summary>
    public Dictionary<string, double> Metrics { get; } = [];

    /// <summary>Technical detail when the section couldn't be read. Not shown as the summary.</summary>
    public string? Error { get; set; }

    public double ElapsedMs { get; set; }

    public static SectionResult Unavailable(string id, string title, string summary, string? error = null) =>
        new() { Id = id, Title = title, Verdict = Verdict.Unavailable, Summary = summary, Error = error };
}

public sealed class Intake
{
    public string CustomerName { get; set; } = "";
    public string Complaint { get; set; } = "";

    /// <summary>"before" or "after".</summary>
    public string RunType { get; set; } = "before";
}

public sealed class DiagnosticRun
{
    public required Intake Intake { get; init; }
    public required string MachineName { get; init; }
    public required DateTimeOffset StartedAt { get; init; }
    public required bool RanAsAdmin { get; init; }
    public required string ToolVersion { get; init; }
    public required List<SectionResult> Sections { get; init; }
}

public static class Verdicts
{
    /// <summary>Worst verdict wins; Unavailable doesn't outrank a real Bad or Warning.</summary>
    public static Verdict Worst(IEnumerable<Verdict> verdicts)
    {
        var list = verdicts.ToList();
        if (list.Contains(Verdict.Bad)) return Verdict.Bad;
        if (list.Contains(Verdict.Warning)) return Verdict.Warning;
        if (list.Contains(Verdict.Unavailable)) return Verdict.Unavailable;
        if (list.Contains(Verdict.Good)) return Verdict.Good;
        return Verdict.Info;
    }
}
