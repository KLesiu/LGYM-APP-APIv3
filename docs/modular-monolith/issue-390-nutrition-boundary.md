# Issue #390: Nutrition Boundary

## Status

This document records the implemented Nutrition capability boundary. It assigns logical write ownership and preserves the existing API, command, Worker, and persistence behavior. It does not create a physical split.

## Source precedence

1. `#311` and ADR-006 define the modular-monolith constraints.
2. `docs/ARCHITECTURE.md` defines the layered runtime, unit-of-work rules, and shared persistence topology.
3. `docs/modular-monolith/issue-376-module-context-map.md` defines the module catalog and allowed dependency direction.
4. `docs/modular-monolith/issue-376-ownership-map.md` and `LgymApi.ArchitectureTests/PersistedEntityOwnershipCatalog.cs` define persisted ownership.
5. `docs/modular-monolith/issue-380-background-contract-ownership.md` defines background command ownership and canonical command compatibility.

When this document is more specific, it clarifies the Nutrition boundary without overriding those sources.

## Scope and non-goals

Nutrition owns Diet Plans and Supplementation rules, their six persisted entities, their focused use cases, module-local persistence ports, and the four existing HTTP controller adapters. It consumes only the published Coaching relationship-access contract for trainer and active-link facts. It doesn't own Coaching relationships, Identity accounts, notification delivery, Worker runtime, or foreign persistence.

The cutover preserves all existing routes, verbs, policies, DTOs, `_id` and `msg` JSON fields, localized errors, command identity and shape, Worker handlers, schema, tables, indexes, foreign keys, query filters, typed-ID conversions, projects, deployment, and schedulers. The production system remains one deployable application with one `AppDbContext`, one PostgreSQL database, and one migration stream.

## Persisted ownership

The executable persisted-entity catalog is authoritative. These stable rows are its Nutrition view. Each entity remains in the shared physical topology and has exactly one logical write owner.

| Owner ID | Entity name | Owner | Write boundary |
| --- | --- | --- | --- |
| `nutrition.owner.diet-plan` | `DietPlan` | `Nutrition` | Owns Diet Plan lifecycle, active state, ordering, and trainer-trainee authorization after Coaching facts are adapted. |
| `nutrition.owner.diet-meal` | `DietMeal` | `Nutrition` | Owns meal normalization, replacement, ordering, and membership in a Diet Plan. |
| `nutrition.owner.diet-plan-history` | `DietPlanHistory` | `Nutrition` | Owns pre-save Diet lifecycle snapshots and their immutable history entries. |
| `nutrition.owner.supplement-plan` | `SupplementPlan` | `Nutrition` | Owns Supplement Plan creation, replacement, activation, assignment, unassignment, and soft deletion. |
| `nutrition.owner.supplement-plan-item` | `SupplementPlanItem` | `Nutrition` | Owns Supplement Plan item lifecycle, ordering, schedule facts, and plan membership. |
| `nutrition.owner.supplement-intake-log` | `SupplementIntakeLog` | `Nutrition` | Owns intake check-off state, timestamps, and the existing uniqueness-recovery lifecycle. |

## Capability map

The map contains exactly 18 focused Application actions. D1 through D9 cover Diet Plans and S1 through S9 cover Supplementation. The labels identify existing behavior and do not add routes.

| Action ID | Application action | Surface | Current adapter family |
| --- | --- | --- | --- |
| `nutrition.action.d1.list-trainee-diet-plans` | `IGetTraineeDietPlansUseCase.ExecuteAsync` | HTTP | Trainer Diet Plans controller |
| `nutrition.action.d2.get-trainee-diet-plan` | `IGetTraineeDietPlanUseCase.ExecuteAsync` | HTTP | Trainer Diet Plans controller |
| `nutrition.action.d3.create-trainee-diet-plan` | `ICreateTraineeDietPlanUseCase.ExecuteAsync` | HTTP | Trainer Diet Plans controller |
| `nutrition.action.d4.update-trainee-diet-plan` | `IUpdateTraineeDietPlanUseCase.ExecuteAsync` | HTTP | Trainer Diet Plans controller |
| `nutrition.action.d5.activate-trainee-diet-plan` | `IActivateTraineeDietPlanUseCase.ExecuteAsync` | HTTP | Trainer Diet Plans controller |
| `nutrition.action.d6.delete-trainee-diet-plan` | `IDeleteTraineeDietPlanUseCase.ExecuteAsync` | HTTP | Trainer Diet Plans controller |
| `nutrition.action.d7.get-trainee-diet-plan-history` | `IGetTraineeDietPlanHistoryUseCase.ExecuteAsync` | HTTP | Trainer Diet Plans controller |
| `nutrition.action.d8.list-current-diet-plans` | `IGetCurrentDietPlansUseCase.ExecuteAsync` | HTTP | Trainee Diet Plan controller |
| `nutrition.action.d9.get-current-diet-plan` | `IGetCurrentDietPlanUseCase.ExecuteAsync` | HTTP | Trainee Diet Plan controller |
| `nutrition.action.s1.list-trainee-supplement-plans` | `IGetTraineeSupplementPlansUseCase.ExecuteAsync` | HTTP | Trainer Supplementation controller |
| `nutrition.action.s2.create-trainee-supplement-plan` | `ICreateTraineeSupplementPlanUseCase.ExecuteAsync` | HTTP | Trainer Supplementation controller |
| `nutrition.action.s3.update-trainee-supplement-plan` | `IUpdateTraineeSupplementPlanUseCase.ExecuteAsync` | HTTP | Trainer Supplementation controller |
| `nutrition.action.s4.delete-trainee-supplement-plan` | `IDeleteTraineeSupplementPlanUseCase.ExecuteAsync` | HTTP | Trainer Supplementation controller |
| `nutrition.action.s5.assign-trainee-supplement-plan` | `IAssignTraineeSupplementPlanUseCase.ExecuteAsync` | HTTP | Trainer Supplementation controller |
| `nutrition.action.s6.unassign-trainee-supplement-plan` | `IUnassignTraineeSupplementPlanUseCase.ExecuteAsync` | HTTP | Trainer Supplementation controller |
| `nutrition.action.s7.get-supplement-compliance-summary` | `IGetSupplementComplianceSummaryUseCase.ExecuteAsync` | HTTP | Trainer Supplementation controller |
| `nutrition.action.s8.get-supplement-schedule` | `IGetSupplementScheduleUseCase.ExecuteAsync` | HTTP | Trainee Supplementation controller |
| `nutrition.action.s9.check-off-supplement-intake` | `ICheckOffSupplementIntakeUseCase.ExecuteAsync` | HTTP | Trainee Supplementation controller |

## Public contracts and dependencies

Public inputs and read models are sealed immutable Nutrition records with typed internal IDs. They don't expose Domain entities, repositories, EF types, API DTOs, Worker types, or mutable foreign models.

| Contract ID | Target public surface | Allowed data | Status |
| --- | --- | --- | --- |
| `nutrition.contract.diet-use-cases` | D1 through D9 one-method contracts | Typed IDs and immutable Diet input and read records | Implemented |
| `nutrition.contract.supplement-use-cases` | S1 through S9 one-method contracts | Typed IDs and immutable Supplementation input and read records | Implemented |
| `nutrition.contract.coaching-access` | `ICoachingRelationshipAccessService` | Trainer and trainee IDs plus immutable trainer and active-link facts | Implemented consumer contract |
| `nutrition.contract.persistence-ports` | `IDietPlanPersistence` and `ISupplementationPersistence` | Module-local stage-only mutation and no-tracking read operations | Implemented |
| `nutrition.contract.background-command` | `DietPlanUpdatedInAppNotificationCommand` | Existing typed command payload through the Platform dispatcher | Implemented compatibility contract |

| Dependency ID | Allowed target edge | Direction | Policy status |
| --- | --- | --- | --- |
| `nutrition.dependency.api-to-nutrition` | Existing API controllers to focused Nutrition use-case contracts and mapping profile | API to Nutrition | Implemented |
| `nutrition.dependency.nutrition-to-coaching` | `ICoachingRelationshipAccessService` only | Nutrition to Coaching public contract | Implemented |
| `nutrition.dependency.nutrition-to-platform` | `IUnitOfWork`, `IMapper`, and `ICommandDispatcher` public contracts | Nutrition to Platform | Implemented |
| `nutrition.dependency.infrastructure-to-nutrition` | Module-local persistence adapter implementations | Infrastructure implements Nutrition ports | Implemented |

No Nutrition path receives a Coaching entity, repository, persistence port, or private implementation. Diet and Supplementation adapt the published trainer and active-link facts to their established feature-specific errors.

## Persistence topology and lifecycle

| Persistence ID | AppDbContext count | Database count | Migration stream count | Physical split |
| --- | --- | --- | --- | --- |
| `nutrition.persistence.shared-topology` | `1` | `1` | `1` | `None` |

Nutrition persistence adapters remain stage-only. Mutation loads are tracked, reads are no-tracking, and Nutrition use cases own authorization, `IUnitOfWork.SaveChangesAsync()`, and any commit timing. No Nutrition repository saves or starts a transaction.

Diet preserves macro-only plans, caller-visible meal ordering, multiple active plans, active-plan list and single-read ordering, and `Created`, `Updated`, `Activated`, and `Deleted` pre-save history snapshots. D3, D4, and D5 enqueue the unchanged Diet command only after a successful save when the resulting plan is active.

Supplementation preserves inactive creation, replacement with new plan and item IDs while retaining active state, single-active assignment, unassign no-op behavior, schedule day-mask and ordering rules, inclusive compliance rounding, and intake uniqueness-race winner reload behavior.

## Compatibility adapters

The four adapter rows describe existing controllers. They preserve compatibility and don't create controller splits or new routes.

| Adapter ID | Current compatibility surface | Boundary rule |
| --- | --- | --- |
| `nutrition.adapter.api.trainer-diet-plans` | `TrainerDietPlansController` D1 through D7 actions | Preserves existing trainer routes, policies, malformed-ID branches, DTOs, `_id` and `msg` fields, localized errors, and status codes. |
| `nutrition.adapter.api.trainee-diet-plans` | `TraineeDietPlanController` D8 through D9 actions | Preserves trainee current-list and current-single routes, including empty-list and not-found behavior. |
| `nutrition.adapter.api.trainer-supplementation` | `TrainerSupplementationController` S1 through S7 actions | Preserves trainer routes, body mapping, malformed-ID behavior, missing-date validation, DTOs, localization, and response status. |
| `nutrition.adapter.api.trainee-supplementation` | `TraineeSupplementationController` S8 through S9 actions | Preserves UTC default schedule-date behavior, typed item parsing, check-off semantics, and response shape. |

| Adapter ID | Legacy command | Producer actions | Compatibility rule |
| --- | --- | --- | --- |
| `nutrition.adapter.legacy-command.diet-plan-updated` | `DietPlanUpdatedInAppNotificationCommand` with canonical `LgymApi.BackgroundWorker.Common.Commands.DietPlanUpdatedInAppNotificationCommand` ID | D3, D4, D5 | Preserve the command CLR shape, canonical persisted ID, JSON payload bytes, Worker handler identity, and post-save enqueue timing. Application CLR names remain read aliases and writes retain the canonical legacy ID. |

## Cutover result

All 18 focused actions are implemented and all four existing controllers route to them. The cutover removed obsolete Nutrition facades without changing Nutrition's six persisted entities, the total 48-entity ownership catalog, the single `AppDbContext`, the database, or the migration stream. It creates no physical database, schema, context, migration, project, deployment, or service split.

## Guard coverage

Architecture tests parse these stable rows instead of matching prose.

| Guard ID | Asserted invariant | Evidence surface |
| --- | --- | --- |
| `nutrition.guard.persisted-ownership` | The six Nutrition entities appear exactly once and use the compiled catalog owner. | `PersistedEntityOwnershipCatalog.cs` and the persisted-ownership table. |
| `nutrition.guard.action-ledger` | The capability map contains exactly D1 through D9 and S1 through S9, for 18 HTTP actions. | The capability-map table. |
| `nutrition.guard.public-contracts` | Focused contracts are immutable, typed, module-owned, and free of forbidden foreign implementation types. | Public-contract and dependency tables. |
| `nutrition.guard.persistence-topology` | Nutrition retains one `AppDbContext`, one database, one migration stream, and no physical split. | The persistence-topology table. |
| `nutrition.guard.compatibility-adapters` | Exactly four existing API adapters and the unchanged Diet command adapter remain documented. | Compatibility-adapter tables. |
| `nutrition.guard.lifecycle` | Diet history and post-save command timing, plus Supplementation lifecycle invariants, remain durable. | Lifecycle section and focused use-case tests. |
| `nutrition.guard.scope` | The cutover preserves shared topology, ownership totals, routes, schema, Worker behavior, and command compatibility. | Scope and cutover-result sections. |
