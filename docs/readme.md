# CI/CD

This project uses two GitHub Actions workflows, both under `.github/workflows/`.

## `ci.yml` — Continuous Integration

**Triggers:** every pull request targeting `main`, and every push to `main`.

| Job | What it does |
|---|---|
| `build` | `dotnet restore` + `dotnet build` on `src/PhxDbExplorer.slnx` (Release configuration). |
| `unit-tests` | Runs `dotnet test src/PhxDbExplorer.Tests`. No external dependencies. |
| `integration-tests` | Runs `dotnet test src/PhxDbExplorer.IntegrationTests`. Uses [Testcontainers](https://dotnet.testcontainers.org/) to spin up a SQL Server 2022 container on the runner — GitHub's `ubuntu-latest` runners have Docker preinstalled, so no extra setup is needed. |

`unit-tests` and `integration-tests` both depend on `build` and run in parallel with each other.

This workflow only reports status checks on commits/PRs — it does not currently gate merges. To make it a required check, enable "Require status checks to pass" for `main` under the repo's branch protection settings (GitHub UI, not part of this repo's config).

## `release.yml` — Release

**Trigger:** pushing a tag matching `v*.*.*` (e.g. `v1.2.0`).

| Job | What it does |
|---|---|
| `test` | Runs unit tests. If they fail, the release is aborted — no binaries are built or published. |
| `build` (matrix: `win-x64`, `linux-x64`, `osx-x64`, `osx-arm64`) | Runs `dotnet publish` for each RID as a self-contained, single-file executable (`PublishSingleFile=true`, `IncludeNativeLibrariesForSelfExtract=true`), with the assembly version set from the tag. All four run in parallel on `ubuntu-latest` — .NET cross-publishing doesn't require building on the target OS. Each output is archived (`.zip` for Windows, `.tar.gz` for Linux/macOS) and uploaded as a workflow artifact. |
| `publish-release` | Downloads all four archives and creates a GitHub Release at the pushed tag via [`softprops/action-gh-release`](https://github.com/softprops/action-gh-release), with the archives attached and release notes auto-generated from the commit log since the previous tag. |

### Artifact naming

Each release has exactly four assets, named `PhxDbExplorer-<version>-<rid>.<ext>`:

| Asset | Platform |
|---|---|
| `PhxDbExplorer-<version>-win-x64.zip` | Windows x64 |
| `PhxDbExplorer-<version>-linux-x64.tar.gz` | Linux x64 |
| `PhxDbExplorer-<version>-osx-x64.tar.gz` | macOS (Intel) |
| `PhxDbExplorer-<version>-osx-arm64.tar.gz` | macOS (Apple Silicon) |

Each archive contains a single self-contained executable (`PhxDbExplorer` or `PhxDbExplorer.exe`) — no .NET runtime installation is required to run it. Because the build is not trimmed, each archive is roughly 34–36 MB.

### Cutting a release

```bash
git tag v1.2.0
git push origin v1.2.0
```

The workflow runs automatically; no manual steps are needed on GitHub afterward.

### Verification history

The pipeline was verified end-to-end with a throwaway tag (`v0.0.1-test`) on 2026-08-03: unit tests passed, all four platform binaries built and uploaded correctly (with version correctly derived from the tag), and the GitHub Release was created with all four assets attached. The test tag and release were deleted afterward to keep the Releases page clean — this was a one-off validation run, not a real release.
