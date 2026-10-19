# PC Diagnostic Tool — Build Plan

Oct 18, 2026

## Stack

Use C# on .NET 8, published as a self-contained single-file .exe. It runs on any Windows 10 or 11 machine without installing anything, and WMI, the event log, and the registry are all first-class in .NET. The cost is file size: about 70 MB, which doesn't matter on a USB stick.

The alternative is .NET Framework 4.8. It produces a roughly 1 MB exe that runs on every modern Windows machine, but you'd be writing against a framework that's frozen. Only switch if the file size bothers you.

Make it a console app first, with a GUI later if you want one. The customer never sees the tool, only the report, so a GUI buys nothing early on.

Set the app manifest to require administrator. Drive health, boot timing, and some event logs aren't readable without it.

## Data sources

Every report section reads from a built-in Windows source; nothing needs a third-party driver in v1.

| Section | Source | Notes |
| --- | --- | --- |
| Hardware | WMI: `Win32_Processor`, `Win32_VideoController`, `Win32_PhysicalMemory`, `Win32_BaseBoard`, `Win32_BIOS` | Easy. Start here. |
| Drive health | `MSFT_PhysicalDisk` + `MSFT_StorageReliabilityCounter` (namespace `root\Microsoft\Windows\Storage`) | Wear, temperature, power-on hours, and error counts for SATA and NVMe. The older `MSStorageDriver_FailurePredictStatus` only gives pass/fail for SATA. |
| Battery | `powercfg /batteryreport /xml /output <path>` | Design capacity vs. full charge capacity, cycle count. Skip on desktops. |
| Crashes | System log: ID 41, 6008, 1001 (source BugCheck). Application log: ID 1000. Files in `C:\Windows\Minidump`. | Last 30 days only. |
| Boot time | `Microsoft-Windows-Diagnostics-Performance/Operational`, event 100 | Boot duration in ms. The before/after headline number. |
| Startup programs | Registry Run keys (HKLM and HKCU), Startup folders | Flag unsigned executables and anything running from Temp or AppData. |
| Disk space | `DriveInfo` | Trivial. |
| Activation | `SoftwareLicensingProduct` where `LicenseStatus = 1` | Slow (several seconds). Run in parallel. |
| Updates | `Win32_QuickFixEngineering`, latest install date | Rough, but catches machines months behind. |
| Antivirus | `root\SecurityCenter2`, `AntiVirusProduct` | Client Windows only, not Server. |

Skip CPU temperatures for v1. LibreHardwareMonitor depends on the WinRing0 kernel driver, which Microsoft Defender reportedly began flagging as vulnerable in 2025 (verify before relying on it). NVMe drive temperature comes free from the reliability counters.

Optional later: bundle `smartctl` from smartmontools for fuller SMART data. It's GPL-licensed; shipping the unmodified binary with its license file is generally considered fine, but read the license first.

## Architecture

The solution has three projects:

- **Core:** one collector class per section, all implementing the same interface. Each returns raw data plus a verdict (Good, Warning, or Bad) and a plain-English sentence, e.g. "This drive has used 87% of its rated life. Replace it soon." Each collector gets its own try/catch and timeout, so a failure shows as "couldn't read" instead of crashing the run.
- **Report:** turns results into a single HTML file with inline CSS and no external files. Print to PDF from any browser; no PDF library needed.
- **App:** the console front end. It asks for intake fields (customer name, complaint, Before or After run), runs the collectors in parallel, and saves everything.

Output goes to a `Reports` folder on the USB stick, e.g. `LastName_2026-10-18_before.html`, plus a `.json` file of raw data. The JSON makes before/after comparison possible later.

Make no network calls. It keeps the tool simple, and "nothing leaves the machine" is a selling point when a customer asks what you ran.

## Build order

Phase 1 is a weekend; phases 2 and 3 take the most debugging because WMI output varies a lot between machines.

1. **Skeleton.** Solution, collector interface, hardware and disk space collectors, basic HTML report. Get one report rendering end to end first.
2. **Health data.** Drive health, battery, crashes. This is what diagnoses real problems.
3. **Cleanup data.** Boot time, startup programs, activation, updates, antivirus.
4. **Intake and comparison.** Intake prompts and JSON saving, then a comparison report that reads two JSON files and shows changes like "Boot time: 94s → 22s."
5. **Polish.** README with a sample-report screenshot, MIT license, and a GitHub release with the exe attached.

## Testing

Test on varied real hardware, not just your own PC. At minimum:

- [ ] A laptop, for the battery section
- [ ] A machine with a SATA SSD or hard drive
- [ ] A machine with NVMe
- [ ] A run without admin rights, confirming a clear message instead of a stack trace

A Windows VM is useful for clean runs, but it reports fake hardware, so it can't replace a real laptop.

## Two things to expect

**SmartScreen will warn about the unsigned exe.** Since you're the one running it, you can click past the warning. Code signing costs money every year and isn't worth it until other people start downloading the tool.

**Test the verdict wording on someone non-technical.** It's the part of the report customers actually read, and the easiest part to get wrong.
