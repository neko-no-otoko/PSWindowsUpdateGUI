# Changelog

This project follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

- Align repository documentation, public metadata, and catalog tests with the current
  native WUA architecture.

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
