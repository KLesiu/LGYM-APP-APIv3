# LGYM API Architecture Guide

This document explains how the backend is structured and how to add a new module in a way that is consistent with existing patterns.

## 1. Solution Structure

- `LgymApi.Api` - HTTP layer (controllers, DTO contracts, validators, middleware, API mapping profiles).
- `LgymApi.Application` - remaining Reporting, Workout & Progress, Coaching, and Nutrition use-case orchestration.
- `LgymApi.Platform`, `LgymApi.Identity`, `LgymApi.TrainingPlanning`, and `LgymApi.Notifications` - stable module assemblies with public facades, explicit contracts, and internal implementations.
- `LgymApi.Domain` - core domain types (entities, enums, domain-only helpers).
- `LgymApi.Infrastructure` - shared persistence and technical runtime (EF Core `AppDbContext`, UoW, migrations, Hangfire persistence, and module context bridges).
- `LgymApi.UnitTests` - focused unit tests.
- `LgymApi.ArchitectureTests` - Roslyn-based architecture guards.
- `LgymApi.IntegrationTests` - end-to-end API tests with `WebApplicationFactory`, in-memory coverage, and prepared PostgreSQL coverage.
- `LgymApi.Resources` and `LgymApi.Resources.Generator` - localized resources and source generators for strongly-typed message access.

## 2. Request Flow

1. **Controller (API)** receives request DTO.
2. **FluentValidation** validates shape and basic constraints.
3. **Application service** executes use-case logic and authorization/business checks.
4. **Repository implementations** stage entity changes or query read models.
5. **Unit of Work** commits staged changes at service boundary.
6. **Mapping profiles** translate domain/application outputs into API response DTOs.
7. **Middleware** translates exceptions to HTTP responses.

## 3. Unit of Work Rules (Critical)

The project uses explicit Unit of Work semantics.

- Repositories **must not call** `SaveChangesAsync`.
- Repositories stage operations only (`Add`, `Update`, `Remove`, query methods).
- Application services own commit timing with `IUnitOfWork.SaveChangesAsync()`.
- One `SaveChangesAsync()` is the default atomic boundary for a write use case.
- Use `BeginTransactionAsync()` only for multiple saves, intermediate flushes, rollback across those saves, or a verified owner-specific requirement.
- Read-only repository queries should prefer `AsNoTracking()` unless tracking is explicitly needed.

### Practical implication

If you add a repository method that mutates data, make it stage-only and ensure the caller service commits once, at the use-case boundary.

### Persistence ownership and identifier contract

The production system has one `AppDbContext`, one design-time `AppDbContextFactory`, one `AppDbContextModelSnapshot`, one database, and one migration stream rooted in Infrastructure. The factory constructs the same Npgsql model without API/runtime DI. Each of the 48 persisted entities still has exactly one module owner. This is logical write ownership only; it does not introduce a physical database, `DbContext`, schema, or migration-stream split. `LgymApi.ArchitectureTests/PersistedEntityOwnershipCatalog.cs` is the executable ownership source of truth, and `docs/modular-monolith/issue-376-ownership-map.md` is its tested documentation view.

Workout execution and completed-training history belong to `Workout & Progress`. `Training.TypePlanDayId` may reference the `Training Planning` definition used to perform a workout, but that reference does not give Training Planning write ownership over the completed `Training` row.

Nutrition owns six persisted entities and 18 focused actions: Diet D1 through D9 and Supplementation S1 through S9. Its four existing controller adapters preserve legacy routes and payloads. Nutrition consumes Coaching only through `ICoachingRelationshipAccessService`, uses module-local stage-only persistence ports, and retains the canonical Diet command ID and payload for D3, D4, and D5 after a successful active-plan save. This is logical ownership only, with the same one `AppDbContext`, database, and migration stream.

Workout & Progress exposes its cross-module surface through `ProgressData`, dashboard, ranking, training execution/history, and accepted-progress contracts with explicit read/write models. Its seven focused persistence ports use `Id<AccountReference>` and immutable persistence models; only Infrastructure adapters convert those IDs to persisted `Id<User>` foreign keys through `WorkoutPersistenceAccountIds`. Foreign modules must not consume its entities, repositories, or implementation classes directly. Existing legacy routes and payloads remain unchanged. For #386, Reporting stages a Reporting-owned accepted-progress command in the existing `CommandEnvelope` outbox, and Workout & Progress owns delivery-side measurement persistence.

Reporting uses exactly five focused persistence ports for templates, requests/submissions, recurring assignments, photos/upload sessions, and relationship access. Their Infrastructure adapters are stage-only and no-tracking for reads; Reporting services retain authorization, transaction, and UoW commit ownership. The separate Workout & Progress accepted-progress persistence port is not a sixth Reporting port.

Known internal entity references use `Id<T>`. Reporting and Workout & Progress Application persistence contracts use `Id<AccountReference>` for account identity; only their Infrastructure adapters rebind those values to persisted `Id<User>` foreign keys. EF Core stores provider values in PostgreSQL `uuid` columns, while HTTP and JSON UUID values remain strings. The only polymorphic string ID exceptions are `PushNotificationMessage.EntityId` and `PushEventPayload.EntityId`.

Architecture debt is enforced as direct semantic zero without exception machinery. Wildcard inputs and scanner path or classifier exclusions are not permitted. The dependency, direct entity/repository, and public-surface guards independently compile every production source in Application, Domain, Platform, Identity, TrainingPlanning, and Notifications, require a nonzero source-tree count for each assembly, and print every observed violation on failure. Internal persisted foreign keys remain entity-typed, while cross-module contracts use marker IDs.

Training Planning's PlanDay service accepts marker-only commands and read models, authorizing non-owner access through its consumer-owned account-ID `IPlanDayRelationshipAccessPort`. Workout & Progress Measurements authorizes trainer access through its consumer-owned `IMeasurementsRelationshipAccessPort`. Coaching implements and registers both boolean adapters from `ICoachingRelationshipAccessService`, preserving acyclic dependency direction without exposing Coaching repositories or contracts to either consumer.

Coaching owns 31 focused actions, 30 HTTP-backed and the application-only `GetTrainerInvitationsAsync`. Its invitation and dashboard reads enrich complete Coaching facts with active Identity accounts before search, filtering, sorting, totals, and paging. An expired pending email invitation records `Expired` and `RespondedAt`, remains unbound, creates no link, and queues no notification command. The cutover changed neither the single `AppDbContext`, PostgreSQL database, nor migration stream.

Application services own the transaction proof: a staged write becomes visible only after `IUnitOfWork` commits, and a forced failure in a multi-step service transaction must leave no write after rollback. PostgreSQL transaction integration tests enforce both outcomes.

## 4. Mapping Approach

The solution uses a custom mapping system (not AutoMapper):

- Core contracts compile from `LgymApi.Platform/Mapping/Core` while retaining their established `LgymApi.Application.Mapping.Core` namespaces.
- API mapping profiles implement `IMappingProfile` and are placed under `LgymApi.Api/Mapping/Profiles`.
- Profiles are registered via `AddApplicationMapping(...)` with the exact API, Application, Platform, Identity, TrainingPlanning, and Notifications assembly markers in `Program.cs`; missing or duplicate markers fail fast.
- `MappingContext` with typed `ContextKey<T>` is used for contextual mapping inputs (e.g., translation dictionaries).

When adding new responses, prefer profile-based mapping and keep controllers thin.

Controller rule (enforced): controllers must not construct response DTOs directly (`new *Dto`).
Controllers should call services and return mapped outputs through `IMapper` / mapping profiles.

### 4.1 Nested Mapper Composition Rules

Prefer `context.Map<TTarget>(source)` and `context.MapList<TTarget>(sourceList)` inside profile delegates when mapping nested objects/lists that already have a registered map. Use `Map<TSource, TTarget>` / `MapList<TSource, TTarget>` only when you intentionally need compile-time source typing.

- Prefer nested composition over duplicated inline nested DTO construction.
- Keep manual nested mapping only when:
  - domain-specific shape changes are required for that endpoint,
  - contextual key logic cannot be represented by existing nested maps,
  - fallback payload rules differ from shared DTO mapping behavior.
- Always reuse the same `MappingContext` for nested calls so context keys and guards propagate consistently.
- Ensure nested source/target pairs are registered; missing registrations should fail fast in tests.
- Avoid recursive self-mapping loops in profile delegates; cycle protection is built-in and should not be bypassed.

### 4.2 Mapper Review Checklist

When reviewing mapper changes:

1. Is nested DTO mapping reusing existing maps (`context.Map`/`context.MapList`) where possible?
2. Are any manual nested mappings justified by endpoint-specific behavior?
3. Do context keys required by nested maps remain available and allowed?
4. Are regression tests present for nested object/list success paths and missing-map/cycle failure paths?
5. Do affected integration tests still verify response contract compatibility?

## 5. Error and Auth Pipeline

- Use `AppException` for controlled domain/application errors (`BadRequest`, `Forbidden`, `NotFound`, etc.).
- `ExceptionHandlingMiddleware` maps `AppException` and fallback exceptions to HTTP payloads.
- `UserContextMiddleware` validates JWT `userId` and `sid` through the Identity marker-contract resolver and places immutable `AuthenticatedAccountContext` into `HttpContext.Items`.
- New API adapters read marker IDs and facts via `HttpContext.GetAuthenticatedAccountContext()` / `GetCurrentAccountId()`. Middleware and controllers do not materialize a domain user or use a legacy user item.

## 6. Contributing a Use Case or Module-Boundary Change

Start with the canonical owner and follow the conditional layouts in the [Module Contribution Guide](MODULE_CONTRIBUTION_GUIDE.md). Edit only the seams that the owner and use case need, whether the owner is an extracted module or an Application-owned capability.

- Retain one `AppDbContext`, PostgreSQL database, and migration stream. Logical ownership never creates a separate physical persistence topology.
- Preserve existing endpoint routes, verbs, payload shapes, legacy fields, and registered mapping boundaries.
- Keep repositories stage-only and use the service-owned one-save atomic boundary by default. Use an explicit transaction only for multiple saves, intermediate flushes, rollback across those saves, or a verified owner-specific requirement.
- Add focused behavior, integration, and architecture coverage, then run the relevant guards for the changed owner and boundary.

## 7. Testing Conventions

- **Unit tests** validate isolated behavior.
   - Examples in this repository include UoW commit behavior checks and mapper configuration validation.
- **Architecture tests** validate Roslyn-based dependency, boundary, mapping, DI, and persistence guards.
- **Integration tests** validate real HTTP behavior with middleware, auth, serialization, and data persistence through in-memory and prepared PostgreSQL coverage.
  - Reuse `IntegrationTestBase` helpers for seeding users, setting auth headers, and creating dependent data.

Recommended validation path for new modules:

1. Add service-level unit tests first.
2. Add controller/API integration tests second.
3. Run full test suite before merge.

## 8. Compatibility and Safety Notes

- Preserve legacy payload contract compatibility (`_id`, `msg`, `req`, route naming patterns) unless a planned API versioning change is approved.
- Avoid direct EF bulk update patterns that bypass UoW semantics; use existing staged update conventions.
- Keep transaction ownership in services, not repositories.
- Keep controllers thin: parse inputs, call service, map outputs.

## 9. DTO Enum and Localization Rules (API Contract)

These rules are required for new API modules and for updates to existing endpoints.

- **Enum fields in DTOs**: if a response DTO contains an enum concept, expose it as the enum type in DTO (for example `TrainerDashboardTraineeStatus Status`) instead of manual string/int shadow fields.
- **No duplicate enum fields**: avoid parallel properties like `status` + `statusEnum` unless explicitly required by an approved backward-compatibility requirement.
- **Serialization behavior**: keep enum serialization aligned with global JSON settings in `Program.cs` (`JsonStringEnumConverter`) so API responses use enum names.
- **Mapping rule**: map enum-to-enum in mapping profiles; do not force `ToString()` in profile mapping unless contract explicitly expects raw string.
- **Validation/user messages**: do not hardcode user-facing validator messages. Always use strongly typed `LgymApi.Resources.Messages` entries.
- **Resource updates**: when adding a new validation/error message, add keys in both `LgymApi.Resources/Resources/Messages.resx` and `LgymApi.Resources/Resources/Messages.pl.resx`.
- **Tests for messages**: integration tests for invalid inputs should assert localized resource-driven messages (not hardcoded text literals).

### Enum Evolution Safety (Do/Do Not)

When changing existing enums, treat them as part of a persisted and externally consumed contract.

- **Reordering is forbidden**: do not change the declaration order of existing enum members.
- **Renumbering is forbidden**: do not change explicit numeric values of existing members.
- **Editing requires client impact review**: any enum value rename or semantic change requires coordinated app update planning (mobile/web/API consumers).
- **Prefer deprecation over deletion**: do not remove enum members in normal flow; mark them with `[Obsolete]` first and keep compatibility until a planned removal window.
- **If removal is unavoidable**: document migration steps, update all mappings/validators/tests, and communicate a breaking change before merge.

## 10. DTO and Model ID Conventions (Boundary Guards)

The solution enforces strict boundaries between API contracts and internal application models regarding ID types. These rules are enforced by architecture tests and CI will fail on violations.

### 10.1 API Contracts (External Layer)

- **Rule**: DTOs and models located under `/Contracts/` namespaces/folders must use raw `string` for ID fields.
- **Reasoning**: External API consumers (mobile, web) expect standard string GUIDs. Strongly typed IDs like `Id<T>` are internal implementation details and must not leak into the public API contract.
- **Enforcement**: `ApiContractTypedIdGuardTests` ensures no `Id<TEntity>` usage in `/Contracts/`.

### 10.2 Application Input Models (Internal Layer)

- **Rule**: Internal application models, specifically those ending in `Input` (e.g., `UpdateWorkoutInput`) located under `LgymApi.Application/**/Models/*Input*.cs`, must use strongly typed IDs (`Id<TEntity>`).
- **Reasoning**: This prevents "primitive obsession" and accidental ID swaps (e.g., passing a User ID where a Workout ID is expected) within the business logic layer.
- **Enforcement**: `ApplicationInputModelStringIdGuardTests` ensures no raw `string` ID usage in `*Input.cs` files.

### 10.3 Mapping Boundary

The boundary is handled at the mapping layer:
- **API to Application**: Validators or Mapping Profiles translate incoming `string` IDs from DTOs into `Id<TEntity>` for Application Input models.
- **Application to API**: Mapping Profiles translate `Id<TEntity>` from Domain entities or Application models back into `string` for response DTOs.
- **Lookup-backed enum inputs**: when a request DTO carries a lookup-backed enum value, the API layer maps the lookup `id`/string to the application enum in a mapping profile. Controllers must not hand-parse enum strings for these cases.

## 11. Dependency Injection Conventions

The solution uses module-owned registration helpers composed by the host, enforced by architecture guards in unit tests.

- **Application services**: `AddApplication` composes Reporting, Workout & Progress, Coaching, Nutrition, and the internal Platform command-envelope runtime helper; extracted modules keep their own public facades.
- **Infrastructure dependencies**: `AddInfrastructure` composes shared persistence/Hangfire/post-commit roots and remaining persistence adapters, but no extracted implementation service or provider.
- **Shared platform roots**: keep cross-cutting services in `AddPlatformServices(...)`.
- **Host composition**: `Program.cs` composes Platform, Identity, Training Planning, Notifications, remaining Application, Infrastructure, API adapters, and Worker in that exact order. Narrow Infrastructure helpers are not host entrypoints.

### Registration Ownership

1. **Application Layer**: owns its interfaces, implementation classes, and module-specific business-service helpers.
2. **Infrastructure Layer**: owns repository implementations, external client adapters, and module-specific technical helpers.
3. **Platform carve-out**: shared roots that multiple modules consume stay in `PlatformModule.AddPlatformModule` instead of being forced into one feature module. AppConfig, enum lookup, and unit conversion live in Platform's non-canonical `ReferenceData` sub-boundary.

Neutral application primitives are public only through `BuildingBlocks/Results` and `BuildingBlocks/Errors`; feature-specific services, repositories, DTOs, errors, and provider details do not belong in that shared surface.

### Final Platform boundaries

`Platform / Reference Data` remains one canonical module. It contains three internal sub-boundaries, none of which is a module or a project:

- `LgymApi.Platform/BuildingBlocks/` contains only the neutral public manifest: `Result<T, TError>`, `Result`, `Unit`, `AppError`, `NotFoundError`, `BadRequestError`, `UnauthorizedError`, `ForbiddenError`, `ConflictError`, `UnprocessableEntityError`, and `InternalServerError`. Their established `LgymApi.Application.BuildingBlocks.*` namespaces remain unchanged.
- `LgymApi.Platform/Contracts/`, `Mapping/`, `Pagination/`, and `Repositories/` are Technical Platform. They own the established background-command and serialization contracts, mapping core/registration, pagination contracts, and the Unit of Work and reliability ports while retaining their established `LgymApi.Application.*` namespaces. The public `AddPlatformModule` facade remains in Application until the Reference Data extraction.
- `LgymApi.Platform/ReferenceData/` owns AppConfig, enum lookup, and unit conversion. `IAppConfigRepository` retains its legacy `Application/Repositories` namespace for source compatibility but is classified as an AppConfig-specific Reference Data port. Its internal `AddReferenceDataServices` helper is composed only by `PlatformModule.AddPlatformModule`.

Reference Data may depend on its approved Technical Platform contracts, BuildingBlocks, and Domain types. BuildingBlocks may depend only on the BCL. Technical Platform and Reference Data do not gain feature-workflow ownership through those roots. Platform-owned repositories use only `IPlatformPersistenceContext`; Infrastructure retains `AppDbContext`, the UoW implementation, migrations, Hangfire, and typed-ID conventions.

AppConfig owns `IAppConfigAuthorizationPort` and calls it once for protected operations. Identity implements the scoped adapter with its own user and role repositories, preserving the ID-only boundary. AppConfig no longer consumes Identity repositories directly; its unauthenticated latest-by-platform lookup remains outside that authorization flow.

Enum lookup is owned by Reference Data. `EnumLookupMappingProfile` registers the six concrete enum mappings, and `EnumService` keeps raw member `id`, translated `name` and `displayName`, hidden-value filtering, and case-insensitive type lookup. API mapping composes those Application lookup models rather than formatting enum values in controllers or feature profiles.

Notification delivery follows the same ownership rule: Notifications owns its provider-neutral intent policy, including the six typed Coaching intents, push event/result/scheduling contracts, delivery claims, state transitions, retry policy, UoW commits, private FCM implementation/configuration, and five stage-only repositories/mappings behind `INotificationsPersistenceContext`. The implementation compiles from `LgymApi.Notifications` while retaining compatible application namespaces. Application owns the internal command-envelope lifecycle runtime; Worker owns the closed command-handler registry, raw/string dispatch boundary, and Hangfire-facing host behavior. Password-recovery, push, and Coaching email scheduling adapters are selected by their owner modules, while Worker retains generic Common scheduler forwarding and no-op versus Hangfire host scheduling. Infrastructure retains the one-context bridge, global phase coordinator, migrations, and Hangfire persistence/server registration; the final Worker facade delegates enabled server hosting to that Infrastructure-owned helper.

The composition facades are closed and ordered. `AddNotificationsModule(configuration)` owns Notifications policy, repositories, email, and FCM; `AddApplication` owns the four remaining Application modules plus the internal command-envelope runtime helper; `AddInfrastructure` owns shared technical roots, stage-only persistence, and the internal Npgsql duplicate-recovery classifier; Application and Notifications API-adapter facades follow Infrastructure; and `AddBackgroundWorkerServices` is last so Testing resolves no-op schedulers while non-testing resolves Hangfire schedulers.

Infrastructure registration ownership is explicit: Notifications owns email registration and selection of Dummy or SMTP senders; Reporting owns its photo-storage registration and Local or Cloudflare R2 selection; Identity owns Google-token validation registration; Notifications owns the FCM provider registration; and API logging owns the optional Elasticsearch sink. Provider SDKs, credentials, and raw provider responses do not belong in Application or BuildingBlocks.

### Background Contract Ownership

Platform owns the dispatcher and stage-only outbox ports at `LgymApi.Platform/Contracts/BackgroundCommands/` and persisted-payload serialization at `LgymApi.Platform/Contracts/Serialization/`; their established `LgymApi.Application.Platform.Contracts.*` namespaces remain unchanged. Application retains module commands at `LgymApi.Application/Identity/Contracts/BackgroundCommands/`, `LgymApi.Application/WorkoutProgress/Contracts/BackgroundCommands/`, `LgymApi.Application/Coaching/Contracts/BackgroundCommands/`, `LgymApi.Application/Reporting/Contracts/BackgroundCommands/`, and `LgymApi.Application/Nutrition/Contracts/BackgroundCommands/`, Notifications push contracts at `LgymApi.Application/Notifications/Contracts/Push/`, and the Identity password-recovery port at `LgymApi.Application/Features/PasswordReset/Contracts/`.

`LgymApi.BackgroundWorker/Runtime/` owns the closed registry of 15 commands and 16 handlers, with `TrainingCompletedCommand` as the sole two-handler command. Coaching contributes eight commands: three email-only invitation lifecycle commands and five in-app commands, mapped to six Notifications intents. Notifications-owned password-recovery and Coaching email adapters map owner requests to retained Common email wire payloads; Worker retains generic scheduler forwarding. `LgymApi.BackgroundWorker.Common/Jobs/` and `LgymApi.BackgroundWorker.Common/Notifications/` are the bounded persisted job and email wire seam only. Common must not regain commands, serialization, push contracts, or Application-facing ports.

Application must not reference either `LgymApi.BackgroundWorker` project or any `LgymApi.BackgroundWorker*` namespace. Canonical persisted command IDs retain their legacy `LgymApi.BackgroundWorker.Common.Commands.*` strings, while Application CLR names are read aliases only. The Worker writes the legacy IDs and owns Hangfire-facing runtime behavior.

### Accepted report progress flow

Reporting accepts a submission, derives and validates its versioned measurement payload, and stages a Reporting-owned `ReportSubmissionAcceptedProgressCommand` in `CommandEnvelope` before the submission unit of work commits. The envelope is the same-database outbox and is not dispatched by Reporting directly. After the committed intent is dispatched through the existing ActionMessage infrastructure, the Worker handler forwards shared raw JSON to the Workout & Progress owner. Workout's raw parser validates and maps that wire representation into its consumer event. The consumer deduplicates by trainee, body part, and `ObservedAt` UTC day, and owns the measurement rows. Invalid, unsupported-schema, or poison deliveries are sanitized and bounded for the existing retry/dead-letter path; unexpected persistence exceptions remain retryable.

Operators can trace this flow with event ID, report submission ID, correlation ID, causation ID, schema version, outcome, retry or dead-letter state, and aggregate counts. Logs and operational records must not contain raw answer JSON, photos, device tokens, or payload dumps.

### Forbidden Patterns 

- **Cross-Boundary Registration**: the Infrastructure project **must not** register Application services. This is enforced by a `CrossBoundary` architecture guard.
- **Untracked Concrete Placements**: every concrete service implementation in `LgymApi.Application` or `LgymApi.Infrastructure` must have a corresponding owner helper.
- **Implementation Leaks**: avoid registering infrastructure-specific concrete types in the Application layer.
- **Unsafe Duplicate Assumptions**: do not rely on implicit registrations from other projects; use the named module helpers and `AddPlatformServices(...)` in `Program.cs`.

### Intentional Exceptions

- **Multi-registration Collections**: multiple implementations for a single interface (for example `IPipelineStep`) are allowed and expected in certain orchestration scenarios.
- **Factory/Instance Registrations**: manual factory delegates `(sp => ...)` or pre-constructed instances are permitted for complex initialization but should be used sparingly.

### Verification

DI registrations are automatically verified by `ServiceRegistrationGuardTests`. If you add a new service class, the build/test pipeline will fail until it is correctly registered in the appropriate `ServiceCollectionExtensions.cs` file.

## 12. Contribution References

Use the [Module Contribution Guide](MODULE_CONTRIBUTION_GUIDE.md) for owner-first contribution workflow, conditional layouts, persistence, compatibility, and verification rules. [NEW_MODULES_USAGE.md](NEW_MODULES_USAGE.md) is feature usage documentation, not a universal implementation template.

## 13. Modular monolith direction

### Source of truth

- `#311` is the constraint authority for the modular-monolith direction.
- `#375` is the historical baseline and inventory source.
- `#380` is the current background-contract ownership and project-reference source.
- `#381` defines the Notifications write-ownership boundary and provider-neutral public contract surface. Its original non-relocation scope is historical; #387 later moved the approved Notifications implementation, persistence, and provider sources into `LgymApi.Notifications` without changing runtime contracts.
- `#391` codifies Workout & Progress logical ownership and path classification without changing the shared persistence topology or legacy API contracts.
- `#390` codifies Nutrition's six-entity, 18-action logical boundary and compatibility adapters without changing the shared persistence topology, command identity, or legacy API contracts.
- `#393` defines the executable concern-owner matrix for Platform and Reference Data.
- `docs/adr/006-lgym-evolves-as-modular-monolith.md` records the direction, while `docs/adr/007-final-modular-monolith-compatibility-commitments.md` records the final compatibility commitments.

### Issue #376 links

- `docs/adr/006-lgym-evolves-as-modular-monolith.md`
- `docs/modular-monolith/issue-376-module-context-map.md`
- `docs/modular-monolith/issue-376-ownership-map.md`
- `docs/modular-monolith/issue-380-background-contract-ownership.md`
- `docs/modular-monolith/issue-380-project-reference-graph.md`
- `docs/modular-monolith/issue-381-notifications-boundary.md`
- `docs/modular-monolith/issue-390-nutrition-boundary.md`
- `docs/modular-monolith/issue-392-reporting-boundary.md`
- `docs/modular-monolith/issue-393-platform-reference-data-boundary.md`
- `docs/modular-monolith/issue-395-final-verification.md`

The project-reference manifest fixes the current solution at 18 projects and 90 unique, justified direct edges: 89 have Roslyn-resolved source/import evidence and one is the Resources analyzer edge. The import guard rejects unused edges, missing direct imports, transitive reliance, forbidden edges, duplicates, cycles, and topological-order drift. The graph document also fixes the dependency-first order and 216-edge forbidden complement.
The production topology remains one `AppDbContext`, one PostgreSQL database, and one migration stream. The eight owner totals remain Identity & Accounts 9, Notifications 5, Reporting 7, Training Planning 3, Workout & Progress 10, Coaching 4, Nutrition 6, and Platform / Reference Data 4, for 48 persisted entities.
The compatibility, persistence, and Unit of Work guidance elsewhere in this guide continues to apply and is not restated here.

The final handoff has 25 scoped Application API adapters and three scoped Notifications API adapters. Migration-era `Task7`, `ApiCompatibility`, and `Compatibility.Task7` adapter CLR identities are removed, but established HTTP, JSON, DTO, route, policy, and endpoint-specific legacy-field contracts remain unchanged. The three retained Notifications integration adapters are owner-to-owner seams, not API adapters: `PushInstallationSessionDisassociationAdapter`, `CoachingEmailNotificationSchedulerAdapter`, and `PasswordRecoveryEmailSchedulerAdapter`. See ADR-007 and issue-395 final verification for their removal conditions and the Todo 22 clean same-SHA evidence workflow.
