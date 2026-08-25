# LgymApi.Domain.csproj

- Purpose: core domain model.
- Contains: entities, enums, strongly typed IDs, domain helpers, and auth/security constants.
- Rules: keep free of HTTP, EF, and API concerns.
- Boundary: do not reorder or renumber existing enums.
- `Id<T>.Rebind<TTarget>()` preserves the UUID while changing only the compile-time scope for contract boundaries; it does not replace entity PK/FK types.
- Dependency boundary: Domain has no project references and stays localization-neutral. Application and Resources derive enum labels from the `EnumType_EnumMember` convention; Domain must not reference Resources or contain localized display text.
- `Exercise` now carries `ExerciseEloFormula` with `Standard` as the default profile.
- `ExerciseEloFormula.PullupWeighted` rewards lower weight for pull-up style exercises where added weight makes the score worse.
- `PushInstallation` stores installation-scoped FCM registration state with optional user/session binding so logout and account-switch flows can disassociate a device without deleting its installation record.
- `PushInstallationDisabledReasons.InvalidToken` preserves the legacy disabled-reason value used when provider-neutral delivery classifies an installation token as invalid.
- `User.AdultConfirmedAt` and `User.AdultConfirmationVersion` store only the timestamp and server-owned version of the 18+ self-declaration; no date of birth or identity-document data is stored.
- Notifications ownership is module and write responsibility, not physical relocation: `InAppNotification`, `NotificationMessage`, `EmailNotificationSubscription`, `PushInstallation`, and `PushNotificationMessage` remain under `LgymApi.Domain/Entities` while Notifications owns their write rules. Non-owner modules use published contracts, views, or events; see [`issue-381-notifications-boundary.md`](../docs/modular-monolith/issue-381-notifications-boundary.md).
- #387 moved the approved Notifications implementation into its module assembly while preserving the physical entity locations, shared `AppDbContext`, and single migration stream.
- `CommandEnvelope.CommandTypeFullName` retains its legacy property/column name but stores the canonical legacy `LgymApi.BackgroundWorker.Common.Commands.*` command ID rather than an arbitrary CLR full name. Application CLR names are read aliases only and are never persisted as the canonical ID.
- Nutrition logically owns `DietPlan`, `DietMeal`, `DietPlanHistory`, `SupplementPlan`, `SupplementPlanItem`, and `SupplementIntakeLog`. They remain physical Domain entities in the shared `AppDbContext`, PostgreSQL database, and migration stream; this ownership cutover adds no entity, schema, migration, or enum change.
- This logical ownership record leaves Worker behavior and its project document, DataSeeder behavior, every `.csproj`, and the root Project Purpose Map unchanged.
