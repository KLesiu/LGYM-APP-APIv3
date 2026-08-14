# LgymApi.E2ETests.csproj

## Purpose and Boundary

`LgymApi.E2ETests` is the standalone .NET 10 E2E harness for external published API processes, public HTTP, and pinned Expo Web browser lifecycle proofs. It is a package-only NUnit project in `LgymApi.E2ETests.sln`, has zero direct or evaluated `ProjectReference` items, and is not a member of `LgymApi.sln`. The main solution remains exactly 18 projects with 90 direct project-reference edges.

The harness owns configuration validation, runner policy, standalone-boundary checks, fresh PostgreSQL Testcontainers leases, publication identity, external process lifetime, public HTTP host proofs, and the pinned Expo Web lifecycle. It launches the published API only as an absolute `dotnet.exe` command with the canonical absolute DLL as its one application argument and the publication directory as its working directory. Its base stack is Microsoft Playwright, Reqnroll with NUnit, and Testcontainers PostgreSQL. It does not use product types, query a database directly, or run product business scenarios.

Every proof crosses the product boundary through an external process and public HTTP only. It must not introduce in-process hosting, product references, direct persistence access, test-only API behavior, raw SQL, or an unapproved business browser scenario.

## Prerequisites and Execution

- Use Windows and the .NET 10 SDK.
- Docker must be available for the complete Harness category. A missing Docker daemon is a prerequisite failure, not a skipped test.
- Web proof needs a validated read-only external mobile checkout at the configured pin, Node `>= 22.18`, and the private Playwright Chromium installed by the command below. Missing source, Node, Chromium, or port prerequisites fail with sanitized diagnostics and never skip.
- Run tests serially. NUnit workers are disabled by `LgymApi.E2ETests.runsettings`; the assembly is nonparallel, and Reqnroll maps the `serial` tag to a nonparallel marker.

From the repository root, run:

```powershell
dotnet restore LgymApi.E2ETests.sln
dotnet build LgymApi.E2ETests.sln --configuration Release --no-restore
dotnet publish LgymApi.Api/LgymApi.Api.csproj --configuration Release --output .e2e-private/published-api
pwsh -NoProfile -File LgymApi.E2ETests/scripts/install-playwright-chromium.ps1 -Configuration Release
dotnet test LgymApi.E2ETests/LgymApi.E2ETests.csproj --configuration Release --no-build --settings LgymApi.E2ETests/LgymApi.E2ETests.runsettings --filter "TestCategory=HarnessDocker" --logger "trx;LogFileName=issue-435-harness-docker-direct.trx"
dotnet test LgymApi.E2ETests/LgymApi.E2ETests.csproj --configuration Release --no-build --settings LgymApi.E2ETests/LgymApi.E2ETests.runsettings --filter "TestCategory=Lifecycle" --logger "trx;LogFileName=issue-435-lifecycle-direct.trx"
pwsh -NoProfile -File LgymApi.E2ETests/scripts/invoke-e2e-coordinator.ps1 -Mode HarnessOnly
```

The committed configuration is copied to the test output. Testcontainers uses the configured PostgreSQL image with dynamic container naming and host port allocation. The API port is dynamic (`0`), while the web port is fixed at `8083` for the exact E2E CORS singleton `http://localhost:8083`.

## Safe Configuration Contract

`appsettings.E2E.json` contains one `E2E` object with this schema:

| Section | Properties |
| --- | --- |
| `WebSource` | `RepositoryUrl`, `CommitSha`, optional `SourcePath` |
| `Api` | `PublishedDllPath`, `Port` |
| `Web` | `Port` |
| `Runtime` | `PrivateRunRoot` |
| `Database` | `Image`, `NamePrefix` |
| `Timeouts` | `ContainerStartupSeconds`, `ApiPublishSeconds`, `ApiStartupSeconds`, `WebStartupSeconds`, `ProcessShutdownSeconds`, `HttpRequestSeconds`, `BrowserActionMilliseconds`, `ScenarioSeconds`, `TestSessionSeconds` |

Committed defaults are:

| Setting | Value |
| --- | --- |
| `WebSource.RepositoryUrl` | `https://github.com/KLesiu/LGYM-APP-MOBILE.git` |
| `WebSource.CommitSha` | `8f59d96ec368f509b1565e3296cd89d2a082a952` |
| `WebSource.SourcePath` | omitted |
| `Api.PublishedDllPath` | `.e2e-private/published-api/LgymApi.Api.dll` |
| `Api.Port` | `0` |
| `Web.Port` | `8083` |
| `Runtime.PrivateRunRoot` | `.e2e-private/runs` |
| `Database.Image` | `postgres:17.10-alpine3.24` |
| `Database.NamePrefix` | `lgym_e2e` |
| Timeouts | `120`, `300`, `120`, `120`, `90`, `30`, `15000`, `180`, `900` in schema order |

Configuration loads JSON first, then environment variables with the `LGYM_` prefix. Set `LGYM_E2E__WebSource__SourcePath` only when the lifecycle run needs the external checkout. The committed configuration intentionally omits that machine-specific path. The immutable mobile source pin is `8f59d96ec368f509b1565e3296cd89d2a082a952`.

Keep private runs, browser output, traces, screenshots, reports, caches, binaries, and synthetic runtime data under ignored private-artifact locations. Do not commit credentials, connection strings, source checkouts, browser storage, or runtime state.

## Per-Scenario Lifecycle Contract

One serial Lifecycle test process owns one private run root, one staged export of the configured immutable source pin, and one `npm ci` installation. Each `@Lifecycle` scenario gets a fresh scenario stack in this order: randomized PostgreSQL Testcontainers lease, external published API process and runtime configuration, public `GET /health/live`, public `POST /api/login` invalid-login gate returning exactly `401 Unauthorized`, Expo Web process, Playwright Chromium process, browser context, and page. The linked `Timeouts.ScenarioSeconds` token bounds every scenario acquisition and probe.

The Expo child receives the exact scenario API URI as `REACT_APP_BACKEND` and `BROWSER=none`, preventing Expo from opening a system browser. Port `8083` is a fixed CORS singleton. A foreign listener is never inspected or terminated, and lifecycle runs must not overlap. Public-HTTP helpers cross the API boundary only through HTTP. They do not use product assemblies, direct persistence, raw SQL, test-only routes, or business-flow shortcuts.

Cleanup is acquired-only and reverse ordered: page/context, Chromium/Playwright, Expo process, API process/runtime configuration/PostgreSQL, then scenario runtime children. Cleanup uses independent `Timeouts.ProcessShutdownSeconds` bounds. Git reads run in a private child environment with private HOME/USERPROFILE/TEMP/TMP, a controlled Git/System32 PATH, and no inherited credentials or `LGYM_` values. Receipts retain only source-pin, category, counter, and boolean facts. They never retain source paths, process IDs, cookies, tokens, browser storage, raw output, or private runtime state.

`HarnessDocker` and `Lifecycle` are nonempty, disjoint categories. `HarnessDocker` covers Docker and PostgreSQL ownership proofs. `Lifecycle` covers orchestration, Reqnroll lifecycle probes, failure containment, artifacts, evidence, and runner contracts. `HarnessOnly` is the only coordinator mode. It runs the standalone E2E project with `HarnessDocker` followed by `Lifecycle`, validates fresh nonzero TRXs and sanitized receipts, and writes the issue manifest.

Failure-only artifacts are bounded sanitized receipts at `.e2e-private/runs/<run-id>/artifacts/<safe-scenario-id>/`. Successful scenarios leave no artifact directory, successful runs remove the complete run root, and failed runs retain only these artifacts. Never commit private artifacts, browser storage, traces, screenshots, runtime configuration, output, credentials, connection strings, or paths.

Issue `#436` owns product business scenarios, including onboarding and authentication flows, and remains deferred. Issue `#437` owns the `Full` coordinator mode and `ArtifactDrill`, including its diagnostics and final acceptance, and remains deferred.

## External Host Contract

- Windows and a reachable Docker daemon are required. Missing prerequisites fail with sanitized diagnostics rather than skipped tests. The host uses a fresh PostgreSQL lease, then starts the external process, waits only for `GET /health/live`, and proves database-backed readiness through public invalid login returning `401` rather than `500`.
- `E2E` applies migrations before startup guards and keeps rate limiting enabled. `Testing` skips migration, guards, and rate limiting. Both select test-safe no-op workers and suppress `/hangfire` before authentication. Production and unknown environments reject a fresh pending schema before readiness.
- The generated runtime configuration, connection details, JWT material, and process/container identities are private and transient. Child configuration is passed by `LGYM_APP_CONFIG_PATH`; its closed environment contains only `SystemRoot`, `WINDIR`, `TEMP`, `TMP`, `ASPNETCORE_ENVIRONMENT`, `DOTNET_ENVIRONMENT`, `ASPNETCORE_URLS`, `LGYM_APP_CONFIG_PATH`, `DOTNET_NOLOGO`, and `DOTNET_CLI_TELEMETRY_OPTOUT`.
- Publication, startup, HTTP, output drain, process-tree absence, and cleanup use configured bounds. Captured stdout and stderr retain at most 64 KiB each after redaction. Receipts retain safe categories, statuses, API HEAD SHA, and dirty marker only, never secrets, connection strings, private paths, PIDs, container IDs, or private configuration contents.
- Disposal is reverse ordered: `api-process, runtime-configuration, postgresql`. Success and failure paths require the API process tree, private runtime directory, configuration file, and PostgreSQL lease to be absent. The focused TRX must contain every ApiHostProof result as passed and none skipped.
