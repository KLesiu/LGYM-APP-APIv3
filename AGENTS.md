# LGYM API - Agent Notes

## Scope

This file applies repo-wide. Keep `AGENT.md` and `AGENTS.md` aligned. More specific `AGENTS.md` files under subtrees override this file for their subtree.

Main areas:

- API: `LgymApi.Api`
- Application: `LgymApi.Application`
- Domain: `LgymApi.Domain`
- Infrastructure: `LgymApi.Infrastructure`
- Resources: `LgymApi.Resources` and `LgymApi.Resources.Generator`
- Background jobs: `LgymApi.BackgroundWorker` and `LgymApi.BackgroundWorker.Common`
- Data seeding: `LgymApi.DataSeeder`
- Tests: `LgymApi.UnitTests`, `LgymApi.IntegrationTests`, `LgymApi.ArchitectureTests`, `LgymApi.DataSeeder.Tests`, `LgymApi.TestUtils`

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
| `LgymApi.Application/LgymApi.Application.csproj` | Use-case and business orchestration: services, repository abstractions, application models, mapping core, module-facing command, scheduling, and serialization contracts, plus application DI. | Own business rules, authorization checks, transactions, and unit-of-work commits here. Never reference a `LgymApi.BackgroundWorker*` project or namespace. |
| `LgymApi.Domain/LgymApi.Domain.csproj` | Core domain: entities, enums, strongly typed IDs, domain helpers, and auth/security constants. | Keep free of HTTP, EF, API, and localization concerns. Domain is localization-neutral and must not reference `LgymApi.Resources`. Do not reorder or renumber existing enums. |
| `LgymApi.Infrastructure/LgymApi.Infrastructure.csproj` | Technical implementations: EF Core `DbContext`, migrations, repositories, unit of work, storage, email, auth/external services, Hangfire persistence, FCM delivery, and infrastructure DI. | Repositories must not call `SaveChangesAsync` or own transactions. Do not register application services or select push schedulers here. |
| `LgymApi.Resources/LgymApi.Resources.csproj` | Localized `.resx` resources for messages, enums, and emails, with generated strongly typed access. | Add or update English and Polish resources for user-facing text. |
| `LgymApi.Resources.Generator/LgymApi.Resources.Generator.csproj` | Roslyn source generator/analyzer used by `LgymApi.Resources`; targets `netstandard2.0` for analyzer compatibility. | Keep deterministic and free of runtime app dependencies. |
| `LgymApi.BackgroundWorker.Common/LgymApi.BackgroundWorker.Common.csproj` | Exact bounded Worker/Infrastructure seam for persisted job interfaces, scheduler bridges, idempotency policy, and email wire contracts. | Do not add Application-facing contracts, commands, serialization, or push delivery types. |
| `LgymApi.BackgroundWorker/LgymApi.BackgroundWorker.csproj` | Hangfire/background worker implementations for Application ports and the bounded Common job seam. | Keep jobs idempotent where practical, register worker services here, and select no-op or Hangfire scheduling by testing mode. |
| `LgymApi.DataSeeder/LgymApi.DataSeeder.csproj` | Console executable for deterministic data seeding/bootstrap using infrastructure and EF tooling. | Do not make API startup depend on this executable. |
| `LgymApi.UnitTests/LgymApi.UnitTests.csproj` | Focused unit tests for service, domain, application, mapping, API, and infrastructure units. | Use NUnit, FluentAssertions, NSubstitute, and shared helpers from `LgymApi.TestUtils`. |
| `LgymApi.IntegrationTests/LgymApi.IntegrationTests.csproj` | End-to-end API tests with `WebApplicationFactory`, middleware, auth, serialization, localization, and test persistence. | Reuse integration helpers and validate legacy contract compatibility for changed endpoints. |
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
- Map between distinct layer models or results, including entities, application/read models, and DTOs, through registered custom `IMapper` mapping profiles (`IMapper`, `IMappingProfile`, `MappingContext`). AutoMapper, ad-hoc mapper implementations, and manual cross-layer mapping in controllers, services, or adapters are prohibited. Trivial scalar assignments without model transformation are allowed.
- Use resource-backed messages from `LgymApi.Resources` for user-facing validation, errors, and emails.
- The current project-reference graph is fixed at exactly 14 projects and 32 edges. The sole approved current edge delta is removal of `LgymApi.Domain -> LgymApi.Resources`; do not add projects or restore that reference. The authoritative current graph is `docs/modular-monolith/issue-380-project-reference-graph.md`; `issue-375-project-reference-graph.md` is historical and must remain unchanged.
- Register Application services in `LgymApi.Application/ServiceCollectionExtensions.cs` and Infrastructure services and repositories in `LgymApi.Infrastructure/ServiceCollectionExtensions.cs`.
- Application owns module-facing background command, password scheduling, push, and persisted-payload serialization contracts. It must not reference either Worker project or namespace.
- Worker implements Application contracts and chooses no-op schedulers for testing and Hangfire schedulers otherwise. Infrastructure owns FCM delivery only, not push scheduler selection.
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
```
