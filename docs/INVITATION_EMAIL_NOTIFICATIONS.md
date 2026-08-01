# Invitation Email Notifications (NotificationMessages)

This module sends trainer invitation emails asynchronously and stores delivery state in `NotificationMessages`.

## What Happens on Invitation Create

1. Coaching creates `TrainerInvitation` and stages the legacy invitation-created command at its application commit boundary.
2. The Worker handler reads public Coaching and Identity facts and submits the email-only invitation-created Notifications intent.
3. Notifications applies recipient, culture, correlation, idempotency, and email-feature policy, then produces a provider-neutral scheduling request when email is eligible.
4. The Worker `CoachingEmailNotificationSchedulerAdapter` maps that request to the unchanged Common invitation payload and generic email scheduler.
5. The scheduler creates or reuses the `NotificationMessage` row and schedules `EmailJob`; the HTTP response does not wait for SMTP.

## Notification Message Model

Table: `NotificationMessages`

- `Id`
- `Channel` (`Email`)
- `Type` (`trainer.invitation.created`)
- `CorrelationId` (invitation id)
- `Recipient`
- `PayloadJson`
- `Status` (`Pending`, `Sending`, `Sent`, `Failed`)
- `Attempts`
- `NextAttemptAt`
- `LastError`
- `LastAttemptAt`
- `SentAt`
- `DeliveredAt`
- `CreatedAt`
- `UpdatedAt`

Indexes:

- `(Status, NextAttemptAt, CreatedAt)` for processing/querying pending items.
- unique `(Channel, Type, CorrelationId, Recipient)` for idempotency.

## Retry and Idempotency Rules

- Duplicate scheduling for the same `Type + CorrelationId + Recipient + Channel` does not create a second row.
- Job handler first atomically claims the row (`Pending -> Sending`) while stamping the send lease in `LastAttemptAt`.
- A stale or interrupted `Sending` row can be reclaimed when the lease expires, or when an older broken row is missing `LastAttemptAt`.
- The recurring committed-intent dispatcher re-enqueues stale `Sending` notifications so recovery does not depend on a duplicate business event happening later.
- Job handler exits immediately when the notification cannot be claimed or is already complete.
- Hangfire retries failed executions (`1m`, `5m`, `15m`).
- Attempts and failure details are persisted on each failed run.
- Successful retry clears `LastError`, sets `SentAt`, and then best-effort persists `DeliveredAt`.

## Runtime Topology

- Notifications owns email intent policy and `NotificationMessages` write responsibility.
- Notifications compiles the Dummy/SMTP senders, email options, metrics, six composers, and templates; Common retains the scheduled email payload and provider interfaces.
- `LgymApi.BackgroundWorker` owns `EmailJob`, generic email scheduling, and the `CoachingEmailNotificationSchedulerAdapter` that maps the provider-neutral Coaching request to the retained Common payload.
- The host composes module-owned registrations before Worker registration. The Worker project supplies runtime implementations and is not a separate host process.
- The application remains one deployable with one `AppDbContext`, PostgreSQL database, and migration stream.

## Required Configuration

Section: `Email`

- `Enabled` (`true/false`)
- `DeliveryMode` (`Smtp` or `Dummy`, defaults to `Smtp`)
- `DummyOutputDirectory` (required when `DeliveryMode=Dummy`)
- `FromAddress`
- `FromName`
- `SmtpHost`
- `SmtpPort`
- `Username`
- `Password`
- `UseSsl`
- `InvitationBaseUrl`
- `TemplateRootPath`
- `DefaultCulture`

Notes:

- If `Enabled=false`, scheduler short-circuits (no enqueue).
- If `Enabled=true`, startup validation enforces required values and valid URL/email formats.
- In `Dummy` mode, no SMTP connection is used and each outgoing email is saved as a text file in `DummyOutputDirectory`.
- The twelve templates are sourced from `LgymApi.Notifications/EmailTemplates` and copied to `AppContext.BaseDirectory/EmailTemplates/<Template>/<en|pl>.email` for API and Integration runtime lookup.

## Verification Checklist

1. Create invitation and confirm one pending row in `NotificationMessages`.
2. Process worker job and confirm `Status=Sent` and `SentAt` set.
3. Simulate sender failure and confirm `Status=Failed`, `Attempts` increment, `LastError` set.
4. Reprocess and confirm status returns to `Sent` without duplicate sends.
