using System.IO;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace GalacticDiagnosis.Gui.Services;

/// <summary>Sets up WebView2 for showing reports only: no web access, no browser chrome, cache kept off the USB stick.</summary>
public static class WebViewHost
{
    public static bool ForceUnavailable { get; set; }

    /// <summary>In %TEMP% rather than next to the exe, so browser cache doesn't land on the USB stick.</summary>
    static readonly string DataFolder = Path.Combine(Path.GetTempPath(), "GalacticDiagnosis.WebView2");

    // Chromium switches that stop the background fetches a normal browser makes (updates, metrics, safe browsing,
    // secure-DNS lookups). The dead proxy and the resolver rule make anything left over fail locally: reports
    // never need the network, so there's nothing for them to break.
    const string Switches = "--disable-background-networking --disable-component-update --disable-sync " +
        "--disable-domain-reliability --no-pings --no-first-run --disable-features=msSmartScreenProtection " +
        "--proxy-server=127.0.0.1:9 --proxy-bypass-list=<-loopback> \"--host-resolver-rules=MAP * ~NOTFOUND\"";

    static bool? _available;
    static Task<CoreWebView2Environment>? _env;

    public static bool Available
    {
        get
        {
            if (ForceUnavailable) return false;
            if (_available is { } a) return a;
            try { _available = CoreWebView2Environment.GetAvailableBrowserVersionString() is { Length: > 0 }; }
            catch (WebView2RuntimeNotFoundException) { _available = false; }
            return _available.Value;
        }
    }

    public static async Task InitAsync(WebView2 web)
    {
        if (web.CoreWebView2 is not null) return;
        // Custom crash reporting keeps crash dumps local instead of uploading them to Microsoft.
        _env ??= CoreWebView2Environment.CreateAsync(null, DataFolder,
            new CoreWebView2EnvironmentOptions(Switches) { IsCustomCrashReportingEnabled = true });
        await web.EnsureCoreWebView2Async(await _env);
        var core = web.CoreWebView2!;

        var s = core.Settings;
        s.AreDevToolsEnabled = false;
        s.AreDefaultContextMenusEnabled = false;
        s.IsStatusBarEnabled = false;
        s.IsGeneralAutofillEnabled = false;
        s.IsPasswordAutosaveEnabled = false;
        s.AreBrowserAcceleratorKeysEnabled = false;
        s.IsReputationCheckingRequired = false;
        s.IsZoomControlEnabled = true;

        // Reports are self-contained: anything that isn't the page itself or a jump within it is refused.
        core.NavigationStarting += (_, e) =>
        {
            if (!(e.Uri.StartsWith("data:", StringComparison.Ordinal) || e.Uri.StartsWith("about:", StringComparison.Ordinal)))
                e.Cancel = true;
        };
        core.NewWindowRequested += (_, e) => e.Handled = true;
        core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += (_, e) =>
            e.Response = core.Environment.CreateWebResourceResponse(null, 403, "Blocked", "");

        ApplyTheme(web);
        void OnTheme()
        {
            // The view may have been closed since; stop listening once its browser is gone.
            try { ApplyTheme(web); }
            catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException or System.Runtime.InteropServices.COMException)
            {
                ThemeManager.Changed -= OnTheme;
            }
        }
        ThemeManager.Changed += OnTheme;
    }

    static void ApplyTheme(WebView2 web)
    {
        web.DefaultBackgroundColor = ThemeManager.IsDark
            ? System.Drawing.Color.FromArgb(0x0C, 0x10, 0x28)
            : System.Drawing.Color.FromArgb(0xF6, 0xF7, 0xF9);
        web.CoreWebView2.Profile.PreferredColorScheme =
            ThemeManager.IsDark ? CoreWebView2PreferredColorScheme.Dark : CoreWebView2PreferredColorScheme.Light;
    }

    /// <summary>Best effort on exit; browser processes can hold files for a moment after the window closes.</summary>
    public static void TryDeleteDataFolder()
    {
        for (var i = 0; i < 5 && Directory.Exists(DataFolder); i++)
        {
            try { Directory.Delete(DataFolder, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Thread.Sleep(200); }
        }
    }
}
