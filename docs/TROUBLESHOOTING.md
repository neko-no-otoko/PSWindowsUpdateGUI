# Troubleshooting

## CLI opens separately or output is missing

Start PowerShell or Command Prompt as administrator first. The executable requires UAC;
starting it from a medium-integrity console can detach the elevated process.

## `Microsoft Update` scan fails

Register the service explicitly with `services add-microsoft-update`, then scan again.
Use `--plan` to preview registration.

## Update identity is no longer applicable

Scan again. The app deliberately refuses stale GUID/revision selections.

## WUA reports another installer is busy

Let Windows Settings, Automatic Updates, or another management tool finish. The app
does not disable the Windows Update orchestrator.

## Remote preflight fails

Confirm DNS, Kerberos or trusted HTTPS certificates, administrator access, WinRM, SMB
administrative shares, target free space, and Windows 11 x64. The app will not weaken
these settings automatically.

## Offline scan finds updates but cannot install

Expected: `wsusscn2.cab` contains security metadata only. Perform an online WUA scan to
download/install, or acquire packages through an approved deployment channel.

## Cancellation did not roll back installation

WUA cancellation is a request. Check `status` and `history`, then restart if required.
