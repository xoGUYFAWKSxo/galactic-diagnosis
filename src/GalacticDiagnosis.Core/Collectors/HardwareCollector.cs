using Microsoft.Win32;

namespace GalacticDiagnosis.Core.Collectors;

public sealed class HardwareCollector : ICollector
{
    public string Id => "hardware";
    public string Title => "Hardware";

    public SectionResult Collect(CancellationToken ct)
    {
        var r = new SectionResult { Id = Id, Title = Title };
        var problems = new List<string>();

        foreach (var os in Wmi.Query("SELECT Caption, Version, BuildNumber, OSArchitecture, LastBootUpTime FROM Win32_OperatingSystem"))
        {
            r.Items.Add(new("Windows", $"{os.Str("Caption")} ({os.Str("OSArchitecture")}, build {os.Str("BuildNumber")})"));
            if (os.Date("LastBootUpTime") is { } boot)
            {
                var up = DateTime.Now - boot;
                r.Metrics["uptime_hours"] = Math.Round(up.TotalHours, 1);
                r.Items.Add(new("Last restart", $"{boot:yyyy-MM-dd HH:mm} ({FormatUptime(up)} ago)"));
            }
        }
        ct.ThrowIfCancellationRequested();

        var cs = Wmi.Query("SELECT Manufacturer, Model FROM Win32_ComputerSystem").FirstOrDefault();
        var model = string.Join(" ", new[] { Fmt.Clean(cs?.Str("Manufacturer")), Fmt.Clean(cs?.Str("Model")) }.OfType<string>());
        if (model.Length > 0) r.Items.Add(new("Computer", model));

        foreach (var cpu in Wmi.Query("SELECT Name, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed FROM Win32_Processor"))
        {
            var mhz = cpu.Long("MaxClockSpeed");
            r.Items.Add(new("Processor",
                $"{cpu.Str("Name")} — {cpu.Long("NumberOfCores")} cores / {cpu.Long("NumberOfLogicalProcessors")} threads"
                + (mhz is > 0 ? $", {mhz / 1000.0:0.0#} GHz base" : "")));
        }
        ct.ThrowIfCancellationRequested();

        var vram = ReadVramFromRegistry();
        foreach (var gpu in Wmi.Query("SELECT Name, DriverVersion, DriverDate, AdapterRAM FROM Win32_VideoController"))
        {
            var name = gpu.Str("Name") ?? "Unknown GPU";
            // AdapterRAM is a uint32 and caps at 4 GB; the driver's registry value is the real size.
            var mem = vram.TryGetValue(name, out var q) ? q : gpu.Long("AdapterRAM") ?? 0;
            var parts = new List<string> { name };
            if (mem > 0) parts.Add($"{Math.Max(1, Math.Round(mem / 1073741824.0)):0} GB");
            if (gpu.Str("DriverVersion") is { } dv) parts.Add($"driver {dv}");
            var driverDate = gpu.Date("DriverDate");
            if (driverDate is { } d) parts.Add($"dated {d:yyyy-MM-dd}");

            Verdict v = Verdict.Info;
            string? note = null;
            if (name.Contains("Basic Display", StringComparison.OrdinalIgnoreCase))
            {
                v = Verdict.Bad;
                note = "No graphics driver is installed.";
                problems.Add("The graphics card has no driver installed, so Windows is using a basic fallback. Install the manufacturer's driver.");
            }
            r.Items.Add(new("Graphics", string.Join(", ", parts), v, note));
        }
        ct.ThrowIfCancellationRequested();

        var sticks = Wmi.Query("SELECT Capacity, Speed, ConfiguredClockSpeed, Manufacturer, PartNumber, BankLabel, DeviceLocator FROM Win32_PhysicalMemory");
        long totalRam = sticks.Sum(s => s.Long("Capacity") ?? 0);
        if (totalRam == 0) // Some VMs and odd firmware report no sticks.
            totalRam = Wmi.Query("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem").FirstOrDefault()?.Long("TotalPhysicalMemory") ?? 0;
        if (totalRam > 0)
        {
            var gb = totalRam / 1024.0 / 1024 / 1024;
            r.Metrics["ram_gb"] = Math.Round(gb, 1);
            Verdict v = Verdict.Info;
            string? note = null;
            if (gb < 7.5)
            {
                v = Verdict.Warning;
                note = "Less than 8 GB is tight for Windows 10/11.";
                problems.Add($"This PC has {gb:0} GB of memory, which is tight for current Windows. More memory would help it feel faster with several programs open.");
            }
            var speeds = sticks.Select(s => s.Long("ConfiguredClockSpeed") is > 0 and var c ? c : s.Long("Speed")).OfType<long>().Distinct().ToList();
            var desc = $"{Fmt.Gb(totalRam)} in {sticks.Count} stick{(sticks.Count == 1 ? "" : "s")}"
                + (speeds.Count > 0 ? $", running at {string.Join("/", speeds)} MT/s" : "");
            r.Items.Add(new("Memory", desc, v, note));
            foreach (var s in sticks)
            {
                // DeviceLocator alone repeats across channels on many boards ("DIMM 1" twice), so prefix the bank.
                var loc = string.Join(" ", new[] { Fmt.Clean(s.Str("BankLabel")), s.Str("DeviceLocator") }.OfType<string>());
                if (loc.Length == 0) loc = "Slot";
                var detail = string.Join(" ", new[] { Fmt.Gb(s.Long("Capacity") ?? 0), Fmt.Clean(s.Str("Manufacturer")), Fmt.Clean(s.Str("PartNumber")) }.OfType<string>());
                r.Items.Add(new($"  {loc}", detail));
            }
        }
        ct.ThrowIfCancellationRequested();

        var board = Wmi.Query("SELECT Manufacturer, Product FROM Win32_BaseBoard").FirstOrDefault();
        if (board is not null)
        {
            var b = string.Join(" ", new[] { Fmt.Clean(board.Str("Manufacturer")), Fmt.Clean(board.Str("Product")) }.OfType<string>());
            // Custom-built desktops report the board as the computer model; don't list it twice.
            if (b.Length > 0 && !b.Equals(model, StringComparison.OrdinalIgnoreCase)) r.Items.Add(new("Motherboard", b));
        }

        var bios = Wmi.Query("SELECT SMBIOSBIOSVersion, ReleaseDate FROM Win32_BIOS").FirstOrDefault();
        if (bios is not null)
        {
            var date = bios.Date("ReleaseDate");
            r.Items.Add(new("BIOS", $"{bios.Str("SMBIOSBIOSVersion")}" + (date is { } d ? $" ({d:yyyy-MM-dd})" : "")));
        }

        r.Verdict = problems.Count > 0 ? Verdicts.Worst(r.Items.Select(i => i.Verdict)) : Verdict.Good;
        r.Summary = problems.Count > 0 ? string.Join(" ", problems) : "The hardware was identified and nothing stands out.";
        return r;
    }

    static string FormatUptime(TimeSpan t) =>
        t.TotalDays >= 1 ? $"{(int)t.TotalDays} day{((int)t.TotalDays == 1 ? "" : "s")}" : $"{(int)t.TotalHours} h";

    /// <summary>Maps adapter name → VRAM bytes from the display class key's HardwareInformation.qwMemorySize.</summary>
    static Dictionary<string, long> ReadVramFromRegistry()
    {
        var map = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var cls = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
            if (cls is null) return map;
            foreach (var sub in cls.GetSubKeyNames().Where(n => n.All(char.IsDigit)))
            {
                try
                {
                    using var k = cls.OpenSubKey(sub);
                    if (k?.GetValue("DriverDesc") is not string desc) continue;
                    var size = k.GetValue("HardwareInformation.qwMemorySize") switch
                    {
                        long l => l,
                        byte[] { Length: 8 } b => BitConverter.ToInt64(b),
                        _ => 0L,
                    };
                    if (size > 0) map[desc] = size;
                }
                catch (System.Security.SecurityException) { } // "Properties" subkey and friends are locked down.
            }
        }
        catch { }
        return map;
    }
}
