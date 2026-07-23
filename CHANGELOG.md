# Changelog

This project follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [3.0.0-beta.1] - 2026-07-22

### Changed

- Migrate the complete desktop shell from WPF/.NET Framework 4.8 to WinUI 3 on .NET
  10 and Windows App SDK 2.3.1.
- Publish an unpackaged, framework-self-contained, single-file Windows 11 x64 EXE;
  no permanent runtime, package, or update module installation is required.
- Replace the WPF dispatcher dependency in the WUA worker with an owning native STA
  message pump and move fixed WinRM transport to built-in Windows PowerShell 5.1.
- Redesign History & Status with compact status cards, an inline typed history table,
  selected-entry details, and exact-revision removal or driver rollback verification.
- Add native System, Light, and Dark WinUI themes, Mica, themed title bar, modern
  controls, semantic resources, and Windows high-contrast fallback.

### Added

- Add a production-assembly WinUI smoke mode with a fake WUA adapter, test-only
  non-elevated manifest, dark-theme screenshot, and no machine mutations.
- Pin .NET 10, Windows App SDK 2.3.1, and Windows SDK BuildTools 10.0.28000.2270.

### Removed

- Remove WPF, .NET Framework, GAC PowerShell automation assembly, and the obsolete
  cross-assembly UI smoke host from the product and build.


## [2.0.0-beta.1] - 2026-07-21

### Added

- Portable, elevated Windows 11 x64 WPF application and same-executable CLI.
- Typed Windows Update Agent engine with asynchronous search, download, install, and
  uninstall operations selected by update GUID and revision.
- Software and driver workflows, update history and status, service management,
  Microsoft-signed offline catalogs, payload export, policy backup and restore,
  component maintenance, scheduled jobs, SMTP reporting, and secure remote execution.
- Versioned JSON results, stable exit codes, mutation plans, explicit confirmation
  gates, redacted logs, locked dependencies, tests, SPDX SBOM, CI, and release provenance.
