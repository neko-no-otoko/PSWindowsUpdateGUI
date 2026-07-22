# Contributing

Thank you for helping improve PSWindowsUpdate GUI.

1. Open an issue for significant behavior or security-model changes before investing
   in a large implementation.
2. Create a focused branch from `main`.
3. Keep all WUA calls typed and on the dedicated STA COM worker. Do not add
   script interpolation, arbitrary command execution, or silent policy changes.
4. Run `build\Build.ps1` and include the relevant tests.
5. Never include real hostnames, credentials, update logs, or enterprise policy data.
6. Submit a pull request using the repository template.

Windows PowerShell is reserved for fixed, embedded WinRM transport scripts with
typed arguments. Update execution must stay inside the staged native executable.
