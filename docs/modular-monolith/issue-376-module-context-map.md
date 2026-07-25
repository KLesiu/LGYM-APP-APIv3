# Issue #376: Module Context Map

## Status
Draft

## Source precedence

- `#311` defines the constraints and allowed direction.
- `#375` provides the historical baseline for module clusters.
- `#380` provides the current background-contract ownership and project-reference graph.
- `#381` defines the Notifications write-ownership boundary and provider-neutral contract surface.
- `#391` codifies Workout & Progress logical ownership and application-path classification without a persistence or API split.
- `docs/ARCHITECTURE.md` is the reader-facing integration guide for the current maps.
- ADR-006 explains why the modular-monolith direction exists.

## Scope

This file defines the current module catalog and the draft policy notes that later issue-376 tasks will refine.

## Module catalog

The #375 baseline already shows these as the current feature-level dependency clusters, and #311 constrains how far the modular-monolith direction can go.

## Platform / Reference Data

### Responsibility boundary

- Owns host startup, composition root wiring, shared configuration, cross-cutting infrastructure hooks, and reference data that multiple modules depend on.
- Does not own end-user workflows, business decisions, or feature-specific write rules.

### Public contract surface

- Exposes shared bootstrapping, DI registration, lookup data, and other stable platform services that the feature modules consume.
- Publishes only cross-module primitives and lookup payloads, not feature commands or feature-specific read models.

### Final internal sub-boundaries

`Platform / Reference Data` is one canonical module with three non-canonical internal sub-boundaries. None is a project or a ninth module.

| Sub-boundary | Final root | Responsibility |
| --- | --- | --- |
| BuildingBlocks | `LgymApi.Application/BuildingBlocks/` | Exact neutral public manifest: results, `Unit`, and the eight base application errors only. |
| Technical Platform | `LgymApi.Application/Platform/Contracts/` | Shared background-command and serialization contracts, pagination, reliability, Unit of Work ports, and the public Platform facade. |
| Reference Data | `LgymApi.Application/Platform/ReferenceData/` | AppConfig, enum lookup, unit conversion, and the internal registration helper composed only by `AddPlatformModule`. |

BuildingBlocks may consume only the BCL. Technical Platform may consume BuildingBlocks and approved Domain-neutral primitives. Reference Data may consume BuildingBlocks, approved Technical Platform contracts, and Domain types. These classifications do not change the canonical owner name in the eight-module catalog.

AppConfig owns the ID-only `IAppConfigAuthorizationPort`. Identity implements the port from Identity-owned repositories and registers the adapter. AppConfig has no direct Identity-repository dependency. Its latest-by-platform read remains unauthenticated.

Reference Data owns the six concrete enum-to-lookup mappings and the lookup service. API consumers compose those maps, preserving raw `id`, translated `name` and `displayName`, hidden-value filtering, and case-insensitive enum type lookup.

## Identity & Accounts

### Responsibility boundary

- Owns authentication, account state, user identity, roles, permissions, sessions, and account-scoped lifecycle rules.
- Does not own training, reporting, workout, coaching, or notification business flows beyond the account events they consume.

### Public contract surface

- Exposes login, logout, profile, identity, and authorization contracts that other modules can rely on.
- Publishes account identity events and account lookup data, while keeping credential handling and account mutation rules internal.

## Notifications

### Responsibility boundary

- Owns in-app notifications, push registration, push delivery lifecycle, and stale-installation cleanup.
- Does not own the business event that triggered the message, only the delivery and audit of that message.
- Owns module responsibility and writes, not a physical relocation of notification entities, projects, or runtime adapters; see `issue-381-notifications-boundary.md`.

### Public contract surface

- Exposes notification submission, delivery status, and installation management contracts for other modules.
- Accepts domain events and notification requests from other modules, but keeps provider integration details internal.

## Reporting

### Responsibility boundary

- Owns report templates, report requests, report submissions, and report-photo workflows. Accepted-progress measurement persistence belongs to Workout & Progress.
- Does not own training plans, workout scoring, or coaching relationships, except where those inputs are required to validate reporting access.

### Public contract surface

- Exposes report request, submission, template, and photo-upload contracts to the rest of the system.
- Publishes reporting statuses, submission views, and the Reporting-owned accepted-progress command without leaking storage concerns.

## Training Planning

### Responsibility boundary

- Owns plan creation, plan editing, plan-day structure, and planning-time user ownership rules.
- Does not own workout execution history, score calculation, or coach-trainee relationship management.

### Public contract surface

- Exposes plan management commands, plan views, and plan-sharing contracts used by adjacent modules.
- Publishes planning data that other modules can read, while keeping plan mutation rules inside the module.

## Workout & Progress

### Responsibility boundary

- Owns workout sessions, exercise progress, score calculation, ELO or rank updates, and the history needed to track progress over time.
- Does not own planning, account identity, or coaching links, even when those inputs are needed to validate a workout action.
- Owns completed `Training` rows even when `Training.TypePlanDayId` references the `Training Planning` definition used for the performed workout. That reference does not transfer completed-training write ownership to Training Planning.
- `LgymApi.Application/WorkoutProgress/` and the legacy `Training`, `Exercise`, `ExerciseScores`, `Gym`, `Measurements`, `MainRecords`, and `EloRegistry` application folders are classified as this module until physical moves or wrappers replace the legacy paths.

### Public contract surface

- Exposes workout entry, history, scoring, and progress contracts for the UI and neighboring modules.
- Publishes progress summaries, workout events, and score updates, while keeping calculation details internal.
- The explicit neighboring-module surface is `ProgressData` read/write models, dashboard and ranking reads, training execution/history contracts, and the production `#386` accepted-progress consumer contract. It does not expose entities, repositories, or service implementations. Reporting stages the accepted-progress command, while Workout & Progress validates and owns measurement persistence.

## Coaching

### Responsibility boundary

- Owns trainer and trainee relationships, invitations, dashboard views, and the coordination layer between a coach and the people they support.
- Does not own identity, workout execution, reporting storage, or plan structure, even when it can initiate actions in those modules.

### Public contract surface

- Exposes coach-trainee relationship, invitation, and dashboard contracts that other modules can query or react to.
- Exposes `ICoachingRelationshipAccessService` as the ID-only Reporting/Nutrition authorization surface; `CoachingRelationshipAccessDecision` reports trainer status and active-link existence independently.
- Implements the Training Planning-owned `IPlanDayRelationshipAccessPort` and Workout & Progress-owned `IMeasurementsRelationshipAccessPort` as boolean projections of its access decision. Both adapters are registered by `AddCoachingModule`, so neither consumer depends on Coaching contracts, persistence, or implementations.
- Publishes coaching relationship changes and request flows, while delegating actual workout, plan, or report behavior to the owning module.

## Nutrition

### Responsibility boundary

- Owns diet plans, meals, supplement plans, plan history, and intake logs.
- Owns 18 focused actions: Diet D1-D9 and Supplementation S1-S9, while retaining the four existing controller adapters.
- Does not own identity, training planning, workout execution, reporting, or notification business flows beyond the shared events and contracts it consumes.

### Public contract surface

- Exposes diet-plan, meal, supplement-plan, and intake contracts that other modules can read or react to.
- Publishes nutrition data and nutrition events while keeping nutrition write rules internal.
- Diet-plan and supplementation authorization consume only Coaching's `ICoachingRelationshipAccessService`; Nutrition adapts its trainer and active-link decision to the existing feature-specific errors without importing Coaching persistence or private implementation types.
- Uses module-local stage-only Diet and Supplementation persistence ports. D3, D4, and D5 preserve the existing post-save `DietPlanUpdatedInAppNotificationCommand` through the Platform dispatcher and its canonical legacy command ID.

## Allowed dependencies

The #375 baseline shows the current clusters already depend on shared platform, identity, and infrastructure roots. #311 narrows that into a contract-first module policy.

| Module | May depend on | Must not depend on |
| --- | --- | --- |
| Platform / Reference Data | Its own shared bootstrap, configuration, reference data stores, and external runtime primitives | Any feature module's internal services, write models, or feature workflows |
| Identity & Accounts | Platform / Reference Data | Internal persistence or private services from Notifications, Reporting, Training Planning, Workout & Progress, or Coaching |
| Notifications | Platform / Reference Data; Identity & Accounts public contracts | Internal persistence or private services from Reporting, Training Planning, Workout & Progress, or Coaching |
| Reporting | Platform / Reference Data; Identity & Accounts public contracts; Coaching `ICoachingRelationshipAccessService`; Training Planning read contracts | Internal persistence or private services from Notifications, Training Planning, Workout & Progress, or Coaching |
| Training Planning | Platform / Reference Data; Identity & Accounts public contracts; its own `IPlanDayRelationshipAccessPort` | Internal persistence, private services, or public contracts from Notifications, Reporting, Workout & Progress, or Coaching |
| Workout & Progress | Platform / Reference Data; Identity & Accounts public contracts; Training Planning plan contracts; its own `IMeasurementsRelationshipAccessPort` | Internal persistence, private services, or public contracts from Notifications, Reporting, or Coaching |
| Coaching | Platform / Reference Data; Identity & Accounts public contracts; Training Planning; Workout & Progress; Notifications public contracts | Internal persistence or private services from Reporting or Nutrition |
| Nutrition | Platform / Reference Data; Identity & Accounts public contracts; Coaching `ICoachingRelationshipAccessService` | Internal persistence or private services from Notifications, Reporting, Training Planning, Workout & Progress, or Coaching |

## Platform / Reference Data hosting rules

Platform / Reference Data may host application startup, composition-root wiring, shared configuration, reference data, lookup data, and other cross-cutting primitives that multiple modules consume.

It must not absorb feature-specific commands, business workflows, or module-owned write models, because those belong to the fixed feature modules and stay under their own contract surfaces.

Provider and registration ownership remains module-specific: Notifications owns email registration and Dummy or SMTP selection and owns FCM registration; Reporting owns Local or Cloudflare R2 photo-storage registration; Identity owns Google-token validation registration; API logging owns the optional Elasticsearch sink. Provider SDKs, credentials, and raw provider responses remain outside Application and BuildingBlocks.

### Background-contract path rules

Platform owns `LgymApi.Application/Platform/Contracts/BackgroundCommands/` and `LgymApi.Application/Platform/Contracts/Serialization/`. Feature commands belong only under `LgymApi.Application/Identity/Contracts/BackgroundCommands/`, `LgymApi.Application/WorkoutProgress/Contracts/BackgroundCommands/`, `LgymApi.Application/Coaching/Contracts/BackgroundCommands/`, `LgymApi.Application/Reporting/Contracts/BackgroundCommands/`, and `LgymApi.Application/Nutrition/Contracts/BackgroundCommands/`. Notifications owns `LgymApi.Application/Notifications/Contracts/Push/`. Identity owns `LgymApi.Application/Features/PasswordReset/Contracts/`.

Worker runtime concerns belong under `LgymApi.BackgroundWorker/Runtime/` and Worker adapters, including `LgymApi.BackgroundWorker/Notifications/PasswordRecoveryEmailSchedulerAdapter.cs`. `LgymApi.BackgroundWorker.Common/Jobs/` and `LgymApi.BackgroundWorker.Common/Notifications/` remain the bounded persisted job and email wire seam. Common has no commands, serializers, push contracts, or Application-facing scheduling ports.

Application has no dependency on either Worker project or any `LgymApi.BackgroundWorker*` namespace. Its only relationship to legacy `LgymApi.BackgroundWorker.Common.Commands.*` names is persisted canonical command-ID data. The Worker resolves Application CLR names as read aliases but writes only those legacy IDs.

## Forbidden dependencies

No module may skip its published contract and reach into another module's private state or implementation.
No module may use direct access to another module's repositories or direct access to another module's entities instead of the published contract surface.
No module may create a catch-all boundary or a side channel that replaces the owning module's contract.

## Accepted progress communication

For #386, Reporting stages a `ReportSubmissionAcceptedProgressCommand` in the existing `CommandEnvelope` outbox before its submission commit. The committed intent is dispatched through the existing Worker `ActionMessage` infrastructure, and the Worker action invokes the Workout & Progress consumer. This is a published contract flow, not a direct repository write or direct consumer call. Invalid or poison deliveries are sanitized and bounded for retry or dead-letter handling, while unexpected persistence failures remain retryable.

## Cross-module communication rules

Cross-module communication must use one of these modes only: published contracts, read models, or in-process events that stay inside the application boundary.

- Published contracts are the default for request/response access between modules.
- Read models are allowed for non-owner reads when the target module exposes them as part of its public surface.
- In-process events are allowed only as module-local coordination or as published notifications between modules; they do not grant write access.

Any new interaction must name the source module, target module, and contract surface so later ownership mapping stays traceable to #375.
Direct persistence calls, shared repositories, and hidden side channels are not communication modes.

## Write ownership rules

Each entity or table has exactly one owner module.
Only that owner module may mutate the row set, enforce write validation, or change the write model.
Non-owner modules may read published views or call published contracts, but they may not mutate the underlying entity or table directly.
The solution still uses one production AppDbContext, and the production deployment still uses one database; those shared technical roots do not create shared write ownership.
Shared platform/reference data may be read by all modules, but only the owning module can change it.

## Persistence and identifier rules

The monolith has one production `AppDbContext`, one database, and one EF Core migration stream. Ownership is logical only: there is no physical database, context, schema, or migration-stream split. Those technical roots do not loosen the one-owner write rule. The executable ownership catalog is `LgymApi.ArchitectureTests/PersistedEntityOwnershipCatalog.cs`; its verified documentation view is in `issue-376-ownership-map.md`.

Internal known entity IDs use `Id<T>`. EF Core writes their provider values as PostgreSQL `uuid` columns. HTTP and JSON UUID values remain strings at the API boundary. The only polymorphic string ID exceptions are `PushNotificationMessage.EntityId` and `PushEventPayload.EntityId`.

Architecture-debt allowlists are no-growth. A recorded violation may only be re-keyed when its owner changes and its source and target identities stay exactly the same. New violations, wildcard exemptions, and re-keying to a different source or target are forbidden. Remove stale entries instead of preserving debt that is no longer observed.

Repositories stage writes. Application services own `IUnitOfWork.SaveChangesAsync()` and, for multi-step writes, the explicit transaction commit or rollback. The PostgreSQL transaction tests prove visibility only after commit and prove rollback leaves no persisted write after a forced failure.

## Extraction criteria

Extraction is justified only when all of these conditions are true:

- the module has a stable responsibility boundary that does not depend on private state from another module;
- the module's public-contract surface is complete enough for non-owners to use without repository access;
- the module's write ownership can stay exclusive to one owner module;
- the module no longer needs direct access to another module's repositories, entities, or private services;
- the dependency matrix can remain valid after extraction without adding a boundary-bypass path.

The eight-module catalog is the current baseline for those checks, and any later split must stay compatible with the #311 constraints.

## Executable topology and concern matrix

`ProjectReferenceGraphGuardTests` fixes the current graph at 14 projects and 32 edges. The only approved graph delta is removal of `LgymApi.Domain -> LgymApi.Resources`; Domain no longer depends on Resources. The system remains one production `AppDbContext`, one PostgreSQL database, and one migration stream.

`issue-393-platform-reference-data-boundary.md` is the executable concern-owner matrix for authentication, authorization, serialization, localization, logging, providers, pagination, clock, correlation, idempotency, enum lookup, AppConfig, and persistence topology. It supplements this map without adding an owner, project, provider, or persistence root.

## Links

- `docs/adr/006-lgym-evolves-as-modular-monolith.md`
- `docs/modular-monolith/issue-376-ownership-map.md`
- `docs/modular-monolith/issue-380-background-contract-ownership.md`
- `docs/modular-monolith/issue-380-project-reference-graph.md`
- `docs/modular-monolith/issue-381-notifications-boundary.md`
- `docs/modular-monolith/issue-390-nutrition-boundary.md`
- `docs/modular-monolith/issue-393-platform-reference-data-boundary.md`
