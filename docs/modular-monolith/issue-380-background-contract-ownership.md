# Issue #380: Background Contract Ownership

## Status

Current contract and runtime ownership after the #387 module extraction.

## Ownership

Platform owns the shared dispatcher, stage-only outbox ports, and persisted-payload serialization contracts. Their physical sources compile from `LgymApi.Platform` while retaining established `LgymApi.Application.Platform.Contracts.*` namespaces where compatibility requires them.

Identity, Notifications, Training Planning, and the remaining Application feature areas own their feature commands and public scheduling contracts. `LgymApi.Application` never references a Worker project or namespace.

`LgymApi.BackgroundWorker` owns the closed command registry, handler registration, job execution, scheduler adapters, recurring-job registration, and testing versus non-testing scheduler selection. `LgymApi.BackgroundWorker.Common` remains the bounded persisted-job and email-wire seam. It does not own feature commands, serialization, push contracts, or Application-facing ports.

Notifications owns provider-neutral push contracts, policy, private FCM delivery, and email/template composition. Worker invokes those contracts through durable job interfaces and selects no-op schedulers for Testing or Hangfire schedulers otherwise. Infrastructure owns the shared context, Hangfire storage/server registration, and the post-commit runtime bridge.

## Durable Compatibility

- Canonical persisted command IDs remain the legacy `LgymApi.BackgroundWorker.Common.Commands.*` strings.
- Worker writers emit canonical IDs only. CLR names from Application or extracted modules are accepted as read aliases where the registry defines them.
- The registry remains closed at 15 commands and 16 handlers. `TrainingCompletedCommand` is the sole two-handler command.
- Common job interface targets, method signatures, recurring-job identities, email wire models, and template output paths remain unchanged.
- Provider SDK types, credentials, raw push tokens, and raw provider responses remain private to the smallest runtime owner that needs them.

## Composition Boundary

`AddBackgroundWorkerServices` is the Worker registration facade and is composed last by the API host. It registers the command registry, dispatcher implementation, handler runtime, password-recovery adapter, generic email scheduling, Coaching email scheduling, and environment-selected push scheduling. It does not register module services, module repositories, providers, or the Hangfire persistence server.

The Worker password-recovery adapter maps Identity's public request to the retained Common email payload. Notifications email and push workflows use the same rule: Worker and Common preserve runtime and wire identities, while Notifications retains policy and provider ownership.

## Verification

Run the project graph and import guards, command and envelope compatibility tests, Hangfire compatibility tests, and email payload/template tests. The current graph is defined by [issue-380-project-reference-graph.md](issue-380-project-reference-graph.md). [issue-375-project-reference-graph.md](issue-375-project-reference-graph.md) is an unchanged historical capture.
