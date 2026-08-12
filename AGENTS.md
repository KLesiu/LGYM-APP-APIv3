# LGYM API - Agent Notes

## Scope

This file applies repo-wide. Keep `AGENT.md` and `AGENTS.md` aligned. More specific `AGENTS.md` files under subtrees override this file for their subtree.

Main areas:

- API: `LgymApi.Api`
- Application: `LgymApi.Application`
- Domain: `LgymApi.Domain`
- Infrastructure: `LgymApi.Infrastructure`
- Resources: `LgymApi.Resources` and `LgymApi.Resources.Generator`
- Module shells: `LgymApi.Platform`, `LgymApi.Identity`, `LgymApi.TrainingPlanning`, and `LgymApi.Notifications`
- Background jobs: `LgymApi.BackgroundWorker` and `LgymApi.BackgroundWorker.Common`
- Data seeding: `LgymApi.DataSeeder`
- Tests: `LgymApi.UnitTests`, `LgymApi.IntegrationTests`, `LgymApi.E2ETests`, `LgymApi.ArchitectureTests`, `LgymApi.DataSeeder.Tests`, `LgymApi.TestUtils`

## Instruction Hierarchy

- Root guidance lives here.
- Project docs live next to each `.csproj` as `<ProjectName>.md`.
- Architecture overview lives in `docs/ARCHITECTURE.md`.
- Other docs in `docs/` are supporting references for focused areas.

## Documentation Maintenance

- Keep durable facts in agent-facing docs current when they change.
- Treat `AGENTS.md` and project docs as anchor documents, not exhaustive manuals.
- Add concise, factual notes only: entry points, invariants, recurring pitfalls, validation tips, and compatibility risks.
- Avoid task history and speculative design notes.
- When a doc links to a focused child document for the area you are changing, read that child document before editing.

## Architecture Documentation

Before changing code in a `.csproj` folder, read that assembly's `<ProjectName>.md` if it exists. Before changing architecture, boundaries, DI, mapping, persistence, feature layout, or cross-project flows, also read `docs/ARCHITECTURE.md`.

Before adding or changing a use case or module boundary, read the [module contribution guide](docs/MODULE_CONTRIBUTION_GUIDE.md), `docs/ARCHITECTURE.md`, and the owner project's adjacent doc. `docs/NEW_MODULES_USAGE.md` is feature-usage documentation, not contribution authority.

For any changed `.csproj` folder, update the matching project doc when responsibilities, dependencies, public APIs, important flows, persistence behavior, messaging, configuration, or cross-project interactions change.

## Mandatory `.csproj` Purpose Rule

Every `.csproj` in the solution must be documented in the project map below.

When adding, renaming, deleting, or materially changing a `.csproj`:

1. inspect all project files, for example with `git ls-files '*.csproj'`;
2. update `LgymApi.sln` if solution membership changes;
3. update the project map in this file with why each project exists;
4. create or update the matching project doc next to that `.csproj` as `<ProjectName>.md`;
5. update project references, test commands, workflows, and `Directory.Packages.props` when needed;
6. keep package versions centralized instead of adding inline versions to `.csproj` files.

Final responses for such tasks should mention which `.csproj` files changed and whether this map was updated.

## Project Purpose Map

| Project | Why it exists | Rules for agents |
| --- | --- | --- |
| `LgymApi.Api/LgymApi.Api.csproj` | ASP.NET Core HTTP entrypoint: controllers, DTO contracts, validators, middleware, mapping profiles, auth, JSON setup, Swagger, CORS, rate limits, SignalR, and composition root. | Keep controllers thin and preserve legacy payload shapes. |
| `LgymApi.Application/LgymApi.Application.csproj` | Use-case and business orchestration for the remaining Reporting, Workout & Progress, Coaching, and Nutrition capabilities, plus their application-facing contracts and DI facade. | Own business rules, authorization checks, transactions, and unit-of-work commits here. Never reference a `LgymApi.BackgroundWorker*` project or namespace. |
| `LgymApi.Domain/LgymApi.Domain.csproj` | Core domain: entities, enums, strongly typed IDs, domain helpers, and auth/security constants. | Keep free of HTTP, EF, API, and localization concerns. Domain is localization-neutral and must not reference `LgymApi.Resources`. Do not reorder or renumber existing enums. |
| `LgymApi.Infrastructure/LgymApi.Infrastructure.csproj` | Shared technical composition: the EF Core `DbContext`, migrations, unit of work, Hangfire persistence, module persistence bridges, and remaining non-extracted persistence adapters. | Repositories must not call `SaveChangesAsync` or own transactions. Do not register extracted module services, providers, or select push schedulers here. |
| `LgymApi.Resources/LgymApi.Resources.csproj` | Localized `.resx` resources for messages, enums, and emails, with generated strongly typed access. | Add or update English and Polish resources for user-facing text. |
| `LgymApi.Resources.Generator/LgymApi.Resources.Generator.csproj` | Roslyn source generator/analyzer used by `LgymApi.Resources`; targets `netstandard2.0` for analyzer compatibility. | Keep deterministic and free of runtime app dependencies. |
| `LgymApi.Platform/LgymApi.Platform.csproj` | Stable code boundary for shared platform services and contracts; contains the `ActorReference` contract-only typed-ID marker. | Keep direct references limited to Domain and Resources; do not add feature workflows, a DbContext, migrations, or Worker dependencies. |
| `LgymApi.Identity/LgymApi.Identity.csproj` | Stable code boundary for identity and account capabilities; contains account, session, and role contract-only typed-ID markers. | Keep direct references limited to Domain, Platform, and Resources; do not reference Notifications, Infrastructure, or Worker. |
| `LgymApi.TrainingPlanning/LgymApi.TrainingPlanning.csproj` | Stable code boundary for training-plan definition and planning workflows; contains plan, plan-day, and consumer-owned plan-exercise contract-only typed-ID markers. | Keep direct references limited to Domain, Identity, Platform, and Resources; do not add a DbContext or take ownership of completed workout execution. |
| `LgymApi.Notifications/LgymApi.Notifications.csproj` | Stable code boundary for provider-neutral notification capabilities; contains notification and push-installation contract-only typed-ID markers. | Keep direct references limited to BackgroundWorker.Common, Domain, Identity, Platform, and Resources; do not add a DbContext, migrations, or provider credentials. |
| `LgymApi.BackgroundWorker.Common/LgymApi.BackgroundWorker.Common.csproj` | Exact bounded Worker/Infrastructure seam for persisted job interfaces, scheduler bridges, idempotency policy, and email wire contracts. | Do not add Application-facing contracts, commands, serialization, or push delivery types. |
| `LgymApi.BackgroundWorker/LgymApi.BackgroundWorker.csproj` | Hangfire/background worker implementations for module ports and the bounded Common job seam. | Keep jobs idempotent where practical, register worker services here, and select no-op or Hangfire scheduling by testing mode. |
| `LgymApi.DataSeeder/LgymApi.DataSeeder.csproj` | Console executable for deterministic data seeding/bootstrap using infrastructure and EF tooling. | Do not make API startup depend on this executable. |
| `LgymApi.UnitTests/LgymApi.UnitTests.csproj` | Focused unit tests for service, domain, application, mapping, API, and infrastructure units. | Use NUnit, FluentAssertions, NSubstitute, and shared helpers from `LgymApi.TestUtils`. |
| `LgymApi.IntegrationTests/LgymApi.IntegrationTests.csproj` | End-to-end API tests with `WebApplicationFactory`, middleware, auth, serialization, localization, and test persistence. | Reuse integration helpers and validate legacy contract compatibility for changed endpoints. |
| `LgymApi.E2ETests/LgymApi.E2ETests.csproj` | Standalone package-only E2E harness for configuration, runner policy, boundary, disposable PostgreSQL, and external published-API/process/public-HTTP proofs. It has zero direct/evaluated project references and is not one of the main solution's 18 projects or 90 direct edges. | Keep it outside `LgymApi.sln`; use only package dependencies and public HTTP. Do not add product references, in-process API startup, direct persistence access, browser scenarios, or private artifacts. |
| `LgymApi.ArchitectureTests/LgymApi.ArchitectureTests.csproj` | Roslyn guard tests for dependency direction, ID boundaries, DI placement, feature layout, mapping, enums, and unit-of-work rules. | Treat failures as architecture violations unless an intentional exception is documented. |
| `LgymApi.DataSeeder.Tests/LgymApi.DataSeeder.Tests.csproj` | Tests for DataSeeder behavior and seeding assumptions. | Update when seeder inputs, defaults, or seeded entities change. |
| `LgymApi.TestUtils/LgymApi.TestUtils.csproj` | Shared test builders, fakes, fixtures, and setup helpers; referenced by test projects but not a test project itself. | Centralize reusable fakes and builders here and avoid hidden side effects. |

## Environment

- Windows-only repo.
- Use Visual Studio and MSBuild 18.x where the task needs IDE/MSBuild behavior.
- Repo uses the .NET 10 SDK.
- Initialize submodules before building with `git submodule update --init --recursive` when the task depends on them.

## Build Rules

- Prefer repository-owned entry points and existing solution builds.
- Restore with `dotnet restore LgymApi.sln`.
- Sanity-check with `dotnet build LgymApi.sln --configuration Release --no-restore`.
- Run the API with `dotnet run --project LgymApi.Api`.
- Restore and build the standalone E2E harness with `dotnet restore LgymApi.E2ETests.sln` and `dotnet build LgymApi.E2ETests.sln --configuration Release --no-restore`.
- Do not retarget frameworks or change solution wiring unless the task requires it.
- Restore normally runs in locked mode; do not update lock state unless the task requires it.

## Test Rules

- Prefer the smallest relevant validation slice first.
- Use targeted `dotnet test` on affected projects after the relevant build.
- If you touch E2E code, say clearly whether you only built it or ran it on a prepared workstation.
- If a build or test cannot be run, say so explicitly.

## Repo-Specific Rules

- Preserve legacy payload shapes such as `_id`, `msg`, and `req`.
- Keep the request flow: `Controller -> FluentValidation -> Application Service -> Repository -> Unit of Work -> Mapper -> Middleware`.
- Keep controllers thin; business rules belong in Application services.
- Keep repositories stage-only; services own `IUnitOfWork.SaveChangesAsync()` and transactions.
- For runtime application persistence, use provider-neutral EF Core APIs. Do not use raw SQL or branch on `Database.ProviderName` or provider-name string literals.
- Map between distinct layer models or results, including entities, application/read models, and DTOs, through registered custom `IMapper` mapping profiles (`IMapper`, `IMappingProfile`, `MappingContext`). AutoMapper, ad-hoc mapper implementations, and manual cross-layer mapping in controllers, services, or adapters are prohibited. Trivial scalar assignments without model transformation are allowed.
- Use resource-backed messages from `LgymApi.Resources` for user-facing validation, errors, and emails.
- The current project-reference graph is fixed at exactly 18 projects and 90 edges. The authoritative current graph is `docs/modular-monolith/issue-380-project-reference-graph.md`. No project-reference edge may be added, removed, duplicated, or made cyclic outside an approved topology change. `issue-375-project-reference-graph.md` is historical and must remain unchanged.
- Register remaining Application services in `LgymApi.Application/ServiceCollectionExtensions.cs`, extracted module services through their public module facades, and shared technical services in `LgymApi.Infrastructure/ServiceCollectionExtensions.cs`.
- Platform owns shared dispatcher, outbox, and persisted-payload serialization contracts. Extracted modules own their public contracts and providers. Application must not reference either Worker project or namespace.
- Worker implements module contracts and chooses no-op schedulers for testing and Hangfire schedulers otherwise. Notifications owns FCM delivery, while Infrastructure owns Hangfire persistence and the shared EF runtime.
- Persisted command writes use legacy `LgymApi.BackgroundWorker.Common.Commands.*` canonical IDs. Application CLR names are read aliases only. Keep Common job interface identities and recurring job identities unchanged.
- `AuthConstants` is the canonical source for roles, permissions, policies, and claim names.
- Enum-backed front-end choice lists must be returned as lookup items with stable `id` and translated `displayName`; do not expose raw enum values as the UI value source.
- Enum-backed request values that need conversion to application enums must be mapped in API mapping profiles from lookup `id`/string inputs; controllers should not parse them manually.
- Do not migrate to ASP.NET Identity unless a new ADR explicitly supersedes ADR-005.
- Do not remove idempotency or uniqueness constraints in `AppDbContext` without an explicit replacement.
- Follow the repository's `.editorconfig` and existing code style.

## Change Guidance

- Keep edits focused.
- Prefer fixing shared behavior in `common`-style shared areas only when the issue spans multiple layers or projects.
- Do not broaden a task into opportunistic refactors unless explicitly asked.
- Avoid destructive cleanup commands unless explicitly requested.

## Validation Expectations

- Validate the smallest relevant slice first.
- If you change build, packaging, installer, or project wiring, sanity-check the affected solution build.
- If you touch tests or E2E code, state clearly whether the suite was built or run.
- Run relevant build and tests, and state clearly if a command was not run.

## Common Commands

```powershell
git submodule update --init --recursive
dotnet restore LgymApi.sln
dotnet build LgymApi.sln --configuration Release --no-restore
dotnet run --project LgymApi.Api
```

```powershell
dotnet test LgymApi.UnitTests/LgymApi.UnitTests.csproj --configuration Release --no-build
dotnet test LgymApi.ArchitectureTests/LgymApi.ArchitectureTests.csproj --configuration Release --no-build
dotnet test LgymApi.IntegrationTests/LgymApi.IntegrationTests.csproj --configuration Release --no-build
dotnet test LgymApi.DataSeeder.Tests/LgymApi.DataSeeder.Tests.csproj --configuration Release --no-build
dotnet test LgymApi.E2ETests/LgymApi.E2ETests.csproj --configuration Release --no-build --settings LgymApi.E2ETests/LgymApi.E2ETests.runsettings --filter "TestCategory=Harness"
dotnet test LgymApi.E2ETests/LgymApi.E2ETests.csproj --configuration Release --no-build --settings LgymApi.E2ETests/LgymApi.E2ETests.runsettings --filter "TestCategory=ApiHostProof" --logger "trx;LogFileName=issue-433-api-host.trx"
```
