# Windows 11 VM acceptance

Do not create the `v1.0.0` tag until this checklist has been completed against
snapshot-backed Windows 11 x64 virtual machines. Record the tester, date, Windows
build, VM snapshot identifiers, and evidence links in the release issue.

## Clean local VM

- Confirm PSWindowsUpdate is not installed in any system or user module path.
- Copy only `PSWindowsUpdateGUI.exe` to the VM and verify its published SHA-256.
- Start the application, approve UAC, and verify the displayed identity and elevation.
- Run scan-only against Windows Update and Microsoft Update without installing a module.
- Exercise select, download, install, hide, unhide, uninstall, report, and reboot-policy
  paths with disposable updates and explicit confirmation.
- Verify cancellation wording, offline behavior, an unwritable EXE directory, log
  redaction, log retention, exports, high contrast, keyboard-only use, screen reader
  labels, 200% DPI, and long update titles.
- Confirm the verified per-run module directory is removed after the process exits.

The optional integration runner uses the same compiled module runtime and PowerShell
host as the GUI. It records status operations and two driver scans as structured JSON:

```powershell
dotnet build .\tests\PSWindowsUpdateGui.Acceptance\PSWindowsUpdateGui.Acceptance.csproj -c Release -p:Platform=x64
Start-Process .\tests\PSWindowsUpdateGui.Acceptance\bin\x64\Release\net48\PSWindowsUpdateGui.Acceptance.exe -Verb RunAs -Wait -ArgumentList '--output=local-acceptance.json --install-first-safe-driver'
```

The install option excludes firmware/BIOS candidates, selects by exact UpdateID,
disables automatic reboot, and refuses to install while Windows reports a pending
reboot. Inspect and retain the generated report with the release evidence.

## Preconfigured remote VM

- Use Kerberos or certificate-validated WinRM HTTPS; do not add TrustedHosts.
- Confirm name resolution, Windows 11 x64, WinRM, administrator access, Windows
  PowerShell 5.1, Task Scheduler, SMB administrative-share access, clock, and free space.
- Run a native remote scan and inspect status/history commands.
- Confirm an existing matching 2.2.1.5 module is reused without modification.
- From a clean remote module state, approve staging and verify hashes plus the ownership
  marker before scheduling download/install work.
- Verify reconnect/job monitoring and reboot-recursion behavior.
- After all jobs finish, remove the app-owned copy and confirm a user-managed or
  nonmatching module is never overwritten or removed.
- End the session unexpectedly, reconnect, and reconcile the stale owned copy.

## Release gate

- Attach both completed checklists and any deviations to the release issue.
- Confirm CI, CodeQL, checksum, SPDX SBOM, third-party notices, and provenance.
- A maintainer creates and pushes `v1.0.0` only after all required items pass.
