# LgymApi.Domain.csproj

- Purpose: core domain model.
- Contains: entities, enums, strongly typed IDs, domain helpers, and auth/security constants.
- Rules: keep free of HTTP, EF, and API concerns.
- Boundary: do not reorder or renumber existing enums.
- `Exercise` now carries `ExerciseEloFormula` with `Standard` as the default profile.
- `ExerciseEloFormula.PullupWeighted` rewards lower weight for pull-up style exercises where added weight makes the score worse.
- `PushInstallation` stores installation-scoped FCM registration state with optional user/session binding so logout and account-switch flows can disassociate a device without deleting its installation record.
- `PushInstallationDisabledReasons.InvalidToken` preserves the legacy disabled-reason value used when provider-neutral delivery classifies an installation token as invalid.
- Notifications ownership is module and write responsibility, not physical relocation: `InAppNotification`, `NotificationMessage`, `EmailNotificationSubscription`, `PushInstallation`, and `PushNotificationMessage` remain under `LgymApi.Domain/Entities` while Notifications owns their write rules. Non-owner modules use published contracts, views, or events; see [`issue-381-notifications-boundary.md`](../docs/modular-monolith/issue-381-notifications-boundary.md).
- #381 leaves project references, physical entity locations, the shared `AppDbContext`, and the single migration stream unchanged.
- `CommandEnvelope.CommandTypeFullName` retains its legacy property/column name but stores the canonical legacy `LgymApi.BackgroundWorker.Common.Commands.*` command ID rather than an arbitrary CLR full name. Application CLR names are read aliases only and are never persisted as the canonical ID.
- Nutrition logically owns `DietPlan`, `DietMeal`, `DietPlanHistory`, `SupplementPlan`, `SupplementPlanItem`, and `SupplementIntakeLog`. They remain physical Domain entities in the shared `AppDbContext`, PostgreSQL database, and migration stream; this ownership cutover adds no entity, schema, migration, or enum change.
- This logical ownership record leaves Worker behavior and its project document, DataSeeder behavior, every `.csproj`, and the root Project Purpose Map unchanged.
