# Galactic Diagnosis — Status

Plan: [docs/BUILD_PLAN.md](docs/BUILD_PLAN.md)

## Done — Phase 1 (skeleton), 2026-10-18
- Solution `GalacticDiagnosis.sln`: `src/GalacticDiagnosis.Core` (collectors, model, WMI helpers), `src/GalacticDiagnosis.Report` (HTML), `src/GalacticDiagnosis.App` (console, `requireAdministrator` manifest).
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

## Next — Phase 3 (cleanup data)
Boot time (Diagnostics-Performance event 100), startup programs (Run keys + Startup folders, flag unsigned / Temp / AppData), activation (`SoftwareLicensingProduct`, slow), updates (`Win32_QuickFixEngineering`), antivirus (`root\SecurityCenter2`).

## Build / run
- Publish: `dotnet publish src\GalacticDiagnosis.App -c Release -o publish` → `publish\GalacticDiagnosis.exe` (~34 MB, self-contained, single file).
- Dev run without UAC: `dotnet src\GalacticDiagnosis.App\bin\Release\net8.0-windows\win-x64\GalacticDiagnosis.dll --no-prompt --name "Test User"` (running the dll through `dotnet` skips the admin manifest).
- `nuget.config` is required: the machine's global NuGet config has no package sources.
