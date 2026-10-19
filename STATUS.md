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

## Done — Phase 8 (check flow), 2026-10-18
- **New check** is the start page. It has customer name, job number, Checked by (remembered in the settings file), Before/After, and the complaint (before) or what was done (after).
  - An after check lists this PC's before checks and pre-selects the console's pick (latest, preferring the same customer). The technician can pick another or *Don't compare*.
- **Running:** one row per check, with a spinner until it reports, then a verdict dot, label, summary and time. There's a progress bar and Cancel. New check and Open report are disabled while a check runs. Cancel returns to the filled-in form with "Nothing was saved".
- The finished run opens in the report viewer, on the comparison tab for an after run.
- Model: `Intake.JobNumber` and `Intake.TechnicianName` print in both report headers as Job and Checked by. The job number goes in file names (`Smith_J1042_2026-10-18_before`). `DiagnosticRun.ComparedWith` records the before file. An after report now shows Work done.
- The console gains `--job` and `--tech`.
- 63 tests, including the `CollectorRunner` order, timeout and cancel tests.
- Tested via UI Automation (unelevated dev run). Before → after through the GUI wrote `User_J1042_2026-10-18_before`, `User_2026-10-18_after` and `_comparison`, which are the console's names. **Not yet tested:** a GUI check elevated (needs UAC), and Cancel by hand (an unelevated run finishes in about a second; covered by a unit test).

## Done — Phase 9 (checklist, history, PDF, notes), 2026-10-18
- **Technician checklist** (`Core/Checklist.cs`): 10 common jobs, each tagged with the sections it usually fixes.
  - On the after form, jobs that match the before check's problems get a *Suggested* tag. Nothing is pre-ticked.
  - Ticked jobs go in `Intake.WorkItems` (JSON `work_items`) and print as a list above the technician's own notes, on the after report and the comparison.
- **Customer summary** (`SummaryReport`): one page covering what you told us, what we did, what changed, still needs attention (or what we found), and a compact "Good" list. It's a third tab in the viewer.
- **Report viewer:**
  - **Edit notes:** complaint for a before run, checklist plus notes for an after run. Saving rewrites the JSON, HTML and comparison.
  - **Save as PDF:** `PrintToPdfAsync`, always light, A4 on metric regions and Letter elsewhere.
  - **Leave a copy:** saves the summary and before/after PDFs to the Desktop.
  - An after run re-renders its comparison from `compared_with` when the before file is still there. Older runs fall back to the saved `_comparison.html`.
- **History** (Ctrl+H): every run in `Reports\`, newest first, with search (customer, job, computer, date, complaint) and a worst-verdict dot. You can Open one, Compare any two (the older counts as before) or Show in folder.
- A green notice banner confirms saves.
- 68 tests.
- Tested via UI Automation (unelevated dev run):
  - The checklist with suggestions, and the summary tab.
  - Save as PDF wrote a 1-page summary PDF.
  - Leave a copy wrote both PDFs to the Desktop (1 and 2 pages). They were deleted after the test.
  - Edit notes updated the JSON and the comparison.
  - The History search and comparing two runs.
- **Note:** "Leave a copy" uses the Desktop of the account the tool runs as. If a technician elevates with a different admin account, the files land on that account's Desktop.

## Done — Phase 10 (fix-it shortcuts, Check again, Copy summary), 2026-10-18
- **Fix it panel** (beside the report, toggled from the toolbar): one card per section that needs attention.
  - It shows only for a run made on this PC, because the shortcuts act on this PC.
  - The shortcuts (`Gui/Services/FixIts.cs`) follow the table in GUI_PLAN.md. Settings pages and Windows Security open through Explorer as the signed-in user; Device Manager, Disk Management, Disk Cleanup and Task Manager open elevated like the app. Drive health adds a back-up note, and the battery report is generated locally by `powercfg`.
  - `perfmon /rel` hands off to an Explorer Control Panel window (Reliability Monitor) and exits straight away.
- **Check again:** re-runs one collector and shows the new verdict and summary under the finding. It never changes the saved files.
- **Copy summary** (`Report/SummaryText.cs`): a plain-text version of the customer summary on the clipboard.
- `ComparisonReport.Headlines` is shared by the HTML Results list and the text summary.
- Open in browser and Show in folder are icon-only buttons now, to fit the toolbar.
- 70 tests.
- Tested via UI Automation (unelevated dev run):
  - The panel showed Crashes with Reliability history; the shortcut opened Reliability Monitor (closed after the test).
  - Check again updated the card.
  - Copy summary text was checked from the clipboard.
  - The toggle hides the panel.
- **Not yet tested:** the other shortcuts (each needs a PC with that finding, or an elevated run).

## Done — Phase 11 (polish, release prep), 2026-10-18
- Version 0.2.0.
- Keyboard: Ctrl+N new check, Ctrl+Enter start, Ctrl+H history, Ctrl+O open. Window size and theme are remembered (Phase 7).
- Checklist entries wrap their *Suggested* tag instead of clipping it.
- History says "in the Reports folder next to the tool"; the full path is in the tooltip. This keeps local paths out of screenshots.
- Hidden `--demo` switch: shows the sample PC's name (`SAMPLE-PC`) and an Administrator badge, so README screenshots made from `docs/sample` contain no real machine data.
- Console `--summary <run.json>` writes the one-page customer summary. Its usage text now names `GalacticDiagnosis-Console`.
- Sample data: `sample-after.json` gained `work_items`, a job number, a technician and `compared_with`. The sample pages were re-rendered, and `docs/sample/sample-summary.html` was added.
- README rewritten for the app: GUI screenshots in both themes (`docs/images/gui-*.png`, from `--demo` with sample data only), the two exes, keyboard, an honest note on network use, and build steps.
- **Released v0.2.0**, 2026-10-18: PRs #7 to #12 merged in order, both exes published from main (GUI 64.6 MB, console 34.1 MB) and attached with their SHA-256 hashes.

## Done — Elevated GUI test (v0.2.0 release exe), 2026-10-18
- Ran the released `GalacticDiagnosis.exe` as administrator on the dev PC, driven by UI Automation from an elevated PowerShell. UIPI blocks an unelevated script from driving an elevated window.
- The header shows the Administrator badge. All 10 sections ran as admin, with startup time and drive power-on hours readable. A whole check takes about a second here.
- **Cancel**, pressed straight after Start, returned to the form in 0.2 s and saved nothing.
- **Before → after** with job J2001: the after form picked the before check, marked the checklist *Suggested* tags from its findings, and opened on Before vs after. It wrote `User_J2001_2026-10-18_before`, `_after` and `_comparison`. Customer summary and History also worked.
- The test files stayed outside the repo.

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
- An after run auto-finds the latest before run from the same PC in `Reports` (preferring the same customer name) and writes `<Name>_<date>_comparison.html`. Manual: `GalacticDiagnosis-Console --compare before.json after.json`.
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
- Fix-it shortcuts other than Reliability history, the battery report on a laptop, and "Leave a copy" when elevated with a different admin account.
- High-DPI scaling (125%/150%) and an older Windows 10 PC without WebView2 (the fallback was only simulated with `--no-webview2`).

## Build / run
- Publish: `dotnet publish src\GalacticDiagnosis.Gui -c Release -o publish` → `publish\GalacticDiagnosis.exe` (~65 MB), and `dotnet publish src\GalacticDiagnosis.Console -c Release -o publish` → `publish\GalacticDiagnosis-Console.exe` (~34 MB). Both are self-contained single files.
- GUI dev run without UAC: `dotnet src\GalacticDiagnosis.Gui\bin\Release\net8.0-windows\win-x64\GalacticDiagnosis.dll [run.json] [--demo] [--no-webview2]`.
- Dev run without UAC: `dotnet src\GalacticDiagnosis.Console\bin\Release\net8.0-windows\win-x64\GalacticDiagnosis-Console.dll --no-prompt --name "Test User"` (running the dll through `dotnet` skips the admin manifest).
- `nuget.config` is required: the machine's global NuGet config has no package sources.
