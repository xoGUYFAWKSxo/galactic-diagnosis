using System.Diagnostics;
using System.IO;

namespace GalacticDiagnosis.Gui.Services;

/// <summary>Opens files and folders through Explorer. The app runs elevated, and handing the request to the
/// already-running Explorer means the browser or folder window opens as the normal user, not as admin.</summary>
public static class Shell
{
    public static void Open(string target) => Start("explorer.exe", $"\"{target}\"");

    public static void ShowInFolder(string file) => Start("explorer.exe", $"/select,\"{file}\"");

    static void Start(string exe, string args)
    {
        try { Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = false })?.Dispose(); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException) { }
    }
}
