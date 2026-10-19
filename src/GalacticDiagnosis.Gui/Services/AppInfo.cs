using GalacticDiagnosis.Core;

namespace GalacticDiagnosis.Gui.Services;

/// <summary>This PC as the window presents it. The hidden --demo switch stands in the sample PC, so README
/// screenshots made from docs/sample show no real machine name.</summary>
public static class AppInfo
{
    public static string MachineName { get; private set; } = Environment.MachineName;
    public static bool IsAdmin { get; private set; } = CollectorRunner.IsAdmin();

    public static void UseDemo()
    {
        MachineName = "SAMPLE-PC";
        IsAdmin = true;
    }
}
