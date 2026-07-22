# Security model

## Defenses

- Administrator elevation is explicit at process launch.
- Embedded PSWindowsUpdate package and every required file are SHA-256 pinned.
- Upstream signed files must report a valid Authenticode signature.
- A protected random extraction directory prevents lower-privileged replacement.
- Only 19 public cmdlets are executable through the normal host.
- Inputs are typed parameters, not generated script source.
- Execution policy, user profiles, and system module paths remain unchanged locally.
- Normal credentials stay in memory and all previews/logs redact likely secrets.
- Remote deletion requires a narrow canonical path and matching ownership marker.

## Deliberate high-risk features

The upstream `Invoke-WUJob -Script` parameter is intentionally arbitrary PowerShell.
The GUI labels it, displays the preview, and requires confirmation. `Set-PSWUSettings`
can intentionally persist SMTP credentials in Windows Credential Manager.

## Residual risks

- The GUI is elevated, so a compromise of the process has administrator impact.
- The initial public executable is unsigned; checksums and build provenance mitigate
  distribution tampering but do not replace trusted code signing.
- Windows Update and third-party update content are governed by the configured update
  service, not by this GUI.
- The upstream binary is closed-source in its released package; this project verifies
  its publisher signature and immutable bytes but cannot independently audit its source.
