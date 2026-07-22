# Native operation coverage

The GUI, CLI, scheduled runner, and remote worker share typed request models,
validation, and the operation catalog below.

| Area | Operations | Identification and safety controls |
| --- | --- | --- |
| Discovery | `scan` | Typed source and update-type filters, validated WUA criteria, structured results |
| Update actions | `download`, `install`, `hide`, `unhide` | Exact update GUID and revision, immediate applicability re-scan, EULA gate, plan and confirmation |
| Removal | `uninstall` | Exact WUA identity and uninstall capability check; explicit package path for DISM/WUSA fallback |
| Health | `history`, `status` | WUA history, API version, installer activity, last result, reboot state, service health, target clock |
| Sources | `services` | List sources, register Microsoft Update, or remove an explicitly identified service |
| Offline | `offline-scan`, `export-payload` | Microsoft signature validation, metadata-only notice, empty export directory, hashes and signature results |
| Administration | `policy`, `maintenance` | Allowlisted policy schema, preview, backup/restore, recoverable component reset |
| Automation | `job` | Versioned fixed-action manifest, ACL-restricted storage, EXE hash, exact update identities |
| Reporting | `report` | Validated SMTP settings, redaction, memory-only or explicitly confirmed Credential Manager secret |
| Remote execution | Common `--computer` and `--use-ssl` options | Secure WinRM preflight, exact staged EXE hash, ownership marker, no automatic security-policy changes |

Catalog tests require all 15 public operation groups to remain unique, documented,
and non-empty. Scheduled jobs accept only schema-validated fixed operations; executable
script text is never accepted.
