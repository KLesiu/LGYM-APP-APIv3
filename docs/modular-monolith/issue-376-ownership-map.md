# Issue #376: Ownership Map

## Status
Complete

## Source precedence

- `#311` is the authority for ownership rules.
- `#375` is the historical inventory source.
- `#380` is the current source for background-contract ownership and project references.
- `#381` clarifies Notifications write ownership and provider-neutral contracts without relocating physical artifacts.
- `#391` codifies Workout & Progress logical ownership and application-path classification without changing persistence topology or API contracts.
- `#390` codifies Nutrition's six persisted entities, 18 focused actions, four controller adapters, and unchanged Diet command compatibility without changing persistence topology or ownership totals.
- `docs/ARCHITECTURE.md` is the integration guide that links the current maps.
- ADR-006 states the modular-monolith decision this map serves.

## Scope

This file contains the fixed one-owner matrix for the #375 hotspot and cross-feature inventory, plus the shared runtime roots called out in the issue #376 plan. One artifact, one owner module.

## Owner matrix

| Artifact type | Artifact name | Owner module | Justification | Allowed non-owner access mode |
| --- | --- | --- | --- | --- |
| Runtime root | `Program.cs` | `Platform / Reference Data` | The host composition root wires the whole runtime and owns startup registration. | Other modules do not register services here. They consume the host through DI and public endpoints only. |
| Runtime root | `LgymApi.Application/ServiceCollectionExtensions.cs` | `Platform / Reference Data` | Application registration is a shared startup concern and not a feature-owned decision. | Non-owner modules reference the registered services through DI. They do not add registrations here. |
| Runtime root | `LgymApi.Infrastructure/ServiceCollectionExtensions.cs` | `Platform / Reference Data` | Infrastructure registration owns the shared technical wiring for repositories, UoW, storage, and external adapters. | Non-owner modules use the registered technical services through DI only. |
| Runtime root | `LgymApi.BackgroundWorker/ServiceProvider.cs` | `Platform / Reference Data` | The background worker host is part of the shared runtime wiring surface. | Non-owner modules schedule work through the public worker contracts, not by editing this host. |
| Shared persistence root | `AppDbContext` | `Platform / Reference Data` | The single production EF Core context is the shared persistence root for the current monolith. | Non-owners only reach it through owner repositories and owner services. They do not create alternate context roots. |
| Shared contract | `IUnitOfWork` | `Platform / Reference Data` | Commit timing is a shared runtime rule that stays outside feature ownership. | Non-owner modules receive it only at service boundaries. Repositories do not own commits. |
| Shared contract | `ICommandDispatcher` | `Platform / Reference Data` | Command dispatch is a cross-feature runtime concern used by several modules. | Non-owners dispatch commands through the interface only. They do not embed dispatch logic in repositories or controllers. |
| Shared contract path | `LgymApi.Application/Platform/Contracts/BackgroundCommands/` and `LgymApi.Application/Platform/Contracts/Serialization/` | `Platform / Reference Data` | Platform owns the dispatcher ports and persisted-payload serialization contracts used across modules. | Feature modules consume the dispatcher and serializer. They do not place feature commands in Platform. |
| Module command paths | `LgymApi.Application/Identity/Contracts/BackgroundCommands/`, `LgymApi.Application/WorkoutProgress/Contracts/BackgroundCommands/`, `LgymApi.Application/Coaching/Contracts/BackgroundCommands/`, `LgymApi.Application/Reporting/Contracts/BackgroundCommands/`, `LgymApi.Application/Nutrition/Contracts/BackgroundCommands/` | Respective feature module | A command belongs to the module that owns the business event it describes. | Producers dispatch through the Platform port. Worker handlers implement the contracts without moving command ownership to Worker or Common. |
| Password scheduling path | `LgymApi.Application/Features/PasswordReset/Contracts/` | `Identity & Accounts` | Password recovery is an Identity workflow. Its Application request and scheduler port are not Common email types. | The Worker password-recovery adapter maps the request to the retained Common payload. |
| Push contract path | `LgymApi.Application/Notifications/Contracts/Push/` | `Notifications` | Push payloads, outcomes, scheduler, and provider ports describe notification delivery. | Worker selects and implements scheduling, Infrastructure implements FCM, and Common retains only the persisted push job interface. |
| Worker runtime path | `LgymApi.BackgroundWorker/Runtime/` and Worker adapters | `Platform / Reference Data` runtime | Registry, handler registration, dispatch execution, Hangfire adapters, and password scheduling adapters are Worker runtime concerns. | Application exposes ports and commands only. Application must not reference Worker projects or namespaces. |
| Common bounded seam | `LgymApi.BackgroundWorker.Common/Jobs/` and `LgymApi.BackgroundWorker.Common/Notifications/` | `BackgroundWorker.Common` | Common exists only for persisted job identities, scheduler bridges, idempotency policy, and email wire contracts. | It must not contain Application-facing commands, serialization, push delivery contracts, or Worker runtime contracts. |
| Shared contract | `IGridifyExecutionService` | `Platform / Reference Data` | Query paging and filtering support is shared technical infrastructure. | Non-owner modules call it through the public interface. They do not own its registration or implementation. |
| Shared contract | `IMapperRegistry` | `Platform / Reference Data` | Mapper registration is a shared technical concern that supports the current mapping system. | Non-owner modules consume registered mappings only. They do not create competing registries. |
| Focused service contracts | `IUserRegistrationService`, `IUserCredentialLoginService`, `IUserSessionTerminationService`, `IUserProfileService`, `IUserRankingService`, `IUserAdminAccessService`, and `IUserRoleAdministrationService` | `Identity & Accounts` | Registration, credential login, session, profile, ranking, admin-access, and role-administration use cases belong to the identity boundary. | Other modules use the focused contracts or read models; they do not touch user state directly. |
| Focused dependency aggregates | Service-owned aggregates such as `UserRegistrationServiceDependencies` and `UserProfileServiceDependencies` | `Identity & Accounts` | A dependency aggregate, when needed, is owned by one focused service rather than a broad user-service facade. | Non-owners do not construct the aggregate; they use the focused service contract. |
| Repository contract | `IUserRepository` | `Identity & Accounts` | User lookup and account ownership sit inside the identity boundary. | Non-owners query users through the identity service or a read contract, not by direct repository access. |
| Repository contract | `IRoleRepository` | `Identity & Accounts` | Roles are part of the account and authorization surface. | Non-owners read role data through service contracts only. |
| Repository contract | `IEloRegistryRepository` | `Workout & Progress` | ELO registry persistence and ranking progress belong to the workout boundary. | Non-owners use the owning services or read models instead of direct repository calls. |
| Service contract | `ITokenService` | `Identity & Accounts` | Token creation and validation are part of authentication. | Non-owners consume tokens through the auth flow, not by calling internals. |
| Service contract | `ILegacyPasswordService` | `Identity & Accounts` | Legacy password verification is an auth boundary concern. | Non-owners use the identity service flow rather than calling password internals. |
| Service contract | `IRankService` | `Identity & Accounts` | Rank calculation is exposed with the account surface and user state. | Non-owners request rank data through the identity service or read models. |
| Service contract | `IUserSessionStore` | `Identity & Accounts` | Session state belongs to the user boundary and its auth lifecycle. | Non-owners use the public auth flow and session contracts only. |
| Service contract | `ITutorialService` | `Identity & Accounts` | Onboarding and tutorial state are part of the user experience owned by identity. | Non-owners consume the public tutorial flow, not the persistence layer. |
| Table | `Users` | `Identity & Accounts` | The user table is the root account record for the identity boundary. | Non-owners read it through user-facing services and never write it directly. |
| Table | `EloRegistries` | `Workout & Progress` | ELO registry rows track workout-progress ranking state. | Non-owners use the owning services or read models only. |
| Seed constant | `RoleSeedDataConfiguration.TesterRoleSeedId` | `Identity & Accounts` | The tester role seed identifier supports account bootstrap and user lookup rules. | Non-owners consume it only through the identity seed path and tests. |
| Service family | `InAppNotificationService*` | `Notifications` | In-app notification persistence and fan-out are notification concerns. | Non-owners call the notification service or published contracts only. |
| Dependency bag | `IInAppNotificationServiceDependencies` | `Notifications` | The dependency bag belongs to the in-app notification service family. | Non-owners do not construct the bag or bypass the service. |
| Repository contract | `IInAppNotificationRepository` | `Notifications` | In-app notification rows are notification-owned persisted state. | Non-owners consume notifications through the public service or read model. |
| Publisher contract | `IInAppNotificationPushPublisher` | `Notifications` | Push fan-out from in-app notifications belongs to the notifications boundary. | Non-owners publish only through the notification service workflow. |
| Bridge contract | `INotificationEventBridge` | `Notifications` | Event bridging for notification fan-out is notification-owned orchestration. | Non-owners receive events through the bridge contract only. |
| Service family | `PushNotificationService` | `Notifications` | Push enqueueing, deduplication, and message lifecycle belong to notifications. | Non-owners enqueue through the service contract only. |
| Service family | `PushNotificationDeliveryService` | `Notifications` | Push delivery claims, state transitions, retry policy, and commit timing belong to notifications. | Worker forwards the durable message ID and cancellation token through the application contract only. |
| Repository contract | `PushInstallationRepository / IPushInstallationRepository` | `Notifications` | Installation ownership and stale cleanup are notification state. | Non-owners interact through registration and cleanup services, not direct writes. |
| Repository contract | `PushNotificationMessageRepository / IPushNotificationMessageRepository` | `Notifications` | Push message lifecycle and retry claims belong to notifications. | Non-owners consume push status through the service contract only. |
| Scheduler contract | `IPushBackgroundScheduler` | `Notifications` | Background push scheduling is part of the notification delivery boundary. | Non-owners schedule delivery through the public notification workflow only. |
| Service family | `StalePushInstallationCleanupService` | `Notifications` | Stale-installation cleanup is notification maintenance, not a shared utility. | Non-owners trigger it only through the worker contract and not via direct table edits. |
| Table | `PushInstallations` | `Notifications` | Installation rows are owned by notification registration and cleanup. | Non-owners never mutate the table directly. They use the notification service flow. |
| Table | `PushNotificationMessages` | `Notifications` | Push message rows are owned by notification delivery. | Non-owners only observe message state through the service contract or read model. |
| Service family | `ReportingService*` | `Reporting` | Report templates, requests, submissions, and photo handling belong to reporting. | Non-owners use the reporting service or published contracts only. |
| Dependency bag | `IReportingServiceDependencies` | `Reporting` | The dependency bag belongs to the reporting service family. | Non-owners do not compose the bag themselves. |
| Service family | `RecurringReportAssignmentService*` | `Reporting` | Recurring assignment processing is part of reporting orchestration. | Non-owners call the reporting service layer, not the repository set directly. |
| Dependency bag | `IRecurringReportAssignmentServiceDependencies` | `Reporting` | The dependency bag belongs to the recurring assignment service family. | Non-owners do not construct the bag. |
| Repository contract | `IReportingRepository` | `Reporting` | Reporting templates, requests, submissions, and photos are reporting-owned persisted state. | Non-owners use the reporting service or read models instead of direct repository access. |
| Repository contract | `IRecurringReportAssignmentRepository` | `Reporting` | Recurring assignment rows belong to reporting orchestration. | Non-owners reach them only through the reporting service. |
| Service contract | `IPhotoStorageProvider` | `Reporting` | Photo storage is part of the reporting photo workflow. | Non-owners consume it through the reporting upload flow only. |
| Service contract | `IPhotoUploadInitTracker` | `Reporting` | Upload init tracking belongs to reporting because it gates report photo uploads. | Non-owners do not track upload state directly. |
| Table | `ReportTemplates` | `Reporting` | Templates are owned by reporting. | Non-owners read templates through reporting contracts only. |
| Table | `ReportRequests` | `Reporting` | Report requests are owned by reporting. | Non-owners do not write requests directly. |
| Table | `ReportSubmissions` | `Reporting` | Report submissions are owned by reporting. | Non-owners consume submission state through reporting contracts only. |
| Table | `Photos` | `Reporting` | Report photos are owned by reporting because the photo flow is part of report submission. | Non-owners use the reporting photo workflow only. |
| Service family | `PlanService` | `Training Planning` | Plan ownership and sharing belong to the planning boundary. | Non-owners consume plans through the planning service or read models only. |
| Repository contract | `PlanRepository / IPlanRepository` | `Training Planning` | Plan persistence is owned by the planning boundary. | Non-owners use the planning service, not the repository directly. |
| Repository contract | `IPlanDayRepository` | `Training Planning` | Plan day persistence belongs to the planning boundary. | Non-owners access plan days through the planning service only. |
| Table | `Plans` | `Training Planning` | Plan rows are owned by the planning boundary. | Non-owners do not write plans directly. |
| Service family | `TrainingService*` | `Workout & Progress` | Training, scoring, and workout progress belong to the workout boundary. | Non-owners call the training service or read model only. |
| Dependency bag | `ITrainingServiceDependencies` | `Workout & Progress` | The dependency bag belongs to the training service family. | Non-owners do not compose the bag themselves. |
| Repository contract | `IGymRepository` | `Workout & Progress` | Gym data is part of workout and progress tracking. | Non-owners query gyms through the workout service or read model. |
| Repository contract | `TrainingRepository / ITrainingRepository` | `Workout & Progress` | Training history is owned by the workout boundary. | Non-owners use the training service or read model only. |
| Repository contract | `IExerciseRepository` | `Workout & Progress` | Exercise lookup is part of workout and progress. | Non-owners use workout-facing service contracts instead of direct repository calls. |
| Repository contract | `IExerciseScoreRepository` | `Workout & Progress` | Exercise score persistence belongs to workout progress. | Non-owners read scores through the workout service or score read models. |
| Repository contract | `ITrainingExerciseScoreRepository` | `Workout & Progress` | Training exercise score persistence belongs to workout progress. | Non-owners use the workout service only. |
| Service contract | `IExerciseEloCalculator` | `Workout & Progress` | ELO calculation for workout performance belongs to the workout boundary. | Non-owners consume the calculator only through workout services. |
| Table | `Trainings` | `Workout & Progress` | Training rows are owned by workout and progress. | Non-owners do not write training rows directly. |
| Public access contract | `ICoachingRelationshipAccessService` | `Coaching` | Publishes trainer and active-link facts to Reporting and Nutrition without exposing Coaching persistence. | Consumers use the ID-only contract and adapt its decision to their own errors. |
| Consumer-owned authorization adapters | `IPlanDayRelationshipAccessPort` and `IMeasurementsRelationshipAccessPort` implementations | `Coaching` | Coaching maps its relationship decision to each consumer-owned boolean port. | Training Planning and Workout & Progress own their port contracts and do not depend on Coaching. |
| Focused capability family | Invitations, relationships, dashboard and progress, managed plans, and trainee notes | `Coaching` | The 31 focused Coaching actions own relationship, invitation, dashboard, note, and collaboration orchestration. | Non-owners use published contracts or owner read models only. |
| Persistence adapters | Focused invitation, active-link, fact-reader, and trainee-note/history ports | `Coaching` | Coaching persistence is stage-only and limited to its four owned entities. | Application use cases own authorization, transactions, and commits. |
| Service contract | `ITrainingService` | `Workout & Progress` | Training is the workout boundary, even when coaching depends on it. | Non-owners call the training service interface only. |
| Repository contract | `MainRecordRepository / IMainRecordRepository` | `Workout & Progress` | Main-record persistence belongs to workout progress, even when coaching workflows consume the resulting records. | Non-owners use the owning service or published read model only. |
| Table | `TrainerInvitations` | `Coaching` | Trainer invitations are owned by coaching. | Non-owners do not mutate invitations directly. |
| Table | `TrainerTraineeLinks` | `Coaching` | Trainer to trainee links are owned by coaching. | Non-owners do not mutate links directly. |
| Table | `DietPlans` | `Nutrition` | Diet plan rows are owned by nutrition. | Non-owners do not write diet plans directly. |
| Table | `DietMeals` | `Nutrition` | Diet meal rows are owned by nutrition. | Non-owners do not write diet meals directly. |
| Table | `DietPlanHistories` | `Nutrition` | Diet plan history rows are owned by nutrition. | Non-owners do not write diet plan history directly. |
| Table | `SupplementPlans` | `Nutrition` | Supplement plan rows are owned by nutrition. | Non-owners do not write supplement plans directly. |
| Table | `SupplementPlanItems` | `Nutrition` | Supplement plan item rows are owned by nutrition. | Non-owners do not write supplement plan items directly. |
| Table | `SupplementIntakeLogs` | `Nutrition` | Supplement intake log rows are owned by nutrition. | Non-owners do not write supplement intake logs directly. |
| Focused capability family | Diet D1-D9 and Supplementation S1-S9 | `Nutrition` | The 18 focused Nutrition actions own Diet and Supplementation lifecycle rules through module-local contracts and persistence ports. | The four existing API controllers adapt HTTP requests; non-owners use published contracts and do not write Nutrition entities directly. |
| Persistence adapters | `IDietPlanPersistence` / `DietPlanPersistenceRepository` and `ISupplementationPersistence` / `SupplementationPersistenceRepository` | `Nutrition` | Nutrition owns stage-only persistence seams for its six entities. | Application use cases own authorization and UoW commits; reads remain no-tracking and repositories do not save or start transactions. |

## Notes

- No placeholder rows remain.
- The owner choice is deterministic, not advisory.
- If an artifact can be used by another module, that module uses the owner service, contract, or read model.
- A non-owner never gets write authority just because the solution still uses one production `AppDbContext`.
- The Notifications rows define module/write responsibility; #381 does not move their entities from `LgymApi.Domain/Entities` or create a separate persistence root.
- All owner rows describe logical write ownership only. They do not create a physical database, `DbContext`, schema, or migration-stream split.
- Completed `Training` rows remain `Workout & Progress`-owned. `Training.TypePlanDayId` references the `Training Planning` definition used for a performed workout; it does not grant Training Planning write ownership over completed training.
- Workout & Progress publishes `ProgressData`, dashboard, ranking, training execution/history, and the production `#386` accepted-progress consumer surface as explicit contracts/read models. Foreign modules do not reference its entities, repositories, or implementation classes. Reporting stages the accepted-progress command in the shared `CommandEnvelope` outbox, and Workout & Progress owns measurement persistence. Legacy API routes remain unchanged.
- Nutrition ownership remains six of the 48 persisted entities. Its D1-D9 and S1-S9 actions are logical capability ownership only, with four existing controller adapters and no physical database, `DbContext`, schema, or migration-stream split.

## Persisted Entity Ownership Catalog

`LgymApi.ArchitectureTests/PersistedEntityOwnershipCatalog.cs` is the executable source of truth for persisted-entity ownership. This table is a verified documentation view of that compiled catalog, not an independent authority. `PersistedEntityOwnershipDocumentationTests` parses these rows and fails if a row is missing, duplicated, or names an entity outside the catalog.

Application files under `LgymApi.Application/WorkoutProgress/` classify as `Workout & Progress`. Until physical moves or wrappers replace them, the legacy `Training`, `Exercise`, `ExerciseScores`, `Gym`, `Measurements`, `MainRecords`, and `EloRegistry` application folders classify the same way.

| Persisted entity | Owner module |
| --- | --- |
| `User` | `Identity & Accounts` |
| `Role` | `Identity & Accounts` |
| `UserRole` | `Identity & Accounts` |
| `RoleClaim` | `Identity & Accounts` |
| `PasswordResetToken` | `Identity & Accounts` |
| `UserExternalLogin` | `Identity & Accounts` |
| `UserSession` | `Identity & Accounts` |
| `UserTutorialProgress` | `Identity & Accounts` |
| `UserTutorialStepProgress` | `Identity & Accounts` |
| `NotificationMessage` | `Notifications` |
| `EmailNotificationSubscription` | `Notifications` |
| `PushInstallation` | `Notifications` |
| `PushNotificationMessage` | `Notifications` |
| `InAppNotification` | `Notifications` |
| `ReportTemplate` | `Reporting` |
| `ReportTemplateField` | `Reporting` |
| `ReportRequest` | `Reporting` |
| `ReportSubmission` | `Reporting` |
| `RecurringReportAssignment` | `Reporting` |
| `Photo` | `Reporting` |
| `PhotoUploadSession` | `Reporting` |
| `Plan` | `Training Planning` |
| `PlanDay` | `Training Planning` |
| `PlanDayExercise` | `Training Planning` |
| `Exercise` | `Workout & Progress` |
| `ExerciseTranslation` | `Workout & Progress` |
| `Training` | `Workout & Progress` |
| `TrainingExerciseScore` | `Workout & Progress` |
| `ExerciseScore` | `Workout & Progress` |
| `Measurement` | `Workout & Progress` |
| `MainRecord` | `Workout & Progress` |
| `Gym` | `Workout & Progress` |
| `Address` | `Workout & Progress` |
| `EloRegistry` | `Workout & Progress` |
| `TrainerInvitation` | `Coaching` |
| `TrainerTraineeLink` | `Coaching` |
| `TraineeNote` | `Coaching` |
| `TraineeNoteHistory` | `Coaching` |
| `DietPlan` | `Nutrition` |
| `DietMeal` | `Nutrition` |
| `DietPlanHistory` | `Nutrition` |
| `SupplementPlan` | `Nutrition` |
| `SupplementPlanItem` | `Nutrition` |
| `SupplementIntakeLog` | `Nutrition` |
| `AppConfig` | `Platform / Reference Data` |
| `ActionExecutionLog` | `Platform / Reference Data` |
| `CommandEnvelope` | `Platform / Reference Data` |
| `ApiIdempotencyRecord` | `Platform / Reference Data` |

Totals are fixed: Identity & Accounts 9, Notifications 5, Reporting 7, Training Planning 3, Workout & Progress 10, Coaching 4, Nutrition 6, and Platform / Reference Data 4.

## Ownership tie-breaker

If an artifact appears to touch more than one module, the owner is the module that owns the write boundary for that artifact. If the artifact is a shared runtime contract, the owner is `Platform / Reference Data`.

## Links

- `docs/adr/006-lgym-evolves-as-modular-monolith.md`
- `docs/modular-monolith/issue-376-module-context-map.md`
- `docs/modular-monolith/issue-380-background-contract-ownership.md`
- `docs/modular-monolith/issue-380-project-reference-graph.md`
- `docs/modular-monolith/issue-381-notifications-boundary.md`
- `docs/modular-monolith/issue-390-nutrition-boundary.md`
