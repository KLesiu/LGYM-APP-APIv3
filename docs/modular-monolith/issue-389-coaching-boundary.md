# Issue #389: Coaching Boundary

## Status

This document records the implemented, compatibility-preserving Coaching boundary after issue #389. It names logical ownership and public contracts without claiming a physical split.

## Source precedence

1. `#311` and ADR-006 define modular-monolith constraints.
2. `docs/ARCHITECTURE.md` defines the current layered runtime, unit-of-work rules, and shared persistence topology.
3. `docs/modular-monolith/issue-376-module-context-map.md` defines the module catalog and dependency direction.
4. `docs/modular-monolith/issue-376-ownership-map.md` and `LgymApi.ArchitectureTests/PersistedEntityOwnershipCatalog.cs` define persisted ownership.
5. `docs/modular-monolith/issue-380-background-contract-ownership.md` defines Application, Worker, and Common background-contract ownership.
6. `docs/modular-monolith/issue-381-notifications-boundary.md` defines Notifications policy and provider-neutral contract ownership.

When this document is more specific, it clarifies the Coaching boundary without overriding those sources.

## Scope and non-goals

Coaching owns trainer-trainee relationship facts, invitations, trainer dashboard facts, trainee notes, and their histories. It coordinates authorized use of Identity, Training Planning, Workout & Progress, Reporting, Nutrition, and Notifications through public contracts or compatibility adapters; it does not own their entities, repositories, policies, or writes.

This cutover preserved routes, DTOs, JSON, validation, idempotency, provider behavior, Common wire types, command identities, schema, tables, indexes, foreign keys, query filters, projects, deployment, brokers, and schedulers. It removed obsolete Coaching repository and service seams without adding a physical split.

The production system remains one deployable application with one `AppDbContext`, one PostgreSQL database, one schema model, and one migration stream. Logical write ownership does not create a physical schema, database, context, migration, service, deployment, or project split.

## Persisted ownership

The executable persisted-entity catalog remains authoritative. These stable rows are its Coaching view; every entity appears once and stays physically where it is until separately approved work changes that location.

| Owner ID | Entity name | Owner | Write boundary |
| --- | --- | --- | --- |
| `coaching.owner.trainer-invitation` | `TrainerInvitation` | `Coaching` | Owns invitation creation, pending-state transitions, response state, and invitation-specific compatibility facts. |
| `coaching.owner.trainer-trainee-link` | `TrainerTraineeLink` | `Coaching` | Owns active trainer-trainee relationship facts and lifecycle transitions. |
| `coaching.owner.trainee-note` | `TraineeNote` | `Coaching` | Owns trainer-authored note lifecycle, visibility, pinning, and soft-delete facts. |
| `coaching.owner.trainee-note-history` | `TraineeNoteHistory` | `Coaching` | Owns immutable note-change history associated with Coaching note transitions. |

## Capability map

The map contains exactly 31 Application capabilities: 30 existing HTTP-backed actions and one application-only action. The capability labels below do not prescribe, rename, or invent routes. `GetTrainerInvitationsAsync` remains the only unexposed action.

| Action ID | Application action | Surface | Current adapter family |
| --- | --- | --- | --- |
| `coaching.action.create-trainer-invitation` | Create trainer invitation | HTTP | Trainer invitation adapter |
| `coaching.action.create-trainer-invitation-by-email` | Create trainer invitation by email | HTTP | Trainer invitation adapter |
| `coaching.action.get-trainer-invitations` | `GetTrainerInvitationsAsync` | Application only | No HTTP adapter |
| `coaching.action.get-trainer-invitations-paginated` | Get paginated trainer invitations | HTTP | Trainer invitation adapter |
| `coaching.action.get-public-invitation-status` | Get public invitation status | HTTP | Public invitation-status adapter |
| `coaching.action.accept-trainer-invitation` | Accept trainer invitation | HTTP | Trainee relationship adapter |
| `coaching.action.reject-trainer-invitation` | Reject trainer invitation | HTTP | Trainee relationship adapter |
| `coaching.action.revoke-trainer-invitation` | Revoke trainer invitation | HTTP | Trainer invitation adapter |
| `coaching.action.unlink-trainee` | Unlink trainee | HTTP | Trainer relationship adapter |
| `coaching.action.detach-from-trainer` | Detach from trainer | HTTP | Trainee relationship adapter |
| `coaching.action.get-current-trainer` | Get current trainer | HTTP | Trainee relationship adapter |
| `coaching.action.get-trainer-dashboard` | Get trainer dashboard | HTTP | Trainer dashboard/progress adapter |
| `coaching.action.get-training-dates` | Get trainee training dates | HTTP | Trainer dashboard/progress adapter |
| `coaching.action.get-training-by-date` | Get trainee training by date | HTTP | Trainer dashboard/progress adapter |
| `coaching.action.get-exercise-scores-chart` | Get exercise scores chart | HTTP | Trainer dashboard/progress adapter |
| `coaching.action.get-elo-chart` | Get ELO chart | HTTP | Trainer dashboard/progress adapter |
| `coaching.action.get-main-records-history` | Get main-records history | HTTP | Trainer dashboard/progress adapter |
| `coaching.action.list-managed-plans` | List managed plans | HTTP | Trainer managed-plan adapter |
| `coaching.action.create-managed-plan` | Create managed plan | HTTP | Trainer managed-plan adapter |
| `coaching.action.update-managed-plan` | Update managed plan | HTTP | Trainer managed-plan adapter |
| `coaching.action.delete-managed-plan` | Delete managed plan | HTTP | Trainer managed-plan adapter |
| `coaching.action.assign-managed-plan` | Assign managed plan | HTTP | Trainer managed-plan adapter |
| `coaching.action.unassign-managed-plan` | Unassign managed plan | HTTP | Trainer managed-plan adapter |
| `coaching.action.get-active-managed-plan` | Get active managed plan | HTTP | Trainer managed-plan adapter |
| `coaching.action.list-trainer-notes` | List trainer notes | HTTP | Trainer trainee-note adapter |
| `coaching.action.create-trainee-note` | Create trainee note | HTTP | Trainer trainee-note adapter |
| `coaching.action.update-trainee-note` | Update trainee note | HTTP | Trainer trainee-note adapter |
| `coaching.action.delete-trainee-note` | Delete trainee note | HTTP | Trainer trainee-note adapter |
| `coaching.action.get-trainee-note-history` | Get trainee-note history | HTTP | Trainer trainee-note adapter |
| `coaching.action.list-visible-trainee-notes` | List visible trainee notes | HTTP | Trainee-note adapter |
| `coaching.action.get-visible-trainee-note` | Get visible trainee note | HTTP | Trainee-note adapter |

## Public contracts

The following surfaces are implemented. Contract data is ID-only or immutable fact/read data; it does not expose foreign entities, repositories, EF types, or mutable values.

| Contract ID | Target public surface | Allowed data | Status |
| --- | --- | --- | --- |
| `coaching.contract.relationship-access` | `ICoachingRelationshipAccessService` | Trainer and trainee `Id<T>` values and an immutable relationship decision | Implemented |
| `coaching.contract.invitation-facts` | Coaching invitation read surface | Invitation IDs, relationship IDs, status, expiry, and approved immutable invitation facts | Implemented |
| `coaching.contract.notification-facts` | Coaching notification fact-read surface | Stable event facts, identity references, relationship references, and approved metadata | Implemented |
| `coaching.contract.training-planning-authorization` | `IPlanDayRelationshipAccessPort`, owned by Training Planning | ID-only relationship authorization request and boolean decision | Implemented by Coaching adapter |
| `coaching.contract.workout-progress-authorization` | `IMeasurementsRelationshipAccessPort`, owned by Workout & Progress | ID-only relationship authorization request and boolean decision | Implemented by Coaching adapter |

## Dependency DAG

The directed graph is acyclic. Reporting and Nutrition use the single Coaching public access surface; Training Planning and Workout & Progress define consumer-owned authorization ports implemented by Coaching adapters, so they do not compile against Coaching. No foreign module receives Coaching persistence access.

| Dependency ID | Allowed target edge | Direction | Policy status |
| --- | --- | --- | --- |
| `coaching.dependency.api-to-coaching` | API adapter to focused Coaching use case/read model | API to Coaching | Implemented |
| `coaching.dependency.worker-to-coaching` | Worker compatibility adapter to Coaching fact-read and Notifications intent contracts | Worker to public contracts | Implemented |
| `coaching.dependency.reporting-to-coaching` | Reporting to `ICoachingRelationshipAccessService` only | Reporting to Coaching public contract | Implemented |
| `coaching.dependency.nutrition-to-coaching` | Nutrition to `ICoachingRelationshipAccessService` only | Nutrition to Coaching public contract | Implemented |
| `coaching.dependency.coaching-to-identity` | Coaching to Identity account and authorization fact reads | Coaching to Identity public contract | Implemented |
| `coaching.dependency.coaching-to-training-planning` | Coaching to Training Planning managed-plan contracts | Coaching to Training Planning public contract | Implemented |
| `coaching.dependency.coaching-to-workout-progress` | Coaching to Workout & Progress dashboard reads | Coaching to Workout & Progress public contract | Implemented |
| `coaching.dependency.training-planning-authorization-port` | Training Planning-owned port implemented by a Coaching adapter | Coaching implements consumer port | Implemented |
| `coaching.dependency.workout-progress-authorization-port` | Workout & Progress-owned port implemented by a Coaching adapter | Coaching implements consumer port | Implemented |

## Persistence topology

| Persistence ID | AppDbContext count | Database count | Migration stream count | Physical split |
| --- | --- | --- | --- | --- |
| `coaching.persistence.shared-topology` | `1` | `1` | `1` | `None` |

Coaching persistence is isolated behind focused stage-only ports, and Coaching application services own authorization, transaction boundaries, and `IUnitOfWork.SaveChangesAsync()` at use-case boundaries. The cutover retains the existing tables, mappings, configuration registrar, migrations, foreign keys, indexes, query filters, and typed-ID conversions.

## Invitation, link, and note lifecycle

Coaching owns invitation state and active-link facts. Focused invitation slices preserve the current create, email-create, list, paginated list, public status, accept, reject, and revoke semantics, including pending and expiry behavior, existing command timing, and current error mapping. Accept and reject validate bound-ID or normalized-email invitee ownership before mutation; an expired pending email invitation stages only `Expired` and `RespondedAt`, remains unbound, creates no active link, and queues no command. The focused controllers invoke these slices; `GetTrainerInvitationsAsync` remains the application-only action.

Coaching owns link creation, unlink, trainee detach, and current-trainer facts. Authorization and account/profile enrichment belong behind the documented contract boundary, not through foreign entity or repository values. Dashboard and invitation identity enrichment must happen before filtering, sorting, counting, and paging so the characterized current result membership and ordering remain compatible.

Coaching owns note visibility, pinning, soft deletion, and history facts. Note mutations persist the note and its history atomically at the Coaching use-case boundary. No row here authorizes changing note payloads, history semantics, notification timing, or malformed-ID API compatibility.

## Owner-oriented API adapters and legacy command compatibility

The five controller-facing Coaching API ports, their adapters, and their mapping profile reside under `LgymApi.Application/Coaching/ApiAdapters`. The following adapter rows preserve the current API and durable command surfaces. They are endpoint compatibility boundaries, not new routes or persisted identities.

| Adapter ID | Current compatibility surface | Boundary rule |
| --- | --- | --- |
| `coaching.adapter.api-invitations` | Existing trainer invitation HTTP actions | Implemented focused adapters preserve routes, verbs, authorization, idempotency, DTOs, JSON fields, status codes, localization, and errors. |
| `coaching.adapter.api-dashboard-progress` | Existing trainer dashboard and progress HTTP actions | Implemented focused adapters preserve request and response behavior without direct Workout & Progress persistence access. |
| `coaching.adapter.api-managed-plans` | Existing trainer managed-plan HTTP actions | Implemented focused adapters preserve current behavior while Training Planning retains plan writes. |
| `coaching.adapter.api-relationships` | Existing trainer and trainee relationship HTTP actions | Implemented focused adapters preserve relationship action semantics and response shapes. |
| `coaching.adapter.api-trainee-notes` | Existing trainer and trainee note HTTP actions | Implemented focused adapters preserve note and history behavior, including malformed-ID behavior. |
| `coaching.adapter.api-public-invitation-status` | Existing anonymous public invitation-status HTTP action | The implemented focused adapter preserves anonymous status and `userExists` behavior; it adds no route. |

| Adapter ID | Legacy command | Notifications intent | Eligible legacy channel | Compatibility rule |
| --- | --- | --- | --- | --- |
| `coaching.adapter.legacy-command.invitation-created-email` | `InvitationCreatedCommand` with canonical `LgymApi.BackgroundWorker.Common.Commands.InvitationCreatedCommand` ID | `coaching.intent.invitation-created` | Email | Preserve CLR shape, canonical persisted ID, Application read alias, JSON bytes, existing Worker handler identity, and email-only behavior. |
| `coaching.adapter.legacy-command.invitation-created-in-app` | `TrainerInvitationCreatedInAppNotificationCommand` with canonical `LgymApi.BackgroundWorker.Common.Commands.TrainerInvitationCreatedInAppNotificationCommand` ID | `coaching.intent.invitation-created` | In-app | Preserve CLR shape, canonical persisted ID, Application read alias, JSON bytes, existing Worker handler identity, and in-app-only behavior. |
| `coaching.adapter.legacy-command.invitation-accepted-email` | `InvitationAcceptedCommand` with canonical `LgymApi.BackgroundWorker.Common.Commands.InvitationAcceptedCommand` ID | `coaching.intent.invitation-accepted` | Email | Preserve CLR shape, canonical persisted ID, Application read alias, JSON bytes, existing Worker handler identity, and email-only behavior. |
| `coaching.adapter.legacy-command.invitation-accepted-in-app` | `TrainerInvitationAcceptedInAppNotificationCommand` with canonical `LgymApi.BackgroundWorker.Common.Commands.TrainerInvitationAcceptedInAppNotificationCommand` ID | `coaching.intent.invitation-accepted` | In-app | Preserve CLR shape, canonical persisted ID, Application read alias, JSON bytes, existing Worker handler identity, and in-app-only behavior. |
| `coaching.adapter.legacy-command.invitation-revoked-email` | `InvitationRevokedCommand` with canonical `LgymApi.BackgroundWorker.Common.Commands.InvitationRevokedCommand` ID | `coaching.intent.invitation-revoked` | Email | Preserve CLR shape, canonical persisted ID, Application read alias, JSON bytes, existing Worker handler identity, and email-only behavior. |
| `coaching.adapter.legacy-command.invitation-rejected-in-app` | `TrainerInvitationRejectedInAppNotificationCommand` with canonical `LgymApi.BackgroundWorker.Common.Commands.TrainerInvitationRejectedInAppNotificationCommand` ID | `coaching.intent.invitation-rejected` | In-app | Preserve CLR shape, canonical persisted ID, Application read alias, JSON bytes, existing Worker handler identity, and in-app-only behavior. |
| `coaching.adapter.legacy-command.relationship-ended-in-app` | `TrainerRelationshipEndedInAppNotificationCommand` with canonical `LgymApi.BackgroundWorker.Common.Commands.TrainerRelationshipEndedInAppNotificationCommand` ID | `coaching.intent.relationship-ended` | In-app | Preserve CLR shape, canonical persisted ID, Application read alias, JSON bytes, and existing Worker handler identity. |
| `coaching.adapter.legacy-command.trainee-note-updated-in-app` | `TraineeNoteUpdatedInAppNotificationCommand` with canonical `LgymApi.BackgroundWorker.Common.Commands.TraineeNoteUpdatedInAppNotificationCommand` ID | `coaching.intent.trainee-note-updated` | In-app | Preserve CLR shape, canonical persisted ID, Application read alias, JSON bytes, and existing Worker handler identity. |

Canonical legacy IDs remain write IDs; Application CLR names remain read aliases only. Worker remains the compatibility runtime and execution owner. Notifications owns fact-to-message rendering, culture, channel, idempotency, persistence, fanout, and provider-neutral scheduling policy. This cutover does not change Worker, Common, Hangfire, email, push, or provider behavior.

## Notifications intents

The six intent rows consolidate the eight legacy compatibility adapters without changing their eligible channels. Created and accepted each retain distinct email and in-app adapter entries; they do not fan out into both channels.

| Intent ID | Target Notifications-owned intent | Source adapters | Policy status |
| --- | --- | --- | --- |
| `coaching.intent.invitation-created` | Invitation created | Created email and in-app adapters | Implemented, one selected channel per adapter |
| `coaching.intent.invitation-accepted` | Invitation accepted | Accepted email and in-app adapters | Implemented, one selected channel per adapter |
| `coaching.intent.invitation-rejected` | Invitation rejected | Rejected in-app adapter | Implemented, in-app only |
| `coaching.intent.invitation-revoked` | Invitation revoked | Revoked email adapter | Implemented, email only |
| `coaching.intent.relationship-ended` | Trainer relationship ended | Relationship-ended in-app adapter | Implemented, in-app only |
| `coaching.intent.trainee-note-updated` | Trainee note updated | Trainee-note-updated in-app adapter | Implemented, in-app only |

## Cutover result

The focused Coaching cutover removes obsolete Coaching repository and service seams. The boundary-debt baseline contains 157 active entries after stale Coaching-related rows were removed. Existing guards reject reintroduced persistence, private-service, and entity edges while allowing typed `Id<T>` transport.

Coaching is one of the Application-owned API adapter owners in the final 25-adapter Application handoff. Its five controller-facing ports remain owner-oriented, while migration-era adapter CLR names are removed under ADR-007 without changing the listed HTTP contracts.

All 31 actions now use focused Coaching ownership. Invitation and dashboard identity enrichment occurs before filtering, sorting, totals, and paging. The expired pending email binding path records only `Expired` and `RespondedAt`, keeps `TraineeId` null, creates no link, and queues no command. No schema, model, migration, table, index, foreign-key, query-filter, typed-ID conversion, `AppDbContext`, database, or migration-stream change occurred.

## Guard coverage

Architecture tests parse these stable rows rather than matching prose. The rows represent the completed cutover and continue to guard its durable contracts.

| Guard ID | Asserted invariant | Evidence surface |
| --- | --- | --- |
| `coaching.guard.persisted-ownership` | The four Coaching entities appear exactly once and use the compiled catalog owner. | `PersistedEntityOwnershipCatalog.cs` and the persisted-ownership table. |
| `coaching.guard.action-ledger` | The capability map has 31 rows: 30 HTTP actions and only `GetTrainerInvitationsAsync` as application-only. | The capability-map table. |
| `coaching.guard.public-contracts` | Public contracts are explicit, ID-only or immutable, and implemented. | The public-contract table. |
| `coaching.guard.dependency-dag` | Implementation uses only the documented acyclic public-contract and consumer-port edges. | The dependency DAG table. |
| `coaching.guard.persistence-topology` | Coaching retains one `AppDbContext`, one database, one migration stream, and no physical split. | The persistence-topology table. |
| `coaching.guard.compatibility-adapters` | API adapters, eight legacy commands, six intents, canonical IDs, and channel isolation remain documented. | Compatibility-adapter and Notifications-intent tables. |
| `coaching.guard.scope` | The cutover preserves routes, schema, and shared physical topology. | Scope, non-goals, and cutover result. |
