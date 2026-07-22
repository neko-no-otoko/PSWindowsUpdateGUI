# PSWindowsUpdate GUI

PSWindowsUpdate GUI is a portable, elevated Windows 11 interface for
[PSWindowsUpdate](https://github.com/mgajda83/PSWindowsUpdate). It provides a guided
scan/select/install workflow and a complete metadata-driven editor for every public
cmdlet and module-specific parameter in PSWindowsUpdate 2.2.1.5.

> [!WARNING]
> This is an administrator tool. Installing or removing updates, changing update
> policy, resetting components, scheduling scripts, and rebooting a computer can
> interrupt users or make a system temporarily unavailable. Review the redacted
> command preview and maintain tested backups.

## Highlights

- One portable `PSWindowsUpdateGUI.exe`; no local module or .NET installation.
- Pinned offline PSWindowsUpdate 2.2.1.5 package with full SHA-256 verification.
- Authenticode verification of the upstream binary and signed script-bearing files.
- Typed in-process Windows PowerShell 5.1 invocation—ordinary inputs are never
  concatenated into scripts.
- Guided update scan, selection, download, installation, hide, and unhide actions.
- All 19 exported cmdlets and their parameter sets in the Advanced page.
- Local or one native remote target at a time.
- Memory-only ordinary credentials and redacted portable logs.
- Accessible WPF layout with keyboard navigation, high contrast, and DPI awareness.

## Requirements

- Windows 11 x64, build 22000 or newer.
- An account that can approve UAC elevation.
- Windows PowerShell 5.1, included with Windows 11.
- For remote operation: preconfigured WinRM plus administrative access to the target.

PowerShell 7 is not used because PSWindowsUpdate 2.2.1.5 declares the Windows
PowerShell Desktop edition and loads Windows Update Agent components through its
binary module.

## Quick start

1. Download `PSWindowsUpdateGUI.exe` and `PSWindowsUpdateGUI.exe.sha256` from a release.
2. Verify the checksum:

   ```powershell
   (Get-FileHash .\PSWindowsUpdateGUI.exe -Algorithm SHA256).Hash
   ```

3. Run the EXE and approve the administrator prompt.
4. Keep **This computer** selected or enter a remote DNS host name and select
   **Test target**.
5. Choose an update source and select **Scan**.
6. Select update rows and choose the intended action. Guided installation never
   automatically reboots.

The executable is unsigned by default. Compare its checksum with the GitHub Release
asset and review the attached SPDX SBOM and build provenance.

Release `v1.0.0` is intentionally gated on the completed local and remote Windows 11
VM checklist in [docs/VM-ACCEPTANCE.md](docs/VM-ACCEPTANCE.md). A CI artifact is not a
production release until that checklist is signed off.

## Interface

| Page | Purpose |
| --- | --- |
| Updates | Scan, filter, select, download, install, hide, or unhide updates. |
| History & Status | History, last result, WUA version, installer/reboot state, jobs, services, and settings. |
| Services & Policies | Route persistent service and policy changes through a validated advanced form. |
| Offline & Maintenance | Offline MSU, uninstall, reset, remoting, module servicing, and scheduled jobs. |
| Advanced | All public cmdlets, parameter sets, switches, ranges, arrays, credentials, and previews. |
| Logs | Live redacted operation history and export/clear controls. |

Optional switches have an explicit **Include** control plus a True/False value. This
preserves PowerShell's distinction between an omitted parameter and a bound false
switch, which is important for actions such as unhide.

## Remote behavior

Remote mode intentionally follows the module's native `ComputerName` surface.
Parameter sets without `ComputerName` remain visible but disabled remotely.

Native remote download/install jobs require PSWindowsUpdate on the target. When the
pinned version is absent, the GUI asks before copying it into the versioned Windows
PowerShell module directory. It writes an ownership marker, refuses to overwrite a
different existing binary, and only removes copies carrying that exact marker.

The GUI does not change TrustedHosts, WinRM listeners, firewall rules, authentication,
or remote UAC policy automatically. See [Remote administration](docs/REMOTE.md).

## Portable data and secrets

When the EXE directory is writable, non-secret state and rotating logs are saved in
`PSWindowsUpdateGUI.Data` beside the executable. If it is not writable, the app runs
ephemerally and displays a warning.

Passwords are not written to settings, logs, previews, exports, arguments, or temporary
request files. `Set-PSWUSettings -SmtpCredential` deliberately uses the upstream
module's Windows Credential Manager behavior and therefore persists when selected.

## Build

The repository pins .NET SDK 10.0.300 as a build tool but targets .NET Framework 4.8,
which is available on Windows 11. From Windows PowerShell:

```powershell
$env:DOTNET_EXE = 'C:\path\to\dotnet.exe' # optional
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build\Build.ps1
```

The build verifies the vendored package, restores locked dependencies, compiles with
warnings as errors, runs tests, and writes release assets under `artifacts/release`.
See [Building](docs/BUILDING.md).

## Security and support

- Read [the security model](docs/SECURITY.md) before deployment.
- Report vulnerabilities according to [SECURITY.md](SECURITY.md), not in public issues.
- Troubleshooting and known limitations are in [docs/TROUBLESHOOTING.md](docs/TROUBLESHOOTING.md).
- PSWindowsUpdate itself is maintained upstream; this project does not replace its
  issue tracker.

## License

PSWindowsUpdate GUI is MIT licensed. The embedded PSWindowsUpdate module retains its
own MIT copyright and notice in [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt).
