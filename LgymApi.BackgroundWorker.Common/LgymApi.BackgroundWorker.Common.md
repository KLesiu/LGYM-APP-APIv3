# LgymApi.BackgroundWorker.Common.csproj

- Purpose: exact bounded Worker/Infrastructure seam for persisted jobs and email wire contracts.
- Contains: nine persisted job interfaces, two scheduler bridges, the idempotency policy, email interfaces/message, and six email payloads.
- Rules: preserve this closed surface and its typed-ID job signatures. Do not add HTTP/controller code, Application-facing contracts, commands, serialization, or push delivery types.
- Boundary: this project references Domain only. Application does not reference it.
- The unchanged job interfaces, including `IPushNotificationJob`, and their scheduler expression targets retain their persisted Hangfire identities. The Worker owns implementations.
- Notifications boundary: Common remains the closed persisted-job and email wire seam, not an Application-facing Notifications contract surface. Worker maps provider-neutral Application requests to this seam without changing its identities or payload compatibility; no push contracts or commands are added here. See [`issue-381-notifications-boundary.md`](../docs/modular-monolith/issue-381-notifications-boundary.md).
- #381 does not change Common project references, job interfaces or identities, email wire payloads, providers, schedulers, persistence, physical entity locations, or migrations.
