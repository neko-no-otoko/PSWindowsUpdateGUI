# Security policy

## Supported versions

Security fixes are applied to the latest release and `main`.

## Reporting a vulnerability

Use GitHub's private **Report a vulnerability** security-advisory flow. Do not include
credentials, production hostnames, private update history, or exploit details in a
public issue.

Include the affected version, Windows build, reproduction steps, security impact, and
whether the issue applies to WUA execution, remote staging, the CLI, or an administrative service.

## Release trust

Release executables are unsigned unless the release notes explicitly state otherwise.
Verify the SHA-256 asset, SPDX SBOM, and GitHub build provenance before use.
