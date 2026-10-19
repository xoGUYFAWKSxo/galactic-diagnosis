# Galactic Diagnosis — Status

Plans: [docs/BUILD_PLAN.md](docs/BUILD_PLAN.md) (Phases 1–5, the console tool) and [docs/GUI_PLAN.md](docs/GUI_PLAN.md) (Phases 6–11, the GUI).

## Done — Phase 6 (shared run pipeline), 2026-10-18
- `GalacticDiagnosis.App` was renamed to `GalacticDiagnosis.Console` (`GalacticDiagnosis-Console.exe`, namespace `GalacticDiagnosis.Cli`, so it doesn't shadow `System.Console`).
- `ReportStore` (naming, saving, `ListRuns`, `FindBeforeRun`, comparisons) and `RunSession` (run → save → compare) live in the Report project, because saving needs the renderers. `Program.cs` is now only prompts and arguments.
- `CollectorRunner.RunAllAsync` takes a `CancellationToken` for the GUI's Cancel button.
- 58 tests. Console before → after output is unchanged (same file names, comparison written).

## Done — Phase 7 (icon, themes, report viewer), 2026-10-18
- Icon: `assets/make_icon.py` (Pillow + NumPy) draws the Galactic Studio galaxy (`galaxy.py`, copied unchanged from Galactic Macro) with a crossed wrench and screwdriver in the bottom-right. It writes `icon.png`, `icon.ico` and `docs/social-preview.png`, and is used by both exes.
- `src/GalacticDiagnosis.Gui` (WPF, `GalacticDiagnosis.exe`, `requireAdministrator`):
  - A header with the icon, machine name, admin badge, Open report (Ctrl+O) and a dark/light toggle.
  - The report viewer: Report and Before vs after tabs, Open in browser, Show in folder.
  - Opening a `.json` re-renders it. An after run also loads its saved `_comparison.html`.
  - Dark and light themes (`Themes/Dark.xaml`, `Themes/Light.xaml`) with a matching title bar. The report HTML has a screen-only dark block; print is always light.
  - Settings (theme, window size) are saved in `GalacticDiagnosis.settings.json` next to the exe.
- WebView2:
  - Data goes to `%TEMP%\GalacticDiagnosis.WebView2`, deleted on exit.
  - Reports load with `NavigateToString`. Any other navigation, new window or web request is refused.
  - Dev tools, context menu, autofill and SmartScreen are off, and crash dumps stay local.
  - Opening in the browser or folder goes through `explorer.exe`, so those windows aren't elevated.
- No-WebView2 fallback: shows a banner and opens the page in the default browser. Test it with the hidden `--no-webview2` switch.
- **Network check** (TCP connections of the app and its WebView2 processes, sampled while viewing):
  - The app process: none.
  - The WebView2 network service: secure-DNS lookups to Cloudflare, removed by the dead proxy (`--proxy-server=127.0.0.1:9`) and `--host-resolver-rules=MAP * ~NOTFOUND`.
  - The WebView2 browser process: one HTTPS connection to 52.96.x.x, which is in the same range as `substrate.office.com` (Microsoft 365). It still happens with those switches, with every guessed Edge feature flag turned off, and with custom crash reporting. It's the runtime's own traffic and can't be configured from the app. No report data can reach it.
  - The console exe makes no connections.
- Published single file (tested with an as-invoker manifest so no UAC was needed): **64.5 MB**. WebView2 starts from it.
- Dev run without UAC: `dotnet src\GalacticDiagnosis.Gui\bin\Release\net8.0-windows\win-x64\GalacticDiagnosis.dll [run.json]`.

## Done — Phase 1 (skeleton), 2026-10-18
- Solution `GalacticDiagnosis.sln`: `src/GalacticDiagnosis.Core` (collectors, model, WMI helpers), `src/GalacticDiagnosis.Report` (HTML), `src/GalacticDiagnosis.Console` (console, `GalacticDiagnosis-Console.exe`, `requireAdministrator` manifest).
- `ICollector` + `CollectorRunner`: parallel, per-collector timeout and try/catch → "Couldn't check" instead of a crash.
- Collectors: Hardware (OS, CPU, GPU + real VRAM from registry, RAM sticks, board, BIOS), Disk space.
- Output: `Reports\<LastName>_<date>_<before|after>.html` + `.json` next to the exe. Intake prompts, or `--name`, `--complaint`, `--after`, `--no-prompt`.
- Tested on the dev PC only (Ryzen 9950X desktop, not elevated).

## Done — Phase 2 (health data), 2026-10-18
- Drive health: `MSFT_PhysicalDisk` health status + `MSFT_StorageReliabilityCounter` (wear, temperature, power-on hours, uncorrected errors; admin only), drive letters via `MSFT_Partition`, USB/SD drives skipped, warning when Windows is on a spinning hard drive.
- Battery: `Win32_Battery` check, then `powercfg /batteryreport /xml`. Health % = full charge ÷ design capacity (<70% warning, <50% problem). The section is hidden on desktops (`NotApplicable`).
- Crashes (30 days): BugCheck 1001 + Kernel-Power 41 (blue screens, with plain-English hints for common stop codes), 41/6008 without a bugcheck (hard resets), Application Error 1000 grouped by program, plus `C:\Windows\Minidump`.
- `tests/GalacticDiagnosis.Tests`: battery XML parsing and thresholds, bugcheck parsing (13 tests).
- Verified elevated and unelevated on the dev PC (1 SATA HDD + 3 NVMe). **Not yet verified:** a real battery report (needs a laptop) and minidump parsing on a PC that has blue-screened.
- WMI gotcha: `SELECT * FROM MSFT_StorageReliabilityCounter` returns nothing even as admin. Counters only come through each disk's association (`ManagementObject.GetRelated`), and without admin that returns empty instead of throwing.
- Samsung NVMe drives here report no PowerOnHours or error counts through this API; only temperature and wear.

## Done — Phase 3 (cleanup data), 2026-10-18
- Startup time: Diagnostics-Performance event 100 (admin only), with fields read by name. Reports the last boot plus the median of the last 10 (90 days). Under 60 s is good, under 120 s needs attention, over that is a problem.
- Startup programs: HKLM/HKLM-32/HKCU Run keys + both Startup folders (.lnk resolved). Honours `Explorer\StartupApproved`, where an odd first byte means turned off in Task Manager. Embedded Authenticode check via WinVerifyTrust with no revocation/network. Flags unsigned (with AppData context) and anything in Temp. Files under %WINDIR% are treated as Windows/driver components, because they're catalog-signed. 12 or more enabled triggers a warning.
- Windows updates: newest `Win32_QuickFixEngineering` date (45/90-day thresholds), version/build from the registry, pending-restart keys.
- Antivirus: `root\SecurityCenter2` productState decode; flags none on, out-of-date definitions, or two AVs running at once.
- Activation: `SoftwareLicensingProduct` filtered to Windows with an installed key (~0.4–0.6 s here), plus edition and channel.
- 37 tests total.
- Verified elevated and unelevated on the dev PC. Elevated: last boot 37 s, median 31 s, with all 10 sections readable. Unelevated, startup time correctly shows "run as administrator".

## Done — Phase 4 (intake and comparison), 2026-10-18
- Intake order: name → before/after → complaint (before) or "what was done" (after; `--work`). An after run takes the complaint from its before run.
- `RunFile` (Core) saves and loads the JSON. Section `Items` and `Metrics` are `init`-able so they survive deserialization.
- `Comparison.Build(before, after)`: a table of known metrics with friendly labels, units, better/worse direction and a noise tolerance (boot ±2 s, disk ±0.5 GB). Metrics without a definition (temperatures, 30-day crash counts) are left out, and their sections still show verdict changes.
- `ComparisonReport`: Results (one headline per area), Measurements, Area by area, Still needs attention. It warns if the two runs come from different PCs.
- An after run auto-finds the latest before run from the same PC in `Reports` (preferring the same customer name) and writes `<Name>_<date>_comparison.html`. Manual: `GalacticDiagnosis --compare before.json after.json`.
- 43 tests. Verified the auto-compare on a real before/after pair, and checked the rendered page with a doctored "slow PC" before file.

## Done — Phase 5 (polish), 2026-10-18
- README: screenshots (`docs/images/`), a what-it-checks table, usage, SmartScreen note and build steps.
- Sample pages in `docs/sample/` are built from hand-written, generic data for a laptop (`sample-before.json` / `sample-after.json`). No real machine data is used.
- The activation section maps internal edition IDs to retail names (Core → Windows Home, Professional → Windows Pro).
- `--render <run.json>` rebuilds a report from its data.
- Published single-file exe (33.8 MB) verified elevated on the dev PC: all 10 sections read.
- Screenshots were made with headless Edge (`--headless=new --screenshot --force-device-scale-factor=2`), then the empty page background was cropped off.

## Still untested
- Battery on a real laptop, a SATA SSD, and a PC with recent blue screens (see the plan's testing checklist).
- Verdict wording with a non-technical reader.

## Build / run
- Publish: `dotnet publish src\GalacticDiagnosis.Console -c Release -o publish` → `publish\GalacticDiagnosis-Console.exe` (~34 MB, self-contained, single file).
- Dev run without UAC: `dotnet src\GalacticDiagnosis.Console\bin\Release\net8.0-windows\win-x64\GalacticDiagnosis-Console.dll --no-prompt --name "Test User"` (running the dll through `dotnet` skips the admin manifest).
- `nuget.config` is required: the machine's global NuGet config has no package sources.
