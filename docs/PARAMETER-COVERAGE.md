# Legacy capability migration

Version 2 preserves user workflows rather than PSWindowsUpdate cmdlet syntax.

| Former command | Native replacement |
| --- | --- |
| `Get-WindowsUpdate` | `scan`, `download`, `install`, `hide`, `unhide` |
| `Remove-WindowsUpdate` | `uninstall`, with explicit DISM/WUSA package fallback |
| `Get-WUOfflineMSU` | `download` then `export-payload` |
| `Get-WUApiVersion`, `Get-WUInstallerStatus`, `Get-WURebootStatus` | `status` |
| `Get-WUHistory`, `Get-WULastResults` | `history` and operation results |
| `Get-WUJob`, `Invoke-WUJob` | `job list/create/run/cancel/cleanup` |
| `Add/Get/Remove-WUServiceManager` | `services add-microsoft-update/list/remove` |
| `Get-WUSettings`, `Set-WUSettings` | `policy get/set/restore` |
| `Set-PSWUSettings` | `report configure/test/send` |
| `Reset-WUComponents` | `maintenance reset-components` |
| `Enable-WURemoting` | secure preflight and documentation; configuration is never changed automatically |
| `Update-WUModule` | application release, checksum, and provenance workflow |

The test catalog contains all 19 former manifest exports and fails on a missing mapping.
Arbitrary scheduled scripts are deliberately removed; scheduled jobs contain only a
versioned action, exact update identities, source, timing, EULA choice, and EXE hash.
