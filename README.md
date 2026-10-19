# Galactic Diagnosis

A Windows PC health check for repair technicians. Run it from a USB stick on a customer's PC and it writes a plain-English HTML report (print it to PDF from any browser) plus a JSON file for before/after comparison.

- One self-contained `.exe`. Nothing needs installing on Windows 10 or 11.
- Reads only built-in Windows sources: WMI, the event log and the registry.
- Makes no network calls. Nothing leaves the machine.

Work in progress. See [STATUS.md](STATUS.md) for what's done and [docs/BUILD_PLAN.md](docs/BUILD_PLAN.md) for the plan.

## Build

```
dotnet publish src\GalacticDiagnosis.App -c Release -o publish
```

This produces `publish\GalacticDiagnosis.exe`. Run it as administrator. Reports are saved to a `Reports` folder next to the exe.

## Before and after

Run it once before the repair ("before") and again afterwards ("after"). The after run finds the matching before run in `Reports` and also writes a `_comparison.html` page, e.g. "Startup time: 125 s → 37 s". To compare any two runs by hand:

```
GalacticDiagnosis.exe --compare Smith_2026-10-15_before.json Smith_2026-10-18_after.json
```

## License

MIT
