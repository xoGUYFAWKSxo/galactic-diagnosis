using System.Management;

namespace GalacticDiagnosis.Core.Collectors;

/// <summary>Registered antivirus from Security Center (root\SecurityCenter2). Exists on client Windows only, not Server.</summary>
public sealed class AntivirusCollector : ICollector
{
    public string Id => "antivirus";
    public string Title => "Antivirus";

    public sealed record Product(string Name, bool Enabled, bool UpToDate);

    public SectionResult Collect(CancellationToken ct)
    {
        List<ManagementBaseObject> rows;
        try
        {
            rows = Wmi.Query("SELECT displayName, productState FROM AntiVirusProduct", @"root\SecurityCenter2");
        }
        catch (ManagementException ex) when (ex.ErrorCode is ManagementStatus.InvalidNamespace or ManagementStatus.InvalidClass)
        {
            return SectionResult.Unavailable(Id, Title, "Antivirus status isn't available on this edition of Windows (Windows Server).");
        }
        return Evaluate(rows.Select(p => Decode(p.Str("displayName") ?? "Unknown antivirus", p.Long("productState") ?? 0)).ToList());
    }

    /// <summary>
    /// productState is undocumented but stable: bits 12–15 = 1 when real-time protection is on,
    /// bits 4–7 = 1 when definitions are out of date. 0x061100 → on, up to date.
    /// </summary>
    public static Product Decode(string name, long state) =>
        new(name, ((state >> 12) & 0xF) == 1, ((state >> 4) & 0xF) == 0);

    public static SectionResult Evaluate(List<Product> products, string id = "antivirus", string title = "Antivirus")
    {
        var r = new SectionResult { Id = id, Title = title };
        foreach (var p in products)
        {
            var v = !p.Enabled ? Verdict.Info : p.UpToDate ? Verdict.Good : Verdict.Warning;
            r.Items.Add(new(p.Name, (p.Enabled ? "On" : "Off") + (p.Enabled ? p.UpToDate ? ", up to date" : ", out of date" : ""), v));
        }

        var on = products.Where(p => p.Enabled).ToList();
        r.Metrics["antivirus_enabled"] = on.Count;
        if (on.Count == 0)
        {
            r.Verdict = Verdict.Bad;
            r.Summary = products.Count == 0
                ? "No antivirus is registered with Windows. The PC is unprotected."
                : "Antivirus is installed but switched off. The PC is unprotected.";
        }
        else if (on.Any(p => !p.UpToDate))
        {
            r.Verdict = Verdict.Warning;
            var stale = on.First(p => !p.UpToDate);
            r.Summary = $"{stale.Name} is on, but its virus definitions are out of date. An expired subscription is the usual cause.";
        }
        else if (on.Count > 1)
        {
            r.Verdict = Verdict.Warning;
            r.Summary = $"{on.Count} antivirus programs are running at once ({string.Join(", ", on.Select(p => p.Name))}). They slow the PC down and can conflict, so keep just one.";
        }
        else
        {
            r.Verdict = Verdict.Good;
            r.Summary = $"{on[0].Name} is on and up to date.";
        }
        return r;
    }
}
