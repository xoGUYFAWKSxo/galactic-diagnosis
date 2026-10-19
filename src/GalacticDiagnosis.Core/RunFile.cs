using System.Text.Encodings.Web;
using System.Text.Json;

namespace GalacticDiagnosis.Core;

/// <summary>Reads and writes the raw-data JSON saved next to each report.</summary>
public static class RunFile
{
    static readonly JsonSerializerOptions Opts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // keep "—" readable; the file is never embedded in HTML
    };

    public static string Serialize(DiagnosticRun run) => JsonSerializer.Serialize(run, Opts);

    public static DiagnosticRun Deserialize(string json) =>
        JsonSerializer.Deserialize<DiagnosticRun>(json, Opts) ?? throw new InvalidDataException("The file is empty.");

    public static DiagnosticRun Load(string path)
    {
        try { return Deserialize(File.ReadAllText(path)); }
        catch (JsonException ex) { throw new InvalidDataException($"{Path.GetFileName(path)} isn't a Galactic Diagnosis data file: {ex.Message}", ex); }
    }
}
