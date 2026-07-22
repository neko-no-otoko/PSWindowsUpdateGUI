# Changelog

This project follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

- Replace the PSWindowsUpdate wrapper with an independent typed Windows Update Agent engine.
- Add same-executable GUI and CLI modes with versioned JSON results and stable exit codes.
- Add GUID-and-revision update selection, async WUA progress/cancellation, offline scans, services, policy, jobs, reporting, maintenance, and payload export.
- Stage the same verified executable for remote WinRM operations instead of installing a module.
- Remove the embedded PSWindowsUpdate package and all runtime module extraction/import code.
- Preserve the former 19-command feature surface as an explicit operation-migration matrix.

### Added

- Portable elevated Windows 11 WPF application.
- Guided local and native remote update workflows.
- Metadata-driven coverage of all PSWindowsUpdate 2.2.1.5 public cmdlets.
- Signed vendor verification, redacted logging, tests, SBOM, CI, and release automation.

## [1.0.0] - Unreleased

- Initial public release after clean local and remote VM acceptance.
