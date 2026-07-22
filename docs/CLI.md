# CLI reference

Run from an elevated PowerShell or Command Prompt:

```text
PSWindowsUpdateGUI.exe <command> [options]
```

Commands are `scan`, `download`, `install`, `uninstall`, `hide`, `unhide`, `history`,
`status`, `services`, `offline-scan`, `export-payload`, `policy`, `maintenance`, `job`,
and `report`.

Common options:

- `--computer <dns-name>` and optional `--use-ssl`
- `--source default|windows-update|microsoft-update|managed-server|service`
- `--type all|software|driver`
- repeatable `--update <guid>:<revision>`
- `--criteria <validated WUA criteria>`
- `--plan`, `--yes`, `--output text|json|jsonl`, and `--timeout <seconds>`

Examples:

```powershell
.\PSWindowsUpdateGUI.exe scan --source default --type driver --output json
.\PSWindowsUpdateGUI.exe download --update 'GUID:REVISION' --accept-eula --plan
.\PSWindowsUpdateGUI.exe install --update 'GUID:REVISION' --accept-eula --yes
.\PSWindowsUpdateGUI.exe uninstall --package C:\Packages\update.msu --plan
.\PSWindowsUpdateGUI.exe offline-scan --download-cab C:\Data\wsusscn2.cab --output json
.\PSWindowsUpdateGUI.exe services add-microsoft-update --plan
.\PSWindowsUpdateGUI.exe policy set --value AUOptions=3 --value NoAutoRebootWithLoggedOnUsers=1 --plan
.\PSWindowsUpdateGUI.exe maintenance reset-components --plan
.\PSWindowsUpdateGUI.exe job create --action install --update 'GUID:REVISION' --at '2026-08-01 03:00' --accept-eula --plan
```

Exit codes are `0` success, `1` failure, `2` validation/preflight failure, `3` partial
success, `4` cancelled/stopped monitoring, and `3010` success with restart required.
JSON uses schema version 1 and includes operation ID, target, UTC timing, state,
warnings, structured errors, and typed data.

SMTP passwords are never accepted as arguments. `report configure` prompts in an
interactive console and stores the credential in Windows Credential Manager.
