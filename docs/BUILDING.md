# Building

Build on Windows 11 x64 with Windows PowerShell 5.1 and the SDK pinned by `global.json`.
The repository-local SDK can be selected through `DOTNET_EXE`.

```powershell
$env:DOTNET_EXE = (Resolve-Path .\.tools\dotnet\dotnet.exe)
.\build\Verify-Native.ps1
.\build\Build.ps1 -Configuration Release -Version 2.0.0-beta.1
```

`Build.ps1` restores locked NuGet dependencies, builds x64 with warnings as errors,
runs unit tests and a non-elevated WPF startup smoke harness, copies the single EXE,
generates SHA-256 and SPDX 2.3 output, and copies notices. WUA interop generation reads the installed Microsoft type library; it
does not download code and its generated DLL is not a release asset.

CI additionally verifies formatting. Release tags use the same build and attach
GitHub artifact provenance. Optional Authenticode signing remains in
`build/Sign-Release.ps1`.
