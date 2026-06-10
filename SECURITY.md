# Security Policy

## Reporting a Vulnerability

SlopArena is a community-driven open-source game. We take security
concerns seriously.

If you discover a security vulnerability, please **do not open a public
issue**. Instead, report it privately via one of the following methods:

- **GitHub Issues** — Open a [regular issue](https://github.com/Binoui/SlopArena/issues/new)
  and mention `[SECURITY]` in the title, with details of the vulnerability.
  We will mark the issue as confidential if possible.
- **Email** — Reach out to the maintainers directly via GitHub.

We will acknowledge receipt within 48 hours and provide an estimated
timeline for a fix.

## Scope

This policy covers:
- The game client (Godot 4 .NET C# project)
- The headless server (`Server/`)
- The `Shared/` C# library
- CI/CD workflows and infrastructure

## Out of Scope

- Issues in Godot Engine itself (report upstream)
- Vulnerabilities in .NET runtime (report to Microsoft)
- General feature requests or bugs (open a regular issue)

## Preferred Format

When reporting, please include:
1. Description of the vulnerability
2. Steps to reproduce
3. Potential impact
4. Suggested fix (if any)

We appreciate your help keeping SlopArena safe for everyone!
