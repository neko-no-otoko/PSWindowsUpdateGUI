# Architecture

## Trust boundaries

The WPF process runs elevated and hosts a Windows PowerShell 5.1 STA runspace. The
runspace imports only the verified embedded PSWindowsUpdate DLL. A fixed command
registry allows the 19 public cmdlets; user values are added as typed parameters.

```text
WPF controls
  -> validated invocation dictionary
    -> allowlisted AddCommand/AddParameter pipeline
      -> isolated Windows PowerShell 5.1 runspace
        -> verified PSWindowsUpdate.dll
          -> Windows Update Agent / WinRM / Task Scheduler
```

The command preview is a renderer for human review. It is never parsed back or used as
the executable command.

## Vendored module

The original `.nupkg` and a per-file hash manifest are embedded resources. Startup
copies the package into a randomly named restricted directory, verifies the package,
performs Zip Slip-safe extraction, verifies each required file, then validates
Authenticode signatures. The binary is imported by absolute path.

.NET Framework cannot unload an imported assembly. Cleanup is attempted at shutdown;
a subsequent launch removes stale runtime directories after handles are released.

## Command catalog

The allowlist fixes the public command surface. Runtime `Get-Command` metadata supplies
parameter sets, mandatory flags, CLR types, validation sets, and validation ranges to
the Advanced UI. Tests compare this live catalog with all allowlisted commands.

## State

Settings and redacted logs are portable sidecars beside the EXE. Secret values remain
inside `SecureString`/`PSCredential` instances, except when the user explicitly invokes
the upstream Credential Manager feature.
