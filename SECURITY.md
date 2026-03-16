# Security Policy

## Reporting a Vulnerability

If you discover a security vulnerability in BulkSharp, please report it responsibly.

**Do not open a public GitHub issue for security vulnerabilities.**

Instead, please email security concerns to the maintainers via the repository's private contact channels, or use [GitHub's private vulnerability reporting](https://github.com/kalfonh/BulkSharp/security/advisories/new).

We will acknowledge receipt within 48 hours and provide a timeline for a fix.

## Supported Versions

| Version | Supported |
|---------|-----------|
| 0.1.x   | Yes       |

## Scope

Security issues in the following areas are in scope:

- Input validation bypass (CSV/JSON parsing, metadata deserialization)
- Path traversal in file storage providers
- Denial of service via unbounded resource consumption
- Information disclosure through error messages or logging
- SQL injection in Entity Framework queries
- Authentication/authorization bypass in Dashboard API endpoints

## Out of Scope

- Sample projects and demo applications
- Issues requiring physical access to the host machine
- Social engineering
