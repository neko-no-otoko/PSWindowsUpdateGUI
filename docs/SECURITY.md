# Security model

## Trust boundaries

- The EXE is administrator code and must be obtained from a trusted release.
- WUA, WinRM, Task Scheduler, DISM, WUSA, and Credential Manager are Windows trust boundaries.
- Update servers, offline catalogs, remote hosts, SMTP servers, and exported files are external inputs.

## Controls

- The build verifies Microsoft Authenticode on `wuapi.dll`; WUA validates the Microsoft
  signature on registered `wsusscn2.cab` catalogs.
- Update mutations require exact GUID and revision identities and an immediate re-scan.
- History-driven removal first runs a non-mutating uninstall plan for the exact installed
  GUID and revision. The modifying control stays disabled until Windows reports that
  revision as uninstallable, and changing the target or update source invalidates the plan.
- A machine-wide mutex plus WUA `IsBusy` prevents overlapping app modifications.
- Search criteria have a length-limited allowlisted grammar. Regex evaluation has a timeout.
- CLI arguments are parsed as data. No update input is interpolated into executable code.
- The WinRM transport script is fixed and encoded by the application. Typed operation
  data is serialized as JSON over the child process standard-input stream; user values
  are never appended to the PowerShell command line or interpolated into script text.
- Staged remote files and scheduled manifests use SYSTEM/Administrators-only ACLs,
  ownership markers, executable hashes, schema validation, and narrow paths.
- Policy names, types, and ranges are allowlisted. A backup is written before application.
- Component reset renames data stores to recoverable timestamped backups instead of deleting them.
- Exported WUA payloads are written to an empty directory, hashed, and Authenticode-checked when signable.
- Passwords are redacted and SMTP secrets exist only in memory or Windows Credential Manager.
- Raw scripts, execution-policy changes, TrustedHosts changes, and automatic reboot are absent.

## Windows Update orchestrator

Microsoft documents WUA operations as separate from the Windows Settings orchestrator.
The app displays that limitation, checks WUA installer activity, and never silently
disables the orchestrator. Administrators must avoid running Windows Settings update
actions concurrently.

## Cancellation

Read and transfer operations request WUA abort. Installation/uninstallation abort is
best effort; after commit begins, the UI describes cancellation as stopping monitoring
and requires the final machine state to be checked.
