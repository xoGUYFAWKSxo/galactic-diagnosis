using System.Management;

namespace GalacticDiagnosis.Core.Collectors;

/// <summary>
/// Physical drive health from the Storage Management API. MSFT_PhysicalDisk works without admin;
/// MSFT_StorageReliabilityCounter (wear, temperature, hours, errors) needs it.
/// </summary>
public sealed class DriveHealthCollector : ICollector
{
    const string Ns = @"root\Microsoft\Windows\Storage";

    public string Id => "drive_health";
    public string Title => "Drive health";
    public bool NeedsAdmin => true;

    // MSFT_PhysicalDisk.BusType values for removable media; the USB stick this tool runs from is one of them.
    static readonly HashSet<long> ExternalBus = [7, 12, 13, 15]; // USB, SD, MMC, file-backed virtual

    public SectionResult Collect(CancellationToken ct)
    {
        var r = new SectionResult { Id = Id, Title = Title };
        // SELECT * so each object keeps its path; the counter association needs it.
        var disks = Wmi.Query("SELECT * FROM MSFT_PhysicalDisk", Ns);
        ct.ThrowIfCancellationRequested();

        var letters = DriveLettersByDisk();
        var counters = ReliabilityCounters(disks, out var countersError);
        // Without admin the association usually comes back empty rather than throwing.
        if (counters.Count == 0 && !CollectorRunner.IsAdmin())
            countersError ??= "Reliability counters: not running as administrator.";
        var systemLetter = (Path.GetPathRoot(Environment.SystemDirectory) ?? "C").Substring(0, 1).ToUpperInvariant();
        var sentences = new List<string>();
        int skipped = 0;

        foreach (var d in disks.OrderBy(d => d.Str("DeviceId")))
        {
            var bus = d.Long("BusType") ?? 0;
            if (ExternalBus.Contains(bus)) { skipped++; continue; }

            var id = d.Str("DeviceId") ?? "?";
            var name = d.Str("FriendlyName") ?? $"Disk {id}";
            var kind = DriveKind(d.Long("MediaType"), bus);
            var held = letters.TryGetValue(id, out var ls) ? ls : [];
            var isSystem = held.Contains(systemLetter);
            var who = isSystem ? "The Windows drive" : held.Count > 0 ? $"Drive {string.Join("/", held.Select(l => l + ":"))}" : $"The {name}";

            var size = d.Long("Size") is long sz and > 0 ? $", {DriveSize(sz)}" : "";
            var heldText = held.Count > 0 ? $" — holds {string.Join(", ", held.Select(l => l + ":"))}" : "";
            var v = Verdict.Good;
            var notes = new List<string>();

            // HealthStatus already folds in the drive's own SMART failure prediction.
            switch (d.Long("HealthStatus"))
            {
                case 2:
                    v = Verdict.Bad;
                    notes.Add("Windows reports this drive as unhealthy.");
                    sentences.Add($"{who} ({name}) is reporting that it is failing. Back up its files now and replace it.");
                    break;
                case 1:
                    v = Worse(v, Verdict.Warning);
                    notes.Add("Windows reports a health warning for this drive.");
                    sentences.Add($"{who} ({name}) is reporting a health warning. Back up its files and plan to replace it.");
                    break;
            }

            var key = $"disk{id}";
            if (counters.TryGetValue(id, out var c))
            {
                var details = new List<string>();
                if (c.Long("Wear") is { } wear and >= 0 and <= 255 && (wear > 0 || kind != "Hard drive"))
                {
                    r.Metrics[$"{key}.wear_pct"] = wear;
                    details.Add($"{wear}% of rated life used");
                    if (wear >= 90)
                    {
                        v = Worse(v, Verdict.Bad);
                        sentences.Add($"{who} ({name}) has used {wear}% of its rated life. Replace it soon.");
                    }
                    else if (wear >= 70)
                    {
                        v = Worse(v, Verdict.Warning);
                        sentences.Add($"{who} ({name}) has used {wear}% of its rated life. It still works, but plan to replace it within a year or so.");
                    }
                }
                if (c.Long("Temperature") is { } temp and > 0 and < 150)
                {
                    r.Metrics[$"{key}.temp_c"] = temp;
                    details.Add($"{temp} °C");
                    var hot = kind == "Hard drive" ? 55 : 70;
                    if (temp >= hot)
                    {
                        v = Worse(v, Verdict.Warning);
                        notes.Add($"Running hot ({temp} °C).");
                        sentences.Add($"{who} ({name}) is running hot at {temp} °C. Check the PC's airflow and fans.");
                    }
                }
                if (c.Long("PowerOnHours") is { } hours and > 0)
                {
                    r.Metrics[$"{key}.power_on_hours"] = hours;
                    details.Add($"{hours:N0} hours powered on");
                    if (kind == "Hard drive" && hours >= 40_000)
                    {
                        v = Worse(v, Verdict.Warning);
                        sentences.Add($"{who} ({name}) has been powered on for {hours:N0} hours (about {hours / 8766.0:0.#} years). Hard drives this old fail more often, so keep a backup.");
                    }
                }
                var uncorrected = (c.Long("ReadErrorsUncorrected") ?? 0) + (c.Long("WriteErrorsUncorrected") ?? 0);
                if (uncorrected > 0)
                {
                    r.Metrics[$"{key}.uncorrected_errors"] = uncorrected;
                    details.Add($"{uncorrected:N0} uncorrected errors");
                    v = Worse(v, Verdict.Warning);
                    sentences.Add($"{who} ({name}) has recorded {uncorrected:N0} read/write errors it couldn't fix. Back up its files.");
                }
                if (details.Count > 0) notes.Insert(0, string.Join(" · ", details));
            }
            else if (countersError is not null)
            {
                notes.Add("Wear, temperature and error counts need administrator rights.");
            }

            if (isSystem && kind == "Hard drive")
            {
                v = Worse(v, Verdict.Warning);
                sentences.Add("Windows is installed on a spinning hard drive, the most common reason an older PC feels slow. Moving to an SSD is the biggest single speed-up for this PC.");
            }

            r.Items.Add(new($"{name}", $"{kind}{size}{heldText}", v, notes.Count > 0 ? string.Join(" ", notes) : null));
        }

        if (r.Items.Count == 0)
            return SectionResult.Unavailable(Id, Title, "Couldn't find any internal drives to check.");
        if (skipped > 0)
            r.Items.Add(new("External drives", $"{skipped} skipped (USB sticks, SD cards and other removable drives aren't checked)"));

        r.Verdict = Verdicts.Worst(r.Items.Select(i => i.Verdict));
        r.Summary = sentences.Count > 0
            ? string.Join(" ", sentences.Distinct())
            : countersError is null
                ? "All drives report healthy, with no wear or error concerns."
                : "All drives report healthy. Run as administrator to also check wear, temperature and error counts.";
        if (countersError is not null) r.Error = countersError;
        return r;
    }

    static Verdict Worse(Verdict a, Verdict b) => Verdicts.Worst([a, b]);

    static string DriveKind(long? mediaType, long bus) => (mediaType, bus) switch
    {
        (_, 17) => "NVMe SSD",
        (4, _) => bus == 11 ? "SATA SSD" : "SSD",
        (3, _) => "Hard drive",
        (5, _) => "Storage-class memory",
        _ => bus == 11 ? "SATA drive" : "Drive",
    };

    // Drives are sold in decimal units (1 TB = 10^12 bytes); match the label on the box.
    static string DriveSize(long bytes) => bytes >= 1e12 ? $"{bytes / 1e12:0.#} TB" : $"{bytes / 1e9:0} GB";

    /// <summary>DeviceId (disk number) → drive letters on it.</summary>
    static Dictionary<string, List<string>> DriveLettersByDisk()
    {
        var map = new Dictionary<string, List<string>>();
        try
        {
            foreach (var p in Wmi.Query("SELECT DiskNumber, DriveLetter FROM MSFT_Partition", Ns))
            {
                var letter = p.Long("DriveLetter") is long ch and > 0 ? ((char)ch).ToString().ToUpperInvariant() : null;
                if (letter is null) continue;
                var disk = p.Long("DiskNumber")?.ToString() ?? "";
                if (!map.TryGetValue(disk, out var list)) map[disk] = list = [];
                list.Add(letter);
            }
        }
        catch (ManagementException) { }
        foreach (var l in map.Values) l.Sort();
        return map;
    }

    /// <summary>
    /// DeviceId → reliability counter, with an error message when access is denied (not admin).
    /// Enumerating MSFT_StorageReliabilityCounter directly returns nothing; counters are only reachable
    /// through each disk's MSFT_PhysicalDiskToStorageReliabilityCounter association.
    /// </summary>
    static Dictionary<string, ManagementBaseObject> ReliabilityCounters(List<ManagementBaseObject> disks, out string? error)
    {
        error = null;
        var map = new Dictionary<string, ManagementBaseObject>();
        foreach (var d in disks.OfType<ManagementObject>())
        {
            try
            {
                foreach (var c in d.GetRelated("MSFT_StorageReliabilityCounter").Cast<ManagementBaseObject>())
                    if (d.Str("DeviceId") is { } id) map[id] = c;
            }
            catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException)
            {
                error ??= $"Reliability counters: {ex.Message.Trim()}";
            }
        }
        return map;
    }}
