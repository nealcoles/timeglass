# 0004: GitHub Actions as the pipeline

- **Status:** Accepted
- **Date:** 2026-11-02

## Context

The code is on GitHub. We need CI that builds and tests .NET on Linux and on Windows (the
Win32 adapters need a real Windows desktop), builds and tests the frontend, reports coverage,
and keeps dependencies and security scanning up to date. Later it should produce the release
build.

Options considered: GitHub Actions, Azure Pipelines, a self-hosted runner or another hosted CI.

## Decision

Use **GitHub Actions** (`.github/workflows/`), with **Dependabot** for dependency updates and
**CodeQL** for code scanning.

- Hosted `ubuntu-latest` and `windows-latest` runners come with the .NET and Node toolchains
  we need, and there is nothing to maintain. The Windows runner has an interactive desktop, so
  the integration tests can create and focus real windows.
- Checks, test reports, coverage comments and artifacts show up directly on the pull request.
- Dependabot and CodeQL are native to GitHub and need no extra service.

The current jobs, all in `ci.yml`, run on every pull request and on pushes to `main`:

| Job               | Runner  | Does                                                                   |
| ----------------- | ------- | ---------------------------------------------------------------------- |
| `dotnet-portable` | Linux   | Format check, build and test `Timeglass.Portable.slnx`, coverage gates |
| `dotnet-windows`  | Windows | Build and test `Timeglass.slnx`, including Windows integration tests   |
| `web`             | Linux   | Lint, format check, typecheck, tests with coverage, build              |

`codeql.yml` analyses C# and TypeScript; `dependabot.yml` groups weekly updates per ecosystem.

## Consequences

- The pipeline is tied to GitHub. Steps are plain `dotnet` and `npm` commands, so moving to
  another CI would mean rewriting YAML, not build logic.
- Windows runners are slower to start than Linux ones, which is why most tests are kept in the
  portable solution and run on Linux.
- Third-party actions are pinned to major versions and updated by Dependabot.
