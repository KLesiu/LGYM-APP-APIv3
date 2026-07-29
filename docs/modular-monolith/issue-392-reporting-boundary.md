# Issue #392: Reporting Boundary

## Status

Reporting is a logical capability boundary in the modular monolith. This document records its current ownership and lifecycle seams. It does not move physical projects or change the persistence topology.

## Ownership

Reporting owns the write lifecycle for these persisted entities and corresponding tables:

- `ReportTemplate`
- `ReportTemplateField`
- `ReportRequest`
- `ReportSubmission`
- `RecurringReportAssignment`
- `Photo`
- `PhotoUploadSession`

The current table names are `ReportTemplates`, `ReportTemplateFields`, `ReportRequests`, `ReportSubmissions`, `RecurringReportAssignments`, `Photos`, and `PhotoUploadSessions`.

The executable ownership catalog remains the authority for the persisted entity roster. The complete roster and totals remain in [`issue-376-ownership-map.md`](issue-376-ownership-map.md).

Reporting application ownership includes:

- `IReportingService` and the `ReportingService*` implementation family for templates, requests, submissions, feedback, and evidence workflows.
- `IRecurringReportAssignmentService` and the `RecurringReportAssignmentService*` implementation family for recurring assignment lifecycle and due assignment processing.
- Five focused persistence ports: `IReportTemplatePersistence`, `IReportRequestSubmissionPersistence`, `IRecurringReportAssignmentPersistence`, `IReportPhotoPersistence`, and `IReportingRelationshipAccessPersistence`.
- `IPhotoStorageProvider` as the application abstraction for provider access. Pending upload-session tracking is part of `IReportPhotoPersistence`.
- `ReportSubmissionAcceptedProgressCommand` as the Reporting-owned accepted-progress command staged through the shared platform outbox port.

Infrastructure implements the five ports under `LgymApi.Infrastructure/Repositories/Reporting/` through `ReportTemplatePersistenceRepository`, `ReportRequestSubmissionPersistenceRepository`, `RecurringReportAssignmentPersistenceRepository`, `ReportPhotoPersistenceRepository`, and `ReportingRelationshipAccessPersistenceRepository`. Reporting entity mappings remain under `LgymApi.Infrastructure/Data/Configurations/Reporting/`. The adapters stage changes only; Application services own authorization, transaction boundaries, and `IUnitOfWork.SaveChangesAsync()`.

## Coaching Authorization Boundary

Reporting and Recurring Reporting consume active trainer-trainee relationship facts only through their consumer-owned `IReportingRelationshipAccessPersistence` port. Trainer status comes from the immutable `AuthenticatedAccountContext`. Reporting owns the interpretation of those facts: a non-trainer retains the existing resource-backed Reporting forbidden error, while a trainer without the active relationship retains the existing Reporting not-found error. Trainee self-access for report photos remains a Reporting rule and bypasses the trainer relationship check.

The access decision carries `Id<AccountReference>` values and a boolean only. Infrastructure translates those marker IDs to persisted `Id<User>` values only inside the Reporting adapters through `ReportingPersistenceAccountIds`. The Application boundary exposes no Identity or Coaching entity, repository, or private implementation. Reporting continues to own report and photo authorization, staged writes, unit-of-work commits, transactions, notification dispatch timing, and accepted-progress outbox staging.

## Evidence and Photo Lifecycle

Reporting owns the business lifecycle for evidence attached to report requests and submissions. That lifecycle includes authorization, view-type normalization, upload-init tracking, generated storage-key conventions, declared size and MIME validation, completion-time object metadata checks, persisted `Photo` metadata, signed reads, history visibility, and cleanup of expired upload sessions.

`PhotoUploadSession` tracks the pending upload agreement, including the initiating user, owner, report request, view type, declared content type and size, storage key, status, and expiry. Completion must match that session before the photo metadata is persisted. An invalid uploaded object is rejected and cleaned up. `ExpiredPhotoUploadCleanupService` deletes expired provider objects before marking their upload sessions expired, and commits only after successful state changes. A failed provider deletion is logged and does not mark that session expired.

`IPhotoStorageProvider` is the application abstraction used by this workflow. Generic storage implementation stays behind module and application abstractions. Local storage, Cloudflare R2 behavior, provider credentials, raw provider responses, bucket details, and other provider-specific implementation details remain Infrastructure-private. Reporting owns the evidence lifecycle, not the generic storage provider implementation.

## Recurring Assignment and Cleanup Jobs

Reporting owns recurring assignment business rules through `IRecurringReportAssignmentService`. The Worker owns execution of the persisted `RecurringReportAssignmentProcessingJob`, which invokes `ProcessDueAssignmentsAsync`. The job retains its existing Hangfire concurrency protection and persisted identity. Processing remains idempotent, respects feedback-read gating, and deactivates assignments whose templates are deleted instead of creating a new request.

`ExpiredPhotoUploadCleanupService` is the Reporting application service for expired evidence upload cleanup and uses `IReportPhotoPersistence` for upload-session state. Its Worker job and scheduler wiring, where present, are runtime adapters. The Worker executes the job, while Reporting remains responsible for the lifecycle policy and state transitions. No persistence adapter owns a commit or transaction.

## Accepted progress outbox flow

Issue #386 is production wiring. Reporting accepts the submission, derives valid measurement triples through its persistence-neutral factory, and stages a Reporting-owned `ReportSubmissionAcceptedProgressCommand` in the existing `CommandEnvelope` outbox before `IUnitOfWork.SaveChangesAsync()`. Reporting does not write Workout & Progress measurement rows, call its repositories, or call its consumer directly. The temporary measurement adapter was removed.

The committed envelope is delivered through the existing committed-intent and `ActionMessage` infrastructure. The Worker handler forwards shared JSON to the Workout & Progress owner, whose raw wire parser validates and maps it to the `ReportSubmissionAcceptedProgressConsumer` event. The consumer deduplicates by body part over the `ObservedAt` UTC day, stages only missing measurements, and commits once when rows are staged. `Applied` and `Duplicate` are successful outcomes. Invalid, unsupported-schema, and poison outcomes become sanitized, bounded failures for the existing retry/dead-letter path. Unexpected persistence exceptions propagate and remain retryable.

Operational records and logs expose event ID, report submission ID, correlation ID, causation ID, schema version, outcome, retry or dead-letter state, and counts. They must not expose raw answer JSON, photos, device tokens, or payload dumps.

## API Compatibility Guard

Reporting API routes, HTTP verbs, action aliases, DTO type and property names, `JsonPropertyName` values, declared response types, and required legacy fields are compatibility contracts. The guarded API contract surface is maintained by architecture tests, including `ReportingApiContractImmutabilityGuardTests`, which fail on silent drift. Existing aliases and legacy fields, including `_id` and `msg`, remain part of the guarded surface. The current Reporting DTO contract has no `req` JSON property.

## Persistence and Deployment Boundary

Reporting uses the current shared persistence composition. The production system has one `AppDbContext`, one PostgreSQL database, and one migration stream. Logical write ownership does not create a physical database, schema, context, migration stream, service, or deployment split. There is no broker. Existing tables, migrations, worker identities, routes, and payload shapes remain compatible.

For the broader ownership matrix and cross-module rules, see [`issue-376-ownership-map.md`](issue-376-ownership-map.md). The durable API contract guard is maintained in `LgymApi.ArchitectureTests/ReportingApiContractImmutabilityGuardTests.cs`.
