using System.Management;

namespace PcDiagnostic.Core;

/// <summary>Thin WMI helpers. Values vary a lot between machines, so every getter tolerates null and odd types.</summary>
public static class Wmi
{
    public static List<ManagementBaseObject> Query(string wql, string scope = @"root\cimv2")
    {
        var options = new System.Management.EnumerationOptions { Timeout = TimeSpan.FromSeconds(15), ReturnImmediately = true };
        using var searcher = new ManagementObjectSearcher(new ManagementScope(scope), new ObjectQuery(wql), options);
        return [.. searcher.Get().Cast<ManagementBaseObject>()];
    }

    public static string? Str(this ManagementBaseObject o, string prop)
    {
        var s = Raw(o, prop)?.ToString()?.Trim();
        return string.IsNullOrEmpty(s) ? null : s;
    }

    public static long? Long(this ManagementBaseObject o, string prop)
    {
        var v = Raw(o, prop);
        if (v is null) return null;
        try { return Convert.ToInt64(v); } catch { return null; }
    }

    public static DateTime? Date(this ManagementBaseObject o, string prop)
    {
        var s = Str(o, prop);
        if (s is null) return null;
        try { return ManagementDateTimeConverter.ToDateTime(s); } catch { return null; }
    }

    static object? Raw(ManagementBaseObject o, string prop)
    {
        try { return o[prop]; } catch (ManagementException) { return null; }
    }
}

public static class Fmt
{
    public static string Gb(double bytes) => (bytes / 1024 / 1024 / 1024) switch
    {
        >= 100 => $"{bytes / 1024 / 1024 / 1024:0} GB",
        _ => $"{bytes / 1024 / 1024 / 1024:0.#} GB",
    };

    /// <summary>Hardware strings often carry filler like "To Be Filled By O.E.M."; treat those as missing.</summary>
    public static string? Clean(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var t = s.Trim();
        string[] junk = ["To Be Filled By O.E.M.", "Default string", "System Product Name", "Not Applicable", "None", "Unknown"];
        return junk.Any(j => t.Equals(j, StringComparison.OrdinalIgnoreCase)) ? null : t;
    }
}
