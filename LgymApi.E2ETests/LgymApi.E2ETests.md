# LgymApi.E2ETests.csproj

## Purpose and Boundary

`LgymApi.E2ETests` is the standalone .NET 10 E2E harness foundation. It is a package-only NUnit project in `LgymApi.E2ETests.sln`, has zero `ProjectReference` items, and is not a member of `LgymApi.sln`. The main solution remains exactly 18 projects with 90 direct project-reference edges.

The foundation owns harness configuration validation, runner policy, boundary checks, and disposable PostgreSQL Testcontainers checks. Its base stack is Microsoft Playwright, Reqnroll with NUnit, and Testcontainers PostgreSQL. It does not start the API, launch a browser, define feature scenarios, use product types, query a database, or access a source checkout in this issue.

Future Given setup must cross the product boundary through public HTTP only. It must not introduce in-process hosting, product references, direct persistence access, or test-only API behavior.

## Prerequisites and Execution

- Use the .NET 10 SDK.
- Docker must be available for the complete Harness category. A missing Docker daemon is a prerequisite failure, not a skipped test.
- Run tests serially. NUnit workers are disabled by `LgymApi.E2ETests.runsettings`; the assembly is nonparallel, and Reqnroll maps the `serial` tag to a nonparallel marker.

From the repository root, run:

```powershell
dotnet restore LgymApi.E2ETests.sln
dotnet build LgymApi.E2ETests.sln --configuration Release --no-restore
dotnet test LgymApi.E2ETests/LgymApi.E2ETests.csproj --configuration Release --no-build --settings LgymApi.E2ETests/LgymApi.E2ETests.runsettings --filter "TestCategory=Harness"
```

The committed configuration is copied to the test output. Testcontainers uses the configured PostgreSQL image with dynamic container naming and host port allocation. The API port is dynamic (`0`), while the web port is fixed at `8083` for later work.

## Safe Configuration Contract

`appsettings.E2E.json` contains one `E2E` object with this schema:

| Section | Properties |
| --- | --- |
| `WebSource` | `RepositoryUrl`, `CommitSha`, optional `SourcePath` |
| `Api` | `PublishedDllPath`, `Port` |
| `Web` | `Port` |
| `Runtime` | `PrivateRunRoot` |
| `Database` | `Image`, `NamePrefix` |
| `Timeouts` | `ContainerStartupSeconds`, `ApiStartupSeconds`, `WebStartupSeconds`, `ProcessShutdownSeconds`, `HttpRequestSeconds`, `BrowserActionMilliseconds`, `ScenarioSeconds`, `TestSessionSeconds` |

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
| Timeouts | `120`, `120`, `120`, `15`, `30`, `15000`, `180`, `900` in schema order |

Configuration loads JSON first, then environment variables with the `LGYM_` prefix. Set `LGYM_E2E__WebSource__SourcePath` only when a later operational stage needs an external checkout. The committed configuration intentionally omits that machine-specific path. Source-checkout and published-DLL existence are deferred to those later stages.

Keep private runs, browser output, traces, screenshots, reports, caches, binaries, and synthetic runtime data under ignored private-artifact locations. Do not commit credentials, connection strings, source checkouts, browser storage, or runtime state.
