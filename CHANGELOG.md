# Changelog

This project follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

- Use the authoritative Windows build value so Windows 11 is detected under .NET Framework compatibility shims.
- Correct one-way WPF bindings for display-only status, progress, preview, and log fields.
- Normalize collection-wrapped PSWindowsUpdate results and map nested Windows Update identities into selectable GUI rows.
- Use silent reboot-status inspection in the hosted runspace and add a guarded local acceptance runner.
- Force the reproducible release build and test pipeline to compile and package the x64 solution platform.

### Added

- Portable elevated Windows 11 WPF application.
- Guided local and native remote update workflows.
- Metadata-driven coverage of all PSWindowsUpdate 2.2.1.5 public cmdlets.
- Signed vendor verification, redacted logging, tests, SBOM, CI, and release automation.

## [1.0.0] - Unreleased

- Initial public release after clean local and remote VM acceptance.
