# LGYM API Architecture Guide

This document explains how the backend is structured and how to add a new module in a way that is consistent with existing patterns.

## 1. Solution Structure

- `LgymApi.Api` - HTTP layer (controllers, DTO contracts, validators, middleware, API mapping profiles).
- `LgymApi.Application` - use-case and business orchestration layer (services, repository interfaces, mapping core).
- `LgymApi.Domain` - core domain types (entities, enums, domain-only helpers).
- `LgymApi.Infrastructure` - persistence and technical implementations (EF Core `DbContext`, repository implementations, UoW, migrations).
- `LgymApi.UnitTests` - focused unit tests and architecture guard tests.
- `LgymApi.IntegrationTests` - end-to-end API tests with `WebApplicationFactory` and in-memory database.
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
- Multi-step write use-cases should use `BeginTransactionAsync()` in the service, with explicit commit/rollback.
- Read-only repository queries should prefer `AsNoTracking()` unless tracking is explicitly needed.

### Practical implication

If you add a repository method that mutates data, make it stage-only and ensure the caller service commits once, at the use-case boundary.

### Persistence ownership and identifier contract

The production system has one `AppDbContext`, one database, and one migration stream. Each of the 48 persisted entities still has exactly one module owner. This is logical write ownership only; it does not introduce a physical database, `DbContext`, schema, or migration-stream split. `LgymApi.ArchitectureTests/PersistedEntityOwnershipCatalog.cs` is the executable ownership source of truth, and `docs/modular-monolith/issue-376-ownership-map.md` is its tested documentation view.

Workout execution and completed-training history belong to `Workout & Progress`. `Training.TypePlanDayId` may reference the `Training Planning` definition used to perform a workout, but that reference does not give Training Planning write ownership over the completed `Training` row.

Nutrition owns six persisted entities and 18 focused actions: Diet D1 through D9 and Supplementation S1 through S9. Its four existing controller adapters preserve legacy routes and payloads. Nutrition consumes Coaching only through `ICoachingRelationshipAccessService`, uses module-local stage-only persistence ports, and retains the canonical Diet command ID and payload for D3, D4, and D5 after a successful active-plan save. This is logical ownership only, with the same one `AppDbContext`, database, and migration stream.

Workout & Progress exposes its cross-module surface through `ProgressData`, dashboard, ranking, training execution/history, and accepted-progress contracts with explicit read/write models. Foreign modules must not consume its entities, repositories, or implementation classes directly. Existing legacy routes and payloads remain unchanged. For #386, Reporting stages a Reporting-owned accepted-progress command in the existing `CommandEnvelope` outbox, and Workout & Progress owns delivery-side measurement persistence.

Known internal entity references use `Id<T>`. EF Core stores their provider values in PostgreSQL `uuid` columns, while HTTP and JSON UUID values remain strings. The only polymorphic string ID exceptions are `PushNotificationMessage.EntityId` and `PushEventPayload.EntityId`.

Architecture debt is no-growth. An allowlist entry may be re-keyed only for an owner change with the same source and target identities. New entries, wildcard exemptions, and source or target changes are not permitted. Remove stale entries.

Training Planning's PlanDay service authorizes non-owner access through its consumer-owned `IPlanDayRelationshipAccessPort`. Workout & Progress Measurements authorizes trainer access through its consumer-owned `IMeasurementsRelationshipAccessPort`. Coaching implements and registers both boolean adapters from `ICoachingRelationshipAccessService`, preserving acyclic dependency direction without exposing Coaching repositories or contracts to either consumer.

Coaching owns 31 focused actions, 30 HTTP-backed and the application-only `GetTrainerInvitationsAsync`. Its invitation and dashboard reads enrich complete Coaching facts with active Identity accounts before search, filtering, sorting, totals, and paging. An expired pending email invitation records `Expired` and `RespondedAt`, remains unbound, creates no link, and queues no notification command. The cutover changed neither the single `AppDbContext`, PostgreSQL database, nor migration stream.

Application services own the transaction proof: a staged write becomes visible only after `IUnitOfWork` commits, and a forced failure in a multi-step service transaction must leave no write after rollback. PostgreSQL transaction integration tests enforce both outcomes.

## 4. Mapping Approach

The solution uses a custom mapping system (not AutoMapper):

- Core contracts live in `LgymApi.Application/Mapping/Core`.
- API mapping profiles implement `IMappingProfile` and are placed under `LgymApi.Api/Mapping/Profiles`.
- Profiles are auto-registered via `AddApplicationMapping(...)` in `Program.cs`.
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
- `UserContextMiddleware` resolves current user from JWT claim (`userId`) and places user object into `HttpContext.Items`.
- Controllers read current user via `HttpContext.GetCurrentUser()`.

## 6. How to Add a New Module

Use this checklist for a new feature module (for example: `Achievements`, `Notifications`, etc.).

1. **Domain**
   - Add new entity/enums in `LgymApi.Domain` if needed.

2. **Infrastructure Data Model**
   - Add `DbSet<T>` in `AppDbContext` when the module introduces a new aggregate root that must be queried directly from the context.
   - Add relation/config mapping in a module-owned `Data/Configurations/<Module>/*EntityTypeConfiguration.cs` class.
   - Register the new configuration explicitly in `Data/Configurations/AppDbContextEntityTypeConfigurationRegistrar` and preserve the existing fixed order; do not use assembly scanning.
   - Create and verify EF migration.

3. **Application Contracts**
   - Add repository interface in `LgymApi.Application/Repositories`.
   - Add service interface and models under module folder in `LgymApi.Application`.

4. **Infrastructure Implementation**
   - Implement repository under `LgymApi.Infrastructure/Repositories`.
   - Keep writes staged only; no direct commit.

5. **Application Service**
   - Implement use-case logic.
   - Validate business rules.
   - Commit with UoW once per use-case.
   - Use explicit transaction for multi-step write flows.

6. **API Layer**
   - Add DTO contracts under `Features/<Module>/Contracts`.
   - Add validators under `Features/<Module>/Validation`.
   - Add controller under `Features/<Module>/Controllers`.
   - Keep route and payload naming conventions compatible with existing API style.

7. **Mapping**
   - Add profile in `LgymApi.Api/Mapping/Profiles`.
   - Use mapper in controller instead of hand-mapping where practical.

8. **Dependency Injection**
   - Register service in `LgymApi.Application/ServiceCollectionExtensions.cs`.
   - Register repository and infra dependencies in `LgymApi.Infrastructure/ServiceCollectionExtensions.cs`.

9. **Tests**
   - Add unit tests for service behavior and commit boundaries.
   - Add integration tests for endpoint contracts and authorization paths.
   - Ensure architecture guard tests still pass.

## 7. Testing Conventions

- **Unit tests** validate isolated behavior and architectural guarantees.
  - Examples in this repository include UoW commit behavior checks and mapper configuration validation.
- **Integration tests** validate real HTTP behavior with middleware, auth, serialization, and data persistence.
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

- **Application services**: register in module-owned helpers under `LgymApi.Application`.
- **Infrastructure dependencies**: register in module-owned helpers under `LgymApi.Infrastructure`.
- **Shared platform roots**: keep cross-cutting services in `AddPlatformServices(...)`.
- **Host composition**: `Program.cs` composes module and platform helpers only, plus host-only wiring.

### Registration Ownership

1. **Application Layer**: owns its interfaces, implementation classes, and module-specific business-service helpers.
2. **Infrastructure Layer**: owns repository implementations, external client adapters, and module-specific technical helpers.
3. **Platform carve-out**: shared roots that multiple modules consume stay in `AddPlatformServices(...)` instead of being forced into one feature module.

Notification delivery follows the same ownership rule: Notifications owns its provider-neutral intent policy, including the six typed Coaching intents. Application owns password plus provider-neutral push event/result/scheduling contracts, delivery claims, state transitions, retry policy, and UoW commits. Worker owns command runtime plus environment-selected password, push, and Coaching email scheduling adapters. Infrastructure owns the private FCM implementation and raw tokens, and `Program.cs` composes module-owned helpers in module-before-Worker order without direct adapter bindings.

### Background Contract Ownership

Application owns the Platform dispatcher and stage-only outbox ports at `LgymApi.Application/Platform/Contracts/BackgroundCommands/`, persisted-payload serialization at `LgymApi.Application/Platform/Contracts/Serialization/`, module commands at `LgymApi.Application/Identity/Contracts/BackgroundCommands/`, `LgymApi.Application/WorkoutProgress/Contracts/BackgroundCommands/`, `LgymApi.Application/Coaching/Contracts/BackgroundCommands/`, `LgymApi.Application/Reporting/Contracts/BackgroundCommands/`, and `LgymApi.Application/Nutrition/Contracts/BackgroundCommands/`, Notifications push contracts at `LgymApi.Application/Notifications/Contracts/Push/`, and the Identity password-recovery port at `LgymApi.Application/Features/PasswordReset/Contracts/`.

`LgymApi.BackgroundWorker/Runtime/` owns the closed registry of 15 commands and 16 handlers, with `TrainingCompletedCommand` as the sole two-handler command. Coaching contributes eight commands: three email-only invitation lifecycle commands and five in-app commands, mapped to six Notifications intents. `LgymApi.BackgroundWorker/Notifications/PasswordRecoveryEmailSchedulerAdapter.cs` maps the Identity request to the retained Common email wire payload. `LgymApi.BackgroundWorker.Common/Jobs/` and `LgymApi.BackgroundWorker.Common/Notifications/` are the bounded persisted job and email wire seam only. Common must not regain commands, serialization, push contracts, or Application-facing ports.

Application must not reference either `LgymApi.BackgroundWorker` project or any `LgymApi.BackgroundWorker*` namespace. Canonical persisted command IDs retain their legacy `LgymApi.BackgroundWorker.Common.Commands.*` strings, while Application CLR names are read aliases only. The Worker writes the legacy IDs and owns Hangfire-facing runtime behavior.

### Accepted report progress flow

Reporting accepts a submission, derives valid measurement triples, and stages a Reporting-owned `ReportSubmissionAcceptedProgressCommand` in `CommandEnvelope` before the submission unit of work commits. The envelope is the same-database outbox and is not dispatched by Reporting directly. After the committed intent is dispatched through the existing ActionMessage infrastructure, the Worker handler invokes the Workout & Progress consumer. That consumer validates the event, deduplicates by trainee, body part, and `ObservedAt` UTC day, and owns the measurement rows. Invalid, unsupported-schema, or poison deliveries are sanitized and bounded for the existing retry/dead-letter path; unexpected persistence exceptions remain retryable.

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

## 12. Quick Module Skeleton (Minimal)

For module `FeatureX`, add:

- `LgymApi.Application/FeatureX/IFeatureXService.cs`
- `LgymApi.Application/FeatureX/FeatureXService.cs`
- `LgymApi.Application/Repositories/IFeatureXRepository.cs`
- `LgymApi.Infrastructure/Repositories/FeatureXRepository.cs`
- `LgymApi.Api/Features/FeatureX/Controllers/FeatureXController.cs`
- `LgymApi.Api/Features/FeatureX/Contracts/FeatureXDtos.cs`
- `LgymApi.Api/Features/FeatureX/Validation/*.cs`
- `LgymApi.Api/Mapping/Profiles/FeatureXProfile.cs`
- `LgymApi.UnitTests/FeatureXServiceTests.cs`
- `LgymApi.IntegrationTests/FeatureXTests.cs`

Then register service/repository in both service collection extension files and add migration if persistence changed.

## 13. Modular monolith direction

### Source of truth

- `#311` is the constraint authority for the modular-monolith direction.
- `#375` is the historical baseline and inventory source.
- `#380` is the current background-contract ownership and project-reference source.
- `#381` defines the Notifications write-ownership boundary and provider-neutral public contract surface; it does not move projects, entities, or runtime behavior.
- `#391` codifies Workout & Progress logical ownership and path classification without changing the shared persistence topology or legacy API contracts.
- `#390` codifies Nutrition's six-entity, 18-action logical boundary and compatibility adapters without changing the shared persistence topology, command identity, or legacy API contracts.
- `docs/adr/006-lgym-evolves-as-modular-monolith.md` records the decision.

### Issue #376 links

- `docs/adr/006-lgym-evolves-as-modular-monolith.md`
- `docs/modular-monolith/issue-376-module-context-map.md`
- `docs/modular-monolith/issue-376-ownership-map.md`
- `docs/modular-monolith/issue-380-background-contract-ownership.md`
- `docs/modular-monolith/issue-380-project-reference-graph.md`
- `docs/modular-monolith/issue-381-notifications-boundary.md`
- `docs/modular-monolith/issue-390-nutrition-boundary.md`
- `docs/modular-monolith/issue-392-reporting-boundary.md`

The current layered runtime stays in place until a later change explicitly alters it.
The compatibility, persistence, and Unit of Work guidance elsewhere in this guide continues to apply and is not restated here.
