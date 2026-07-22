# Contributing

Thank you for helping improve PSWindowsUpdate GUI.

1. Open an issue for significant behavior or security-model changes before investing
   in a large implementation.
2. Create a focused branch from `main`.
3. Do not update the vendored PSWindowsUpdate package without updating every hash,
   third-party notice, catalog test, and compatibility note.
4. Run `build\Build.ps1` and include the relevant tests.
5. Never include real hostnames, credentials, update logs, or enterprise policy data.
6. Submit a pull request using the repository template.

Changes to command invocation must continue to use typed `AddCommand`/`AddParameter`
calls. Dynamic script text is allowed only for the explicitly documented
`Invoke-WUJob -Script` feature and fixed infrastructure scripts reviewed as code.
