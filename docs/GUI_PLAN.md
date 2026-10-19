# Galactic Diagnosis — GUI Plan

## Goal

A windowed version of Galactic Diagnosis. The technician fills in the intake, watches the checks run, and reads the report in the same window, with no console and no separate browser. The rules from the original plan still hold:

- One portable file runs from a USB stick.
- It runs as administrator.
- It makes no network calls.
- It never changes the PC; it only reads.
- It saves HTML and JSON to `Reports\` next to the exe, as it does now.

## Key decisions

| Decision | Choice | Why |
|---|---|---|
| UI framework | **WPF** on .NET 8 | Ships with the Windows Desktop runtime, publishes self-contained as a single file, and is easy to theme. WinUI 3 is awkward to ship as a single file. Avalonia has no built-in web view. |
| Report viewer | **WebView2** (Edge engine) showing the existing HTML reports | Reuses `HtmlReport` and `ComparisonReport`, so the window shows exactly what the customer gets. The old WinForms `WebBrowser` control is the IE engine and breaks the CSS. |
| View model | CommunityToolkit.Mvvm | Small, uses source generators, and avoids hand-written `INotifyPropertyChanged` code. |
| Console version | **Keep it** as `GalacticDiagnosis-Console.exe` and ship both | It's the fallback when WebView2 is missing or the GUI misbehaves on an odd PC, and it keeps `--compare`, `--render` and `--no-prompt` scripting. |
| Shared logic | Move run, save and pairing logic out of `Program.cs` into the Report project | Both front ends call the same code, and the logic becomes unit-testable. |
| Branding | **Galactic Studio** look, matching Galactic Macro and Galactic Tune-Up | See *Branding and themes* below. |
| Themes | **Dark (default) and Light**, with a header toggle, following the Windows setting on first launch | Dark suits the galaxy branding and dim workshop benches. Printed and PDF reports are always light. |

## Branding and themes

- **App icon:** the seeded Galactic Studio spiral galaxy with a **repair badge** (a wrench crossed with a screwdriver) in the bottom-right corner. Same layout as the robot on Galactic Macro and the helmet on Tune-Up.
  - The badge is drawn in the Galactic Macro style: brushed-metal gradient tools, a dark navy outline, a cyan glow and a drop shadow.
  - It's built by `assets/make_icon.py`, adapted from `galactic-macro/assets/make_icon.py`, with `galaxy.py` copied from there unchanged so the galaxy is identical.
  - Outputs: `icon.png` (1024 px), a multi-size `icon.ico` (16–256 px) and a GitHub social preview.
  - The icon is used for the exe, the window title bar and taskbar, the report's browser-tab favicon, and the README header.
- **Palette** (taken from the existing icons):
  - Background: navy `#0C1028`, panels `#121A3A`.
  - Text: metal white `#ECF2FC`, muted text `#8C9CB8`.
  - Accent: cyan `#5AE6FF`.
  - Alert: coral `#FF5C78`.
  - The light theme uses the same accents on white and pale grey.
  - Verdict colours stay green, amber and red in both themes, so "bad" always reads as bad.
- **The report in dark mode:** add a `prefers-color-scheme: dark` block to the report CSS, screen only. WebView2's `PreferredColorScheme` follows the app theme. A customer opening the HTML on their own PC gets their system's theme, and print or PDF is always light.

## Architecture

```
GalacticDiagnosis.Core     collectors, model, comparison
GalacticDiagnosis.Report   HtmlReport, ComparisonReport (+ dark CSS, print CSS), + new ReportStore / RunSession
GalacticDiagnosis.Console  today's App project, renamed; thin wrapper over RunSession
GalacticDiagnosis.Gui      new WPF + WebView2 project → GalacticDiagnosis.exe
assets/                    galaxy.py, make_icon.py, icon.png, icon.ico
```

- **`ReportStore`** (moved from `Program.cs`): the `Reports\` folder path, `LastName` and `FileSafe` file naming, `UniqueStem`, `FindBeforeRun`, `SaveRun` and `SaveComparison`. It also adds `ListRuns()` for the history screen.
- **`RunSession`**: takes an `Intake`, runs the collectors through `CollectorRunner.RunAllAsync` (its `onDone` callback already reports each section as it finishes), saves the files and, for an after run, the comparison. It returns the paths. It also takes a `CancellationToken`.
- **WebView2 setup:**
  - Put the user-data folder in `%TEMP%\GalacticDiagnosis.WebView2`, not next to the exe, so the USB stick doesn't fill with browser cache. Delete it on exit (best effort).
  - Load reports from local files.
  - Cancel every navigation that isn't a local report.
  - Turn off the SmartScreen reputation check, plus the context menu, dev tools and autofill.
- **Missing WebView2 runtime:** Windows 11 always has it, and Windows 10 has it if updated since 2021. If it's missing, show a banner and open the report in the default browser instead.
- **Settings:** theme, window size and technician name live in `GalacticDiagnosis.settings.json` next to the exe. They travel with the USB stick, not the customer's PC.

## Screens

The header bar on every screen shows the icon, *Galactic Diagnosis*, the machine name, an admin badge, the theme toggle, and **New check** / **History**.

1. **New check:** customer name, a **Before / After** toggle, and the complaint (before) or the technician checklist (after; see Phase 9).
   - For an after run, it shows the matching before run (date, customer, complaint) in a card, with *Choose a different one…*.
2. **Running:** the 10 checks as rows. Each starts with a spinner and turns into a coloured verdict dot plus the one-line summary as it finishes. There's an overall progress bar and a Cancel button.
3. **Report:** the WebView2 view with tabs **Report** and **Before vs after** (after runs only). The toolbar has **Save as PDF**, **Open in browser**, **Show in folder**, **Edit notes** and **New check**.
4. **History:** a table of the runs in `Reports\` (customer, date, before/after, machine, worst verdict), with a search box. Double-click a run to open it, or select two and press **Compare**.

## Phases

Each phase is one branch → PR, merged when you say so. The PRs are stacked: each phase's branch starts from the previous one, so work can continue before the earlier PRs merge. Merge them in order.

### Phase 6: Shared run pipeline (no visible change)

- Rename `GalacticDiagnosis.App` to `GalacticDiagnosis.Console` and set its assembly name to `GalacticDiagnosis-Console`.
- Move the logic into `ReportStore` and `RunSession` (in the Report project, since saving needs the renderers) and slim `Program.cs` down to argument parsing and console prompts.
- Add tests for file naming, `FindBeforeRun` pairing (same machine, prefer the same customer, latest wins) and `ListRuns`.
- **Done when:** the console exe behaves exactly as v0.1.1 does (same files, same names), the old tests pass and the new ones pass.

### Phase 7: Icon, themes and report viewer (de-risk first)

- Generate the icon (`assets/make_icon.py`) and add it to both exes.
- Create the WPF project:
  - The `requireAdministrator` manifest and the WebView2 settings above.
  - Dark and light theme resource dictionaries, with the header toggle.
  - A single window that opens an existing run (`.json`, re-rendered so old runs get the current layout) or a comparison.
- Add the dark-screen CSS block to the report.
- **Done when:**
  - The published single-file exe opens a sample report on this PC, in both themes.
  - A network monitor shows no connections from the app while viewing.
  - The missing-WebView2 fallback works (simulated).
  - The icon reads clearly at 16 px and 32 px.
- **Also record:** the published exe size. Expect about 60–70 MB.

### Phase 8: The check flow

- Build the New check, Running and Report screens.
- Live progress comes from the `onDone` callback, marshalled to the UI thread.
- **Technician name and job number** (idea 4): the technician name is set once and saved in the USB settings file. The job number is entered on the intake. Both print in the report header, and the job number goes in file names when it's given.
- An after run auto-pairs with its before run and opens the comparison tab.
- **Done when:** a full before → after cycle works in the GUI elevated and the saved files match what the console version writes.

### Phase 9: Technician checklist, history, PDF and notes

- **Technician checklist on the after-run intake:** tick boxes for common jobs plus a free-text box.
  - Jobs: removed startup programs, installed Windows updates, cleaned up disk space, removed malware or unwanted software, updated drivers, replaced or added hardware, backed up files, and so on.
  - The ticked items become the work-done text in plain English.
  - Items that match the before run's findings are pre-suggested; for example, if the before run flagged low disk space, *Cleaned up disk space* is highlighted. They're never pre-ticked.
  - The ticked items are also stored in the JSON, so the comparison report can list them.
- **History screen:** with search; compare any two runs.
- **Save as PDF:** uses `CoreWebView2.PrintToPdfAsync`, with `@media print` CSS so the PDF paginates cleanly and is always light.
- **Customer summary page** (idea 5): one non-technical page with At a glance, what we did and what still needs attention. It's saved as a PDF next to the full report.
- **Leave a copy for the customer** (idea 3): after an after run, offer to save the summary and comparison PDFs to the customer's Desktop.
- **Edit notes:** edit the work-done text after a run, then rewrite the JSON and HTML.
- **Done when:** the checklist text reads naturally in the comparison report, a PDF looks right on A4 and Letter, and edited notes survive reopening.

### Phase 10: Fix-it shortcuts, Check again, Copy summary

- **Fix-it shortcuts:** see the section below.
- **Check again** (idea 1): re-run one section in place.
- **Copy summary** (idea 2): a plain-text summary on the clipboard.
- **Done when:**
  - Every button in the fix-it table opens its tool from the elevated app.
  - Check again updates one section without touching the saved files.
  - The copied text pastes cleanly into Notepad.

### Phase 11: Polish and release v0.2.0

- Keyboard shortcuts, high-DPI checks, and remembered window size and theme.
- Update the README with the icon header and GUI screenshots in both themes (made-up data only).
- Update STATUS.md.
- Release with both exes and both SHA-256 hashes in the notes.

## Fix-it shortcuts (Phase 10)

When a section comes back yellow or red, the GUI's Report screen shows an **Open** button next to it that launches the Windows tool a technician would use to fix it. The buttons sit in a slim panel beside the report, not in the report itself, so the customer's copy is unchanged.

**The app still changes nothing.** It only opens the tool. The technician makes the change, then runs the after check, which proves the fix worked. That keeps the tool safe to run on any customer PC and keeps the before/after story honest.

| Finding | Button | Opens |
|---|---|---|
| Too many startup programs, slow boot | **Startup apps** | `ms-settings:startupapps` (Task Manager's Startup tab as a second button) |
| Low disk space | **Storage** / **Disk Cleanup** | `ms-settings:storagesense`, `cleanmgr /d C` |
| Updates out of date, restart pending | **Windows Update** | `ms-settings:windowsupdate` |
| Antivirus off or out of date | **Windows Security** | `windowsdefender:` |
| Windows not activated | **Activation** | `ms-settings:activation` |
| Basic Display driver (no GPU driver) | **Device Manager** | `devmgmt.msc` |
| Blue screens, app crashes | **Reliability history** | `perfmon /rel`, the timeline of crashes and installs, which is great for spotting what started it |
| Drive health warning | **Disk Management** | `diskmgmt.msc`. There's no fix to offer, so it also shows "Back up this PC's files before anything else." |
| Battery worn | **Full battery report** | the `powercfg /batteryreport` page this tool already generates, opened locally |

- **Cost:** small. It's a lookup table from section ID and verdict to launch commands, plus a panel.
- **Elevation:** Settings pages open in the signed-in user's context. MMC tools open elevated, because the app is elevated, which is what a technician wants anyway.

## Other GUI ideas (all approved; scheduled in Phases 8–10)

1. **Re-check one section:** a **Check again** button on each section re-runs just that check in a few seconds.
   - Use it right after a fix (for example, disabling startup items) to confirm it took, without a full after run.
   - It's shown live only and doesn't overwrite the saved before run.
   - It's cheap, because each collector already runs on its own.
2. **Copy summary:** one button copies a plain-text summary (customer, the At a glance lines, what was done, before → after headlines) to the clipboard, for pasting into an invoice, ticket system or text message to the customer. It stays offline because it's just the clipboard.
3. **Leave a copy for the customer:** after an after run, offer to save the comparison PDF to the customer's Desktop, so they keep a record without the technician emailing anything.
4. **Technician name and job number:**
   - An optional *Checked by* name (set once, saved in the USB settings file) is printed in the report header.
   - An optional job/ticket number field on the intake is printed in the report and used in file names, so reports line up with shop records.
5. **Customer summary page:** a one-page, non-technical PDF (At a glance, what we did, what still needs attention) next to the full detailed report. It's handy when the full report is more than the customer wants to read.

Not recommended: auto-update checks, emailing reports, or cloud backup of reports. They all need the network and would break the no-network rule.

## Risks

- **Exe size roughly doubles,** to about 60–70 MB. That's fine on a USB stick, and Phase 7 measures it first.
- **WebView2 runs elevated** because the whole app does. This is supported, and keeping its data in `%TEMP%` avoids the known profile-folder problems.
- **The WebView2 runtime contacts Microsoft by itself.** Phase 7 measured this (see STATUS.md):
  - The app's own process opens no connections.
  - Switches stop the runtime's background fetches and secure-DNS lookups, and a dead proxy plus a no-DNS rule block anything else that goes through its network stack.
  - One HTTPS connection from the WebView2 browser process to a Microsoft 365 address remains. It goes around those switches, and no setting we found turns it off.
  - Reports load as strings and every web request is refused, so no report data can reach it. The console exe makes no connections at all.
- **The icon needs Python with Pillow and NumPy** at build time only, the same as the other Galactic repos. The generated `icon.ico` is committed, so normal builds don't need Python.
- **Not tested yet:** older Windows 10 builds without WebView2. The fallback covers them, but it needs a real PC to confirm.
