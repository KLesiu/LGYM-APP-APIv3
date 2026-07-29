# Issue #381: Notifications Boundary

## Status

Current boundary definition for the extracted Notifications module. This document records responsibility, write ownership, provider privacy, and compatibility constraints for the current modular monolith.

## Source precedence

The boundary is read with the following precedence:

1. `#311` and ADR-006 define the modular-monolith constraints.
2. `docs/ARCHITECTURE.md` defines the current layered runtime and unit-of-work rules.
3. `docs/modular-monolith/issue-376-module-context-map.md` defines the module catalog and dependency matrix.
4. `docs/modular-monolith/issue-376-ownership-map.md` and `LgymApi.ArchitectureTests/PersistedEntityOwnershipCatalog.cs` define persisted ownership.
5. `docs/modular-monolith/issue-380-background-contract-ownership.md` defines Application, Worker, and Common background contract ownership.

When this document is more specific, it clarifies the Notifications boundary without overriding those sources.

## Scope and non-goals

Notifications owns notification decisions and writes, including in-app persistence, delivery intent, channel policy, push installation lifecycle, delivery status, and notification maintenance. Source modules own the business facts that cause notification requests.

The original #381 boundary did not authorize physical moves. #387 later approved the focused move of Notifications implementation, persistence, provider, and template sources into `LgymApi.Notifications`. It still does not authorize replacing Hangfire, adding a new `DbContext`, adding a migration stream, creating a schema per module, introducing microservices, or changing endpoints, payloads, job identities, schedulers, or persistence roots.

The current system remains one deployable application with one production database, one `AppDbContext`, and one migration stream. Existing physical layers remain in place.

## Ownership semantics

Module ownership means write and responsibility ownership. Notifications defines notification write rules, validates notification intents, coordinates delivery, and compiles its five stage-only repository and mapping implementations behind the internal persistence seam. The private FCM provider implementation and its configuration compile from `LgymApi.Notifications` within the guarded 18-project, 90-edge graph.

The notification entities remain under `LgymApi.Domain/Entities`. Their physical location does not change their module owner, and a shared `AppDbContext` does not create shared write authority. Non-owner modules use published contracts, read models, or in-process events and do not mutate notification rows directly.

Notifications owns provider-neutral notification ports and models, five stage-only persistence implementations/mappings behind `INotificationsPersistenceContext`, its email template stack, and its private FCM provider adapter/configuration. Infrastructure retains the one `AppDbContext`, scoped seam alias, global registrar phase coordinator, and migration stream. Worker owns runtime handlers, scheduler selection, and job execution. Common keeps only its existing bounded persisted-job and email wire seam.

## Notifications-owned artifacts

The following catalog is the stable ownership surface for #381. The `Artifact ID` values are documentation identifiers for architecture checks and are not runtime type names.

| Artifact ID | Artifact name | Owner | Responsibility and allowed access |
| --- | --- | --- | --- |
| `notifications.artifact.in-app-notification` | `InAppNotification` | Notifications | Owns in-app notification rows, read state, delivery keys, and fan-out coordination. Other modules use notification contracts or published views. |
| `notifications.artifact.notification-message` | `NotificationMessage` | Notifications | Owns durable notification intent and message metadata. Other modules submit provider-neutral intent data and do not write the entity. |
| `notifications.artifact.email-subscription` | `EmailNotificationSubscription` | Notifications | Owns durable email subscription and preference state used by notification policy. Other modules request behavior through contracts. |
| `notifications.artifact.push-installation` | `PushInstallation` | Notifications | Owns installation registration, refresh, disablement, and stale-installation lifecycle. Raw installation tokens are private to the notification implementation. |
| `notifications.artifact.push-message` | `PushNotificationMessage` | Notifications | Owns push delivery records, status, deduplication, and retry claims. Other modules may receive provider-neutral status views. |
| `notifications.artifact.delivery-status-retry-policy` | Notification delivery status/retry policy | Notifications | `PushNotificationDeliveryService` claims durable work, applies provider outcomes, persists state and bounded post-claim recovery, decides retry eligibility, and owns UoW commits. |
| `notifications.artifact.delivery-jobs-cleanup` | Notification delivery jobs and cleanup jobs | Notifications | Owns the intent and sequencing of delivery and stale-data cleanup work. Worker provides ID-only job execution and scheduler adapters. |
| `notifications.artifact.provider-adapters` | Email/push provider adapters | Notifications | Notifications owns the provider boundary, private FCM transport/configuration, and provider-private mapping without exposing them to other modules. |
| `notifications.artifact.event-bridge` | Notification event bridge | Notifications | Owns translation from published business events or notification intents into notification workflows. Producers retain ownership of their business events. |

## Public contract surface

The public surface is provider-neutral. Contracts describe notification intent, logical event identity, recipient identity, channel preference, culture or time-zone data when required, deeplinks, and delivery outcomes. Contracts must be sufficient for another module to request or observe notification behavior without referencing notification persistence or runtime implementation types.

| Contract ID | Provider-neutral purpose | Permitted data | Explicit exclusion |
| --- | --- | --- | --- |
| `notifications.contract.event-intent` | Submit a logical notification event or intent | Stable event ID, source module, entity ID, type key, recipient ID, and approved display metadata | No persisted entity, repository, provider credential, raw installation token, or job ID |
| `notifications.contract.channel-preference` | Express channel eligibility and recipient preference | Channel-neutral preference and subscription state request | No provider selection command or provider configuration |
| `notifications.contract.delivery-outcome` | Report notification lifecycle state | Stable notification ID, status, failure category, and retry eligibility | No external provider response object or provider secret |
| `notifications.contract.push-payload` | Carry the stable push payload shape | Schema version, type, event ID, entity ID, in-app notification ID, deeplink, and approved metadata | No provider-specific transport type, credential, raw token, or notification entity |
| `notifications.contract.installation-registration` | Register or refresh a recipient installation | Recipient identity, installation identifier, platform category, and lifecycle state | No raw installation token in another module's public API |

FCM, Hangfire, Worker and Common runtime types, EF entities, repositories, provider credentials, and raw installation tokens are private implementation concerns. FCM configuration and credentials are internal to Notifications and must not appear in public Notifications contracts. The Common job and email wire seam remains closed and is not an Application-facing Notifications contract.

## Integration events consumed

Notifications consumes logical business events through provider-neutral translation. The source module owns when the business fact becomes true. Notifications decides whether and how that fact produces a notification.

| Event ID | Logical event | Source module | Current adapter or contract | Notifications role | Payload boundary |
| --- | --- | --- | --- | --- | --- |
| `notifications.event.training-completed` | `TrainingCompleted` | Workout & Progress | Current adapter: `LgymApi.Application/WorkoutProgress/Contracts/BackgroundCommands/TrainingCompletedCommand.cs` | Translate the source fact into a provider-neutral notification intent, then let Notifications apply recipient, preference, and channel policy after the source commit boundary | Allowed: stable user and training IDs, logical type key, event identity, approved summary metadata, culture or time-zone when already required. Disallowed: `PushInstallation`, `PushNotificationMessage`, `NotificationMessage`, FCM token or credential, Hangfire job ID, repository, or raw EF entity. |
| `notifications.event.trainer-invitation-created` | `TrainerInvitationCreated` | Coaching | Current adapter: `LgymApi.Application/Coaching/Contracts/BackgroundCommands/TrainerInvitationCreatedInAppNotificationCommand.cs` | Translate the invitation fact into a provider-neutral notification intent; Notifications owns whether in-app, push, or email is eligible | Allowed: relationship IDs, recipient ID, event identity, logical type key, and deeplink. Disallowed: notification persistence entities, `PushInstallation`, provider token or credential, Hangfire job ID, repository, or raw cross-module entity. |
| `notifications.event.trainer-invitation-accepted` | `TrainerInvitationAccepted` | Coaching | Current adapter: `LgymApi.Application/Coaching/Contracts/BackgroundCommands/TrainerInvitationAcceptedInAppNotificationCommand.cs` | Translate the acceptance fact into a provider-neutral intent and apply recipient and channel policy | Allowed: relationship IDs, recipient ID, event identity, logical type key, and deeplink. Disallowed: notification persistence entities, provider data, FCM token or credential, Hangfire job ID, repository, or raw EF entity. |
| `notifications.event.password-reset-requested` | `PasswordResetRequested` | Identity & Accounts | Current adapter: `LgymApi.Application/Features/PasswordReset/Contracts/IPasswordRecoveryEmailScheduler.cs` and `PasswordRecoveryEmailRequest.cs` | Accept the provider-neutral recovery email intent and retain correlation and idempotency semantics while the Worker maps it to the existing email seam | Allowed: recipient identity, correlation ID, culture, and approved recovery metadata needed to render the message. Disallowed: password-reset persistence entity, provider object, email credential, raw token outside the approved recovery contract, Hangfire job ID, or repository. |
| `notifications.event.report-submission-accepted` | `ReportSubmissionAccepted` | Reporting | Current compatibility path: `LgymApi.Application/Reporting/Contracts/BackgroundCommands/ReportSubmissionCreatedInAppNotificationCommand.cs` | Translate the current report-submission command into the accepted-submission intent. #381 does not rename or replace this command or path | Allowed: submission and recipient IDs, event identity, logical type key, approved template or display key, and deeplink. Disallowed: `NotificationMessage`, `InAppNotification`, `PushInstallation`, provider token or credential, Hangfire job ID, repository, or raw EF entity. |

## Channel-selection responsibilities

Source modules own business event timing, source-of-truth facts, recipient eligibility facts, and the transaction boundary that publishes or dispatches an event. They may request a logical notification intent, but they do not select a provider, decide delivery retry behavior, or write notification persistence directly.

Notifications owns channel policy and preference evaluation, in-app persistence and fan-out, push enqueue and delivery lifecycle, email subscription and durable intent ownership, and provider-private delivery decisions. It owns the decision to suppress, persist, enqueue, or deliver through a channel. An in-app event does not automatically imply push or email delivery. No contract in this document authorizes automatic push emission for every in-app event.

Coaching compatibility is expressed as six typed Notifications intents. The eight preserved commands select one eligible channel each: invitation created and accepted have separate email and in-app commands, invitation revoked is email-only, and invitation rejected, relationship ended, and trainee note updated are in-app-only. Notifications owns rendering, culture fallback, delivery metadata, email eligibility, and provider-neutral scheduling requests. Worker adapters execute the selected path without regaining policy ownership.

Worker implements runtime handlers and chooses the configured scheduler implementation. Notifications implements stage-only persistence behind the Infrastructure-provided internal seam and the private FCM provider adapter; Infrastructure retains the single context and migration stream. These layers apply the Notifications policy and do not redefine source module business event semantics or expose runtime details through the public contract.

## Privacy, idempotency, and retry rules

Notification payloads and logs use stable identifiers, event categories, type keys, deeplinks where the contract permits them, and delivery status. Push payloads and logs must not dump raw notification content, raw installation tokens, FCM credentials, or provider response objects. Raw notification content and secrets stay private to the smallest implementation boundary that needs them.

Each channel keeps its current deduplication and correlation meaning. In-app delivery retains its `DeliveryKey` behavior and duplicate resolution. Push delivery retains the `(installation, type, eventId)` identity, with `PushEventPayload.EntityId` remaining a polymorphic string exception. Email retains its current notification type, recipient, and correlation based idempotency inputs. Retry and failure status remain Notifications-owned, while Worker and Infrastructure runtime executors only apply the documented policy and record provider outcomes.

Retries must be bounded by the delivery policy and must not create a second logical notification. Existing pending or transiently failed work may be rescheduled through its existing durable identity, not recreated under a new event identity. After a successful claim, exceptions and cancellation persist a recoverable transient state with a bounded exception-type marker before cancellation propagates. Invalid or stale installations are disabled according to the existing lifecycle rules. Cleanup is idempotent and does not delete historical delivery audit data unless an approved retention rule says otherwise. Existing canonical persisted command IDs remain unchanged.

## Compatibility adapters during migration

Compatibility adapters preserve current wire shapes and durable identities after the extraction. They are adapters, not new ownership seams.

| Adapter ID | Current compatibility surface | Boundary rule |
| --- | --- | --- |
| `notifications.adapter.push-payload` | `PushEventPayload.SchemaVersion`, `Type`, `EventId`, `EntityId`, `InAppNotificationId`, and `Deeplink` | Preserve current meaning and serialization. `EntityId` remains the documented polymorphic string exception. |
| `notifications.adapter.legacy-command-ids` | Legacy `LgymApi.BackgroundWorker.Common.Commands.*` persisted IDs | Worker continues writing canonical legacy IDs. Application CLR names remain read aliases only. |
| `notifications.adapter.password-email` | `IPasswordRecoveryEmailScheduler`, `PasswordRecoveryEmailRequest`, `PasswordRecoveryEmailSchedulerAdapter`, and retained Common email payload | Identity exposes its Application request; Worker maps it to the closed Common email wire seam. Correlation and email idempotency remain unchanged. |
| `notifications.adapter.report-submission` | `ReportSubmissionCreatedInAppNotificationCommand` | Keep the current command and path as the adapter for the logical accepted-submission event. #381 does not rename or replace it. |
| `notifications.adapter.scheduler-runtime` | Worker scheduler and Common persisted job interfaces | Worker owns scheduler selection and execution. The Common identities remain stable persisted targets. |
| `notifications.adapter.coaching-intents` | Six Coaching intent records and eight legacy Coaching commands | Notifications owns intent and channel policy; Worker preserves canonical IDs, handler identities, and adapter execution. |

## Historical Migration Sequence

The sequence records the pre-extraction boundary plan. The #387 extraction completed the approved source move while preserving the monolith and compatibility contracts.

| Step ID | Change | Ownership result | Constraint |
| --- | --- | --- | --- |
| `notifications.migration.381` | Publish this boundary document and architecture fixtures | Stable Notifications ownership and provider-neutral contract rules become auditable | Documentation and guards only. No implementation movement. |
| `notifications.migration.382` | Refine push installation registration, refresh, disablement, and stale lifecycle behind the Notifications boundary | Notifications owns installation writes and lifecycle policy; Infrastructure retains persistence implementation | First preserve current registration endpoints, installation identifiers, permission state, entity location, and token privacy. Introduce or verify provider-neutral registration and lifecycle ports without exposing tokens or provider credentials. Do not remove the current adapter until API and worker consumers are covered. |
| `notifications.migration.383` | Consolidate notification intent translation, enqueueing, delivery claims, retries, and cleanup behind the Notifications boundary | Notifications owns channel policy, durable status, deduplication, and retry eligibility; Worker and Infrastructure retain runtime execution and provider roles | Preserve `PushEventPayload` serialization and field meanings, in-app `DeliveryKey`, push `(installation,type,eventId)`, email correlation and idempotency, retry/failure status, scheduler job identities, and provider configuration. Validate duplicate enqueue, transient retry, permanent failure, invalid token, and cleanup behavior before adapter removal. |
| `notifications.migration.adapter-removal` | Remove a compatibility adapter only after all consumers use the stable provider-neutral contract | Ownership is explicit before physical cleanup | Requires a separately approved change, consumer inventory, serialized payload evidence, durable identity evidence, and rollback or coexistence criteria. #381 does not remove, rename, or replace current command types. |

## Guard coverage

The boundary is auditable through stable documentation rows and architecture checks. Guards should parse IDs and table fields rather than depend on exact prose sentences.

| Guard ID | Asserted invariant | Evidence surface |
| --- | --- | --- |
| `notifications.guard.persisted-ownership` | The five Notifications persisted entities appear exactly once in the ownership catalog and this document | `PersistedEntityOwnershipCatalog.cs` and the `Notifications-owned artifacts` table |
| `notifications.guard.public-contracts` | Application-facing Notifications contracts remain provider-neutral and do not expose persistence or runtime types | Application Notifications contract and model paths |
| `notifications.guard.worker-common-seam` | Worker owns runtime selection; Common remains the closed job and email wire seam | `issue-380-background-contract-ownership.md` and Worker/Common surface guards |
| `notifications.guard.compatibility` | Stable push fields, legacy command IDs, delivery keys, and channel idempotency meanings remain documented | Compatibility adapter and privacy sections |
| `notifications.guard.scope` | The boundary does not authorize physical implementation moves, alternate persistence roots, schema splits, microservices, or provider replacement | Scope and non-goals plus stable migration rows |
