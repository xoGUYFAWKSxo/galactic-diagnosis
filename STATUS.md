# Galactic Diagnosis — Status

Plan: [docs/BUILD_PLAN.md](docs/BUILD_PLAN.md)

## Done — Phase 1 (skeleton), 2026-10-18
- Solution `GalacticDiagnosis.sln`: `src/GalacticDiagnosis.Core` (collectors, model, WMI helpers), `src/GalacticDiagnosis.Report` (HTML), `src/GalacticDiagnosis.App` (console, `requireAdministrator` manifest).
- `ICollector` + `CollectorRunner`: parallel, per-collector timeout and try/catch → "Couldn't check" instead of a crash.
- Collectors: Hardware (OS, CPU, GPU + real VRAM from registry, RAM sticks, board, BIOS), Disk space.
- Output: `Reports\<LastName>_<date>_<before|after>.html` + `.json` next to the exe. Intake prompts, or `--name`, `--complaint`, `--after`, `--no-prompt`.
- Tested on the dev PC only (Ryzen 9950X desktop, not elevated).

## Next — Phase 2 (health data)
Drive health (`MSFT_PhysicalDisk` + `MSFT_StorageReliabilityCounter`), battery (`powercfg /batteryreport /xml`), crashes (event IDs 41/6008/1001/1000 + Minidump, last 30 days).

## Build / run
- Publish: `dotnet publish src\GalacticDiagnosis.App -c Release -o publish` → `publish\GalacticDiagnosis.exe` (~34 MB, self-contained, single file).
- Dev run without UAC: `dotnet src\GalacticDiagnosis.App\bin\Release\net8.0-windows\win-x64\GalacticDiagnosis.dll --no-prompt --name "Test User"` (running the dll through `dotnet` skips the admin manifest).
- `nuget.config` is required: the machine's global NuGet config has no package sources.
