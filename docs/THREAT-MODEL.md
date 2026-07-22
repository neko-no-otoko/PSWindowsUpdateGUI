# Threat model

| Threat | Primary mitigation |
| --- | --- |
| Input becomes privileged code | No arbitrary script feature; fixed commands and typed models |
| Wrong update is changed | UpdateID plus revision revalidation immediately before mutation |
| Remote stage overwrites user data | Hash-versioned directory, ownership marker, restrictive ACL, no overwrite |
| Credential disclosure | No password arguments/files/logs; Credential Manager for optional SMTP persistence |
| Tampered offline catalog | HTTPS download plus WUA's mandatory Microsoft signature validation |
| Tampered exported payload | Empty destination, Authenticode verification for signable files, SHA-256 manifest |
| Policy lockout | Allowlisted values/ranges, preview, confirmation, automatic backup/restore |
| Concurrent servicing | Machine-wide mutex and WUA installer-busy check |
| Destructive reset | Service-state tracking and timestamped directory rename instead of deletion |
| Abandoned scheduled/remote state | Versioned manifests, final state journal, owner/hash-checked cleanup |

Local administrators and SYSTEM are inside the trust boundary: either can replace the
EXE, alter WUA state, scheduled tasks, or protected files. The app does not attempt to
defend a machine from its own administrators.
