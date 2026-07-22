# Building

## Prerequisites

- Windows 11 x64.
- .NET SDK 10.0.300, which can be installed privately with `dotnet-install.ps1`.
- Windows PowerShell 5.1.
- Internet access for the initial locked NuGet restore.

No permanent Visual Studio or module installation is required.

## Commands

```powershell
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File .\build\Verify-Vendor.ps1
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File .\build\Build.ps1 -Configuration Release -Version 1.0.0
```

`artifacts/release` contains the EXE, checksum, SPDX SBOM, and third-party notice.

## Signing

Signing is optional and requires a code-signing certificate already installed in the
current user or local-machine certificate store:

```powershell
.\build\Sign-Release.ps1 -CertificateThumbprint '<thumbprint>'
```

Never commit a PFX or password. Recalculate the checksum after signing.

## Reproducibility controls

- Exact SDK in `global.json`.
- NuGet lock files committed for application and tests.
- Deterministic compilation and warnings-as-errors.
- Vendored package and file hashes.
- Immutable GitHub Action commit references.
