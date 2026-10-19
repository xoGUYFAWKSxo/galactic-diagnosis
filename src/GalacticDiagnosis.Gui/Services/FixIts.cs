using System.Diagnostics;
using System.IO;

namespace GalacticDiagnosis.Gui.Services;

/// <summary>A button that opens the Windows tool a technician would use to fix a finding. The app itself changes nothing.</summary>
public sealed record FixAction(string Label, string ToolTip, Action Launch);

/// <summary>Fix-it shortcuts per report section. Settings pages and Windows Security open through Explorer as the
/// signed-in user; MMC tools and Disk Cleanup open elevated, like the app.</summary>
public static class FixIts
{
    public static IReadOnlyList<FixAction> For(string sectionId) => sectionId switch
    {
        "startup" or "boot_time" =>
        [
            new("Startup apps", "Settings › Apps › Startup", () => Shell.Open("ms-settings:startupapps")),
            new("Task Manager", "Task Manager's Startup apps page", () => Run("taskmgr.exe", "/0 /startup")),
        ],
        "disk_space" =>
        [
            new("Storage", "Settings › System › Storage", () => Shell.Open("ms-settings:storagesense")),
            new("Disk Cleanup", "Disk Cleanup for the Windows drive", () => Run("cleanmgr.exe", $"/d {SystemDriveLetter()}")),
        ],
        "updates" => [new("Windows Update", "Settings › Windows Update", () => Shell.Open("ms-settings:windowsupdate"))],
        "antivirus" => [new("Windows Security", "Virus & threat protection", () => Shell.Open("windowsdefender://threat"))],
        "activation" => [new("Activation", "Settings › System › Activation", () => Shell.Open("ms-settings:activation"))],
        "hardware" => [new("Device Manager", "Check for missing or generic drivers", () => Run("mmc.exe", "devmgmt.msc"))],
        "crashes" => [new("Reliability history", "Timeline of crashes, updates and installs", () => Run("perfmon.exe", "/rel"))],
        "drive_health" => [new("Disk Management", "Drives and partitions", () => Run("mmc.exe", "diskmgmt.msc"))],
        "battery" => [new("Full battery report", "Windows' own battery report, made on this PC", OpenBatteryReport)],
        _ => [],
    };

    /// <summary>Extra advice shown with a section's shortcuts.</summary>
    public static string? NoteFor(string sectionId) => sectionId switch
    {
        "drive_health" => "There's no fix to offer for a failing drive. Back up this PC's files before anything else.",
        _ => null,
    };

    static string SystemDriveLetter() => (Environment.GetEnvironmentVariable("SystemDrive") ?? "C:").TrimEnd(':');

    static void Run(string exe, string args)
    {
        try { Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = true })?.Dispose(); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
    }

    /// <summary>powercfg writes the report locally; it's then opened in the browser as the signed-in user.</summary>
    static void OpenBatteryReport()
    {
        var path = Path.Combine(Path.GetTempPath(), "GalacticDiagnosis-battery-report.html");
        try
        {
            using var p = Process.Start(new ProcessStartInfo("powercfg.exe", $"/batteryreport /output \"{path}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            p?.WaitForExit(15_000);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { return; }
        if (File.Exists(path)) Shell.Open(path);
    }
}
