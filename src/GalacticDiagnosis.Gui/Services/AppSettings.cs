using System.IO;
using System.Text.Json;
using System.Windows;

namespace GalacticDiagnosis.Gui.Services;

/// <summary>Technician preferences, saved next to the exe so they travel with the USB stick, not the customer's PC.</summary>
public sealed class AppSettings
{
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
    static string FilePath => Path.Combine(AppContext.BaseDirectory, "GalacticDiagnosis.settings.json");

    /// <summary>"dark", "light", or null to follow Windows.</summary>
    public string? Theme { get; set; }
    public string TechnicianName { get; set; } = "";
    public double? WindowWidth { get; set; }
    public double? WindowHeight { get; set; }
    public bool Maximized { get; set; }

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath)) return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Json) ?? new();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
        return new();
    }

    /// <summary>Best effort: a read-only stick just means settings aren't remembered.</summary>
    public void Save()
    {
        try { File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    public void RestoreWindow(Window w)
    {
        var area = SystemParameters.WorkArea;
        if (WindowWidth is > 600 and var width) w.Width = Math.Min(width, area.Width);
        if (WindowHeight is > 400 and var height) w.Height = Math.Min(height, area.Height);
        if (Maximized) w.WindowState = WindowState.Maximized;
        w.Closing += (_, _) =>
        {
            Maximized = w.WindowState == WindowState.Maximized;
            var r = w.RestoreBounds;
            if (!r.IsEmpty) (WindowWidth, WindowHeight) = (r.Width, r.Height);
            Save();
        };
    }
}
