namespace PcDiagnostic.Core.Collectors;

public sealed class DiskSpaceCollector : ICollector
{
    public string Id => "disk_space";
    public string Title => "Disk space";

    public SectionResult Collect(CancellationToken ct)
    {
        var r = new SectionResult { Id = Id, Title = Title };
        var sentences = new List<string>();
        var systemDrive = Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\";

        foreach (var d in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady))
        {
            var letter = d.Name.TrimEnd('\\');
            double free = d.AvailableFreeSpace, total = d.TotalSize;
            if (total <= 0) continue;
            var pct = free / total * 100;
            var freeGb = free / 1024 / 1024 / 1024;
            var isSystem = d.Name.Equals(systemDrive, StringComparison.OrdinalIgnoreCase);

            // The system drive needs headroom for updates, the page file and temp files; data drives can run fuller.
            var v = (isSystem, pct, freeGb) switch
            {
                (true, < 10, _) or (true, _, < 10) => Verdict.Bad,
                (true, < 20, _) or (true, _, < 25) => Verdict.Warning,
                (false, < 5, _) => Verdict.Bad,
                (false, < 10, _) => Verdict.Warning,
                _ => Verdict.Good,
            };

            var label = string.IsNullOrWhiteSpace(d.VolumeLabel) ? letter : $"{letter} ({d.VolumeLabel})";
            if (isSystem) label += " — Windows";
            r.Items.Add(new(label, $"{Fmt.Gb(free)} free of {Fmt.Gb(total)} ({pct:0}% free)", v));
            r.Metrics[$"{letter}.free_gb"] = Math.Round(freeGb, 1);
            r.Metrics[$"{letter}.free_pct"] = Math.Round(pct, 1);

            if (v == Verdict.Bad)
                sentences.Add(isSystem
                    ? $"The Windows drive ({letter}) is nearly full, with only {Fmt.Gb(free)} left. This slows the PC down and can stop updates from installing."
                    : $"Drive {letter} is nearly full ({Fmt.Gb(free)} left).");
            else if (v == Verdict.Warning)
                sentences.Add(isSystem
                    ? $"The Windows drive ({letter}) is getting full ({Fmt.Gb(free)} left). Clearing some space is a good idea."
                    : $"Drive {letter} is getting full ({Fmt.Gb(free)} left).");
        }

        if (r.Items.Count == 0)
            return SectionResult.Unavailable(Id, Title, "Couldn't find any fixed drives to measure.");

        r.Verdict = Verdicts.Worst(r.Items.Select(i => i.Verdict));
        r.Summary = sentences.Count > 0 ? string.Join(" ", sentences) : "Every drive has plenty of free space.";
        return r;
    }
}
