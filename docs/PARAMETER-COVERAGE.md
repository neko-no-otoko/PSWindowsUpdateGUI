# Parameter coverage

The Advanced page derives its forms from the live metadata of the pinned module. The
catalog test fails if one of these exported commands is unavailable:

| Area | Commands |
| --- | --- |
| Updates | `Get-WindowsUpdate`, `Remove-WindowsUpdate`, `Get-WUOfflineMSU` |
| Status | `Get-WUApiVersion`, `Get-WUHistory`, `Get-WUInstallerStatus`, `Get-WUJob`, `Get-WULastResults`, `Get-WURebootStatus` |
| Services/settings | `Add-WUServiceManager`, `Get-WUServiceManager`, `Remove-WUServiceManager`, `Get-WUSettings`, `Set-WUSettings`, `Set-PSWUSettings` |
| Administration | `Invoke-WUJob`, `Update-WUModule`, `Enable-WURemoting`, `Reset-WUComponents` |

Control mapping:

- `SwitchParameter` and `Boolean`: Include plus True/False.
- `ValidateSet`: dropdown, or validated token input for arrays.
- numeric ranges: parsed numeric input with runtime range checking.
- `DateTime`: culture-aware date/time input.
- arrays: comma, semicolon, or newline-delimited tokens.
- `PSCredential`: password dialog backed by `SecureString`.
- `Hashtable`: `Key=Value` pairs separated by semicolons/newlines.
- paths, criteria, titles, and scripts: text input; title regexes are compiled with a
  bounded timeout before invocation.

PowerShell plumbing parameters (`ErrorVariable`, `OutVariable`, and similar) are owned
by the host. The UI exposes `Verbose`, `WhatIf`, module `Debuger`, and replaces
interactive `Confirm` with graphical confirmation summaries.
