# ADR-007: Final modular-monolith compatibility commitments

## Status

Accepted

## Context

ADR-006 established the modular-monolith direction. The completed cutover now needs a durable record of the compatibility constraints that govern the final owner-oriented source layout. This ADR does not replace ADR-006, historical issue-375 records, the ownership catalog, or the project-reference graph.

## Decision

LGYM remains one deployable application with one production `AppDbContext`, one PostgreSQL database, one schema model, and one migration stream. The eight logical owners remain Platform / Reference Data, Identity & Accounts, Notifications, Reporting, Training Planning, Workout & Progress, Coaching, and Nutrition.

External behavior means every established route, verb, authorization policy, request and response DTO shape, JSON property name, status code, localized message, endpoint alias, idempotency behavior, and legacy field that applies to that endpoint. `_id`, `msg`, and `req` are endpoint-specific compatibility fields, not universal response fields. Reporting retains `_id` and `msg` and has no `req` field.

The owner-oriented API handoff has exactly 25 scoped Application adapter contracts and exactly 3 scoped Notifications adapter contracts. Migration-era `Task7`, `ApiCompatibility`, and `Compatibility.Task7` CLR adapter identities are removed. This is an approved CLR-name cleanup only. It does not alter public HTTP or JSON contracts.

Established non-Task7 `LgymApi.Application.*` namespaces remain where a source or wire contract depends on them. Their physical owner project is authoritative. A retained namespace may be removed only through a separately approved compatibility change with a complete consumer inventory, serialized-payload evidence where applicable, and a replacement or removal window.

Three Notifications integration adapters remain intentionally: `PushInstallationSessionDisassociationAdapter`, `CoachingEmailNotificationSchedulerAdapter`, and `PasswordRecoveryEmailSchedulerAdapter`. They are owner-to-owner integration seams, not API adapters and not migration leftovers. Remove one only after its owning contracts, registrations, consumers, durable identities, and behavior have an approved replacement.

Canonical persisted command and job identities remain the legacy `LgymApi.BackgroundWorker.Common.Commands.*` values. Worker writes those canonical IDs. Application CLR names are read aliases only. Common job interface identities and recurring Hangfire job identities remain unchanged.

Direct constructor injection is the final service composition model. High constructor arity is accepted when it makes a focused service's collaborators explicit. There is no numeric constructor limit. Broad dependency aggregates and service location remain forbidden in Application, Identity, and Notifications business services and use cases.

## Consequences

1. Logical ownership governs writes and source placement, while physical persistence remains singular.
2. Owner-oriented API adapters preserve established external contracts without preserving migration-era CLR type names.
3. Compatibility namespaces and retained integration adapters need explicit owner, reason, and removal condition before any future cleanup.
4. Final same-SHA verification evidence is intentionally deferred to Todo 22 and must be recorded only after a clean isolated worktree run.

## Links

- `docs/adr/006-lgym-evolves-as-modular-monolith.md`
- `docs/modular-monolith/issue-395-final-verification.md`
- `docs/modular-monolith/issue-376-ownership-map.md`
- `docs/modular-monolith/issue-380-project-reference-graph.md`
