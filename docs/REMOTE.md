# Remote administration

Remote mode manages one Windows 11 x64 DNS target at a time. The target must already
have secure WinRM and administrative access configured. The app never changes
TrustedHosts, listeners, certificates, firewall rules, authentication, UAC, or policy.

Use domain Kerberos/Negotiate for HTTP or a trusted certificate for HTTPS:

```powershell
.\PSWindowsUpdateGUI.exe status --computer pc01.contoso.test --output json
.\PSWindowsUpdateGUI.exe scan --computer pc01.contoso.test --use-ssl --type driver --output json
```

The preflight checks DNS, WinRM, Windows build/architecture, remote administrator
token, free space, clock, Windows Update service, reboot state, Task Scheduler, and SMB
staging access.

The exact running EXE is copied to an ACL-restricted, hash-versioned directory under
the target's `%ProgramData%\PSWindowsUpdateGUI\Remote`. An ownership marker and remote
SHA-256 verification are required. Existing foreign markers or differing binaries are
never overwritten. The target executes WUA locally, avoiding the limited direct-remote
WUA interface set.

Immediate operations remove their owned stage after completion. Scheduled jobs retain
the stage and are reconciled by `job list`/`job cleanup`; cleanup refuses a marker or
hash mismatch. Credentials use the current Windows identity and are never staged.
