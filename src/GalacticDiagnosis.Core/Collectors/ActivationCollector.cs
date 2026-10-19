namespace GalacticDiagnosis.Core.Collectors;

/// <summary>Windows activation from SoftwareLicensingProduct. Slow (seconds on some PCs), hence the long timeout.</summary>
public sealed class ActivationCollector : ICollector
{
    const string WindowsAppId = "55c92734-d682-4d71-983e-d6ec3f16059f";

    public string Id => "activation";
    public string Title => "Windows activation";
    public TimeSpan Timeout => TimeSpan.FromSeconds(45);

    public SectionResult Collect(CancellationToken ct)
    {
        var r = new SectionResult { Id = Id, Title = Title };
        // Only products with an installed key; the class also lists every edition Windows could be upgraded to.
        var products = Wmi.Query("SELECT Name, Description, LicenseStatus, GracePeriodRemaining FROM SoftwareLicensingProduct " +
            $"WHERE ApplicationID = '{WindowsAppId}' AND PartialProductKey IS NOT NULL");

        if (products.Count == 0)
        {
            r.Verdict = Verdict.Bad;
            r.Summary = "No Windows product key is installed, so Windows isn't activated.";
            r.Items.Add(new("Status", "No product key", Verdict.Bad));
            return r;
        }

        // An activated product wins if several keys are installed.
        var p = products.OrderBy(x => x.Long("LicenseStatus") == 1 ? 0 : 1).First();
        var status = p.Long("LicenseStatus") ?? 0;
        var edition = EditionName(p.Str("Name"));
        var channel = Channel(p.Str("Description"));
        r.Metrics["license_status"] = status;

        (r.Verdict, var text, r.Summary) = status switch
        {
            1 => (Verdict.Good, "Activated", "Windows is activated."),
            0 => (Verdict.Bad, "Not activated", "Windows isn't activated. Some settings are locked and Windows will keep asking for a product key."),
            5 => (Verdict.Bad, "Not activated (notification mode)", "Windows isn't activated. Some settings are locked and Windows will keep asking for a product key."),
            _ => (Verdict.Warning, $"Grace period ({StatusName(status)})", "Windows is running in a temporary grace period and needs to be activated soon."),
        };
        r.Items.Add(new("Status", text, r.Verdict));
        r.Items.Add(new("Edition", edition));
        if (channel is not null) r.Items.Add(new("License type", channel));
        if (status != 1 && p.Long("GracePeriodRemaining") is > 0 and var minutes)
            r.Items.Add(new("Grace period left", $"{minutes / 60 / 24} days"));
        return r;
    }

    /// <summary>"Windows(R), Core edition" → "Windows Home". License names use internal edition IDs, not retail names.</summary>
    public static string EditionName(string? licenseName)
    {
        var id = (licenseName ?? "").Replace("Windows(R),", "").Replace("edition", "").Trim();
        return id switch
        {
            "" => "Windows",
            "Core" => "Windows Home",
            "CoreSingleLanguage" => "Windows Home Single Language",
            "CoreCountrySpecific" => "Windows Home (China)",
            "Professional" => "Windows Pro",
            "ProfessionalWorkstation" => "Windows Pro for Workstations",
            "ProfessionalEducation" => "Windows Pro Education",
            _ => $"Windows {id}",
        };
    }

    static string? Channel(string? description)
    {
        if (description is null) return null;
        if (description.Contains("RETAIL", StringComparison.OrdinalIgnoreCase)) return "Retail (bought separately)";
        if (description.Contains("OEM", StringComparison.OrdinalIgnoreCase)) return "OEM (came with the PC)";
        if (description.Contains("VOLUME", StringComparison.OrdinalIgnoreCase)) return "Volume (business licensing)";
        return null;
    }

    static string StatusName(long s) => s switch
    {
        2 => "out-of-box grace", 3 => "out-of-tolerance grace", 4 => "non-genuine grace", 6 => "extended grace", _ => $"status {s}",
    };
}
