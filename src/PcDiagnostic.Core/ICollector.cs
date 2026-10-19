namespace PcDiagnostic.Core;

/// <summary>One report section. Implementations may throw; <see cref="CollectorRunner"/> turns failures into "couldn't read".</summary>
public interface ICollector
{
    string Id { get; }
    string Title { get; }

    /// <summary>Most WMI reads finish in under a second; activation can take several.</summary>
    TimeSpan Timeout => TimeSpan.FromSeconds(20);

    /// <summary>True if the section can't be read reliably without admin rights.</summary>
    bool NeedsAdmin => false;

    SectionResult Collect(CancellationToken ct);
}
