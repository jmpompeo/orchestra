# Agent context

## Product and boundaries

- Purpose: Orchestra is a self-contained .NET 10 CLI that installs portable AI-engineering workflow configuration for Codex, Claude Code, and Cursor.
- In scope: the CLI, embedded configuration assets, skills, project templates, tests, and GitHub workflows in this repository.
- Out of scope: users' global configuration except through an explicit CLI command; project-local files except through `init-project --apply`; external releases and GitHub settings.
- Work/personal boundary: this is a public repository. Do not add credentials, personal data, customer data, internal hostnames, tickets, logs, transcripts, or proprietary procedures.

## Architecture map

- Entry points: `src/AgentHarness/Program.cs` starts the CLI; `src/AgentHarness/Core.cs` implements commands, ownership-state handling, asset installation, and update behavior.
- CLI project: `src/AgentHarness/AgentHarness.csproj` targets .NET 10 and embeds `global/`, `skills/`, `project-template/`, and `models.conf` as assets. Internal `AgentHarness` names and the persistent `agent-harness` state identity are compatibility boundaries.
- Configuration assets: `global/` holds personal-tool defaults and agent templates; `skills/` holds portable skills; `project-template/` holds files created by `init-project`.
- Model assignments: `models.conf` is the single source of truth for model assignments rendered into installed agent definitions.
- Tests and delivery: `tests/AgentHarness.Tests/` is the executable test suite; `.github/workflows/ci.yml` builds and runs it; `.github/workflows/release.yml` uses Release Please and publishes self-contained platform archives.
- Maintainer behaviour evals: `evals/` contains opt-in Codex debugging and refactoring fixtures and a local runner. It is separate from `orchestrate` and CI; raw traces stay in ignored local artifacts.
- Distribution: public stable GitHub Releases include `install.sh` and `install.ps1`; installation and self-update use anonymous HTTPS downloads and verify platform archives against `SHA256SUMS`. GitHub CLI credentials are not a user prerequisite.

## Commands

- Build: `dotnet build AgentHarness.sln --configuration Release`
- Test: `dotnet run --project tests/AgentHarness.Tests/AgentHarness.Tests.csproj --configuration Release`
- Offline eval fixture check: `dotnet run --project evals/Orchestra.Evals.csproj -- self-test` (no model call; requires Docker or Podman with a preloaded .NET 10 SDK image).
- Run the CLI from source: `dotnet run --project src/AgentHarness/AgentHarness.csproj -- --help`
- Package: the release workflow runs `dotnet publish src/AgentHarness/AgentHarness.csproj --configuration Release --runtime <rid> --self-contained true -p:PublishSingleFile=true`.

## Sensors and validation tiers

### Fast: run before handoff

- Format: unknown.
- Type-check/static analysis: unknown.
- Fast targeted test: `dotnet run --project tests/AgentHarness.Tests/AgentHarness.Tests.csproj --configuration Release`.
- Security or dependency check: unknown.

### Full: run before integration when risk warrants it

- Full test suite: `dotnet run --project tests/AgentHarness.Tests/AgentHarness.Tests.csproj --configuration Release`.
- Build/package: `dotnet build AgentHarness.sln --configuration Release`; release packaging is performed by `.github/workflows/release.yml`.
- Integration, end-to-end, or contract test: unknown.

### Architecture and behaviour

- Preserve ownership safety: global installation updates only CLI-owned, unchanged regular files; conflicting, modified, linked, or unsafe paths are preserved. Project initialization creates missing selected-tool files only.
- Preserve compatibility boundaries: do not rename internal `AgentHarness` namespaces/projects, `$bootstrap-agent-harness`, `AGENT_HARNESS_*` variables, or the established `agent-harness` state identity unless an approved migration changes them.
- Approved fixtures: `project-template/docs/approved-fixtures/README.md` defines the fixture convention.
- Manual acceptance: use `--dry-run` before installation changes; verify an update archive against `SHA256SUMS`; use `--apply` only after reviewing `init-project` output.
- Bootstrap installers replace only a safe, unambiguous, user-owned executable, never edit PATH or profiles, never install prerequisites or configuration, and must provide corrective instructions when refusing an operation.

## Engineering constraints

- Runtime/toolchain: .NET 10; project files and GitHub workflows are the source of truth.
- Required conventions: use Conventional Commits for release-relevant changes; `models.conf` is the only file maintainers edit for model assignments, then rerun the installer.
- Dependency policy: unknown.
- Compatibility/migration: external product, command, release artifacts, and repository slug use Orchestra and `orchestrate`; internal AgentHarness identifiers and persistent state remain unchanged unless an explicit migration is approved.
- Generated files: installed agent definitions are rendered from templates and `models.conf`; Cursor commands are generated from canonical skill `SKILL.md` files. Do not hand-edit generated output.

## Risk tiers and definition of done

- Localized, low-risk changes need a targeted deterministic check.
- Module-level changes need a dependency-aware plan and deterministic checks.
- Cross-module, data, authorization, migration, external-API, security, billing, privacy, destructive, or production-impacting changes need proportionate review; changes affecting external systems require explicit approval.
- Done means the requested behavior is observable, relevant deterministic checks pass, documentation and migration implications are updated, and unrelated user changes are preserved.

## Evidence handoff and delivery

- Report deterministic checks with pass/fail evidence, behavioural evidence, independent-review findings when required, and checks not run with reasons.
- Do not commit, push, deploy, publish, merge, alter external systems, or perform destructive actions without explicit authorization.
- Secrets/PII handling: never place them in this public repository; inspect staged content before committing because `.gitignore` is not a secret scanner.
- Branch, format, lint, security, and release-approval policies: unknown.
