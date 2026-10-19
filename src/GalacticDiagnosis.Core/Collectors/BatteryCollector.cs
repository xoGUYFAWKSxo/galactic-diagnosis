using System.Diagnostics;
using System.Xml.Linq;

namespace GalacticDiagnosis.Core.Collectors;

/// <summary>Battery wear from <c>powercfg /batteryreport /xml</c>: design capacity vs. what it charges to now.</summary>
public sealed class BatteryCollector : ICollector
{
    public string Id => "battery";
    public string Title => "Battery";

    public SectionResult Collect(CancellationToken ct)
    {
        // Win32_Battery is instant; skip the slow powercfg call on desktops.
        if (Wmi.Query("SELECT DeviceID FROM Win32_Battery").Count == 0)
            return new SectionResult
            {
                Id = Id, Title = Title, NotApplicable = true,
                Summary = "No battery found. This looks like a desktop PC.",
            };

        var path = Path.Combine(Path.GetTempPath(), $"galactic-battery-{Guid.NewGuid():N}.xml");
        try
        {
            var psi = new ProcessStartInfo("powercfg.exe", ["/batteryreport", "/xml", "/output", path])
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
            };
            using (var p = Process.Start(psi) ?? throw new InvalidOperationException("powercfg didn't start."))
            {
                p.WaitForExitAsync(ct).GetAwaiter().GetResult();
                if (p.ExitCode != 0 || !File.Exists(path))
                    throw new InvalidOperationException($"powercfg exited with {p.ExitCode}: {p.StandardError.ReadToEnd().Trim()}");
            }
            return FromReport(XDocument.Load(path), Id, Title);
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }

    public sealed record Battery(string Name, string? Chemistry, long DesignMwh, long FullMwh, long? Cycles);

    /// <summary>Reads Batteries/Battery elements, matching by local name so the report's XML namespace doesn't matter.</summary>
    public static List<Battery> Parse(XDocument doc)
    {
        static string? Val(XElement e, string name) => e.Elements().FirstOrDefault(x => x.Name.LocalName == name)?.Value.Trim();
        static long? Num(XElement e, string name) => long.TryParse(Val(e, name), out var n) ? n : null;

        return doc.Descendants().Where(e => e.Name.LocalName == "Battery").Select(b =>
        {
            var name = string.Join(" ", new[] { Fmt.Clean(Val(b, "Manufacturer")), Fmt.Clean(Val(b, "Id")) }.OfType<string>());
            return new Battery(name.Length > 0 ? name : "Battery", Fmt.Clean(Val(b, "Chemistry")),
                Num(b, "DesignCapacity") ?? 0, Num(b, "FullChargeCapacity") ?? 0, Num(b, "CycleCount") is > 0 and var c ? c : null);
        }).ToList();
    }

    public static SectionResult FromReport(XDocument doc, string id = "battery", string title = "Battery")
    {
        var r = new SectionResult { Id = id, Title = title };
        var batteries = Parse(doc);
        if (batteries.Count == 0)
            return SectionResult.Unavailable(id, title, "Windows lists a battery but its battery report has no details.");

        var sentences = new List<string>();
        for (var i = 0; i < batteries.Count; i++)
        {
            var b = batteries[i];
            var key = batteries.Count == 1 ? "battery" : $"battery{i + 1}";
            var parts = new List<string>();
            var v = Verdict.Info;

            if (b.DesignMwh > 0 && b.FullMwh > 0)
            {
                var health = Math.Min(100, b.FullMwh * 100.0 / b.DesignMwh);
                r.Metrics[$"{key}.health_pct"] = Math.Round(health, 1);
                parts.Add($"holds {health:0}% of its original charge ({b.FullMwh / 1000.0:0.#} of {b.DesignMwh / 1000.0:0.#} Wh)");
                v = health switch { < 50 => Verdict.Bad, < 70 => Verdict.Warning, _ => Verdict.Good };
                sentences.Add(v switch
                {
                    Verdict.Bad => $"The battery now holds only {health:0}% of the charge it held when new. Replacing it will bring back normal battery life.",
                    Verdict.Warning => $"The battery holds {health:0}% of the charge it held when new, so it runs down noticeably faster than it used to. It's worth replacing if battery life matters.",
                    _ => $"The battery holds {health:0}% of its original charge, which is healthy.",
                });
            }
            else
            {
                parts.Add("capacity not reported");
            }
            if (b.Cycles is { } cycles)
            {
                r.Metrics[$"{key}.cycles"] = cycles;
                parts.Add($"{cycles} charge cycles");
            }
            r.Items.Add(new(b.Name, string.Join(", ", parts), v, b.Chemistry is null ? null : $"Chemistry: {b.Chemistry}"));
        }

        r.Verdict = Verdicts.Worst(r.Items.Select(i => i.Verdict));
        r.Summary = sentences.Count > 0 ? string.Join(" ", sentences) : "The battery didn't report its capacity, so its wear couldn't be measured.";
        return r;
    }
}
