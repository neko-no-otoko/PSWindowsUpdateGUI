# Windows 11 VM acceptance

Use clean x64 VMs with snapshots. Never run destructive acceptance on an unsnapshotted
physical machine.

## Local VM

- Verify a machine with no PSWindowsUpdate installation launches the single EXE.
- Run status, default/software/driver scans, history, services, validated criteria, and cancellation.
- Plan then install one exact software update; verify per-update result and reboot state.
- Plan then install one exact driver; compare provider/version/date before and after and test snapshot rollback.
- Test hide/unhide, WUA uninstall where supported, and explicit MSU/CAB fallback.
- Download/register current `wsusscn2.cab`; confirm security-only results and no payload claim.
- Download and export payloads to an empty directory; verify Authenticode and SHA-256 manifest behavior.
- Exercise policy preview/backup/set/restore and recoverable component reset.
- Exercise scheduled job completion, reboot-required state, cancellation, cleanup, and stale reconciliation.
- Verify report configuration never writes a password to portable data or logs.

## Remote VM

- Use preconfigured Kerberos and separately certificate-valid HTTPS WinRM.
- Verify preflight, ACL-restricted staging, remote SHA-256, scan, exact update plan/install,
  reboot/reconnect, scheduled job monitoring, and owner/hash-checked cleanup.
- Prove foreign files/markers are preserved and TrustedHosts/firewall/UAC remain unchanged.

## UI and failure recovery

- Keyboard-only navigation, screen reader labels, high contrast, 100-300% DPI, long titles.
- Unwritable EXE directory warning, offline operation, service unavailable, invalid criteria,
  hash/signature failure, timeouts, partial results, and pending reboot.

Promote the major prerelease only after every applicable item is recorded with VM build,
snapshot, update identities, result codes, logs, and reviewer sign-off.
