# Issue #375: Architecture Baseline and Dependency Inventory

This report captures the current LGYM API baseline for issue #375. It is doc only. It does not change production behavior, schema, project references, migrations, or public API contracts.

## Scope and non-scope

| In scope | Out of scope |
| --- | --- |
| Record the current solution baseline, graph, hotspots, guards, and cross-feature dependencies. | Refactors, file moves, module extraction, or cleanup passes. |
| Preserve the current state as evidence for epic #311 planning. | Any behavior change, schema change, migration change, or API contract drift. |
| Separate verified failures from environmental blockers. | Reclassifying pre-existing warnings as regressions. |

## Baseline SHA and toolchain

| Item | Value | Evidence |
| --- | --- | --- |
| Baseline target | `origin/main` at `5cdef880395d6c991b4ea9cb9d7a3b914317e0ae` | `.sisyphus/evidence/task-1-failure-matrix.md` |
| Branch HEAD during capture | `d5dc4dc94123d8b4eafe5759d8bac75e7c8e4819` | `.sisyphus/evidence/task-1-failure-matrix.md` |
| SDK | .NET SDK `10.0.102` | `.sisyphus/evidence/task-1-failure-matrix.md` |
| MSBuild | `18.0.7` | `.sisyphus/evidence/task-1-failure-matrix.md` |
| OS | Windows `10.0.26200` | `.sisyphus/evidence/task-1-failure-matrix.md` |

## Command matrix

| Command | Result | Evidence |
| --- | --- | --- |
| `git rev-parse HEAD` | Passed | `.sisyphus/evidence/task-1-failure-matrix.md` |
| `git rev-parse --verify origin/main` | Passed | `.sisyphus/evidence/task-1-failure-matrix.md` |
| `git merge-base HEAD origin/main` | Passed | `.sisyphus/evidence/task-1-failure-matrix.md` |
| `dotnet --info` | Passed | `.sisyphus/evidence/task-1-failure-matrix.md` |
| `dotnet restore LgymApi.sln` | Passed with warnings | `.sisyphus/evidence/task-1-failure-matrix.md` |
| `dotnet build LgymApi.sln --configuration Release --no-restore` | Passed with warnings | `.sisyphus/evidence/task-1-failure-matrix.md` |
| `dotnet test LgymApi.UnitTests/LgymApi.UnitTests.csproj --configuration Release --no-build` | Passed | `.sisyphus/evidence/task-1-failure-matrix.md` |
| `dotnet test LgymApi.ArchitectureTests/LgymApi.ArchitectureTests.csproj --configuration Release --no-build` | Passed | `.sisyphus/evidence/task-1-failure-matrix.md` |
| `dotnet test LgymApi.IntegrationTests/LgymApi.IntegrationTests.csproj --configuration Release --no-build` | Did not complete in the timeout window | `.sisyphus/evidence/task-1-failure-matrix.md` |
| `dotnet test LgymApi.DataSeeder.Tests/LgymApi.DataSeeder.Tests.csproj --configuration Release --no-build` | Passed | `.sisyphus/evidence/task-1-failure-matrix.md` |

## Failure matrix

| Area | Classification | Evidence | Notes |
| --- | --- | --- | --- |
| Restore and Release build warnings | Pre-existing | `.sisyphus/evidence/task-1-failure-matrix.md` | Existing `NU1903` advisory and nullable or duplicate-using warnings were present before this baseline pass. |
| Integration tests | Environmental/flaky | `.sisyphus/evidence/task-1-failure-matrix.md` | The run repeatedly logged `Elastic.Serilog.Sinks.ElasticsearchSink: Failure to export events over to Elasticsearch` and did not reach a final VSTest summary before timeout. |

No new regression is claimed here. The only non-green command is classified as environmental/flaky because the evidence never produced a completed pass or fail summary.

## Project-reference graph summary

The issue #375 snapshot had 14 tracked projects. `LgymApi.Resources.Generator` was the only project with zero outgoing `ProjectReference` edges.

The companion graph doc contains the full historical edge list and Mermaid graph, derived only from tracked `.csproj` references. The current graph is published in `docs/modular-monolith/issue-380-project-reference-graph.md`.

- Companion file: `docs/modular-monolith/issue-375-project-reference-graph.md`
- Evidence: `.sisyphus/evidence/task-2-project-reference-edges.md`

Key hubs from the current graph are `LgymApi.Api`, `LgymApi.Infrastructure`, `LgymApi.TestUtils`, and `LgymApi.Application`. `LgymApi.Api` fans into application, infrastructure, background worker, domain, and resources. `LgymApi.Infrastructure` fans into application, background worker common, and domain. `LgymApi.TestUtils` fans into five projects, which makes it a high-leverage test-only dependency node.

## Hotspot inventory

| Path | Risk | Coupling type | Evidence | Recommended owner module |
| --- | --- | --- | --- | --- |
| `LgymApi.Api/Program.cs` | Densest composition root. It wires API, application, infrastructure, background worker, auth, validation, mapping, CORS, localization, rate limiting, and SignalR in one place. | Cross-project startup composition | `.sisyphus/evidence/task-3-hotspot-validation.md`, `.sisyphus/evidence/task-5-dependency-proof.md` | Platform / Reference Data |
| `LgymApi.Application/ServiceCollectionExtensions.cs` | Main application DI fan-in. A change here can affect many feature services at once. | Service registration fan-in | `.sisyphus/evidence/task-3-hotspot-validation.md`, `.sisyphus/evidence/task-5-dependency-proof.md` | Platform / Reference Data |
| `LgymApi.Infrastructure/ServiceCollectionExtensions.cs` | Main technical wiring root. It binds repositories, UoW, storage, Hangfire, and external auth. | Infrastructure composition fan-in | `.sisyphus/evidence/task-3-hotspot-validation.md`, `.sisyphus/evidence/task-5-dependency-proof.md` | Platform / Reference Data |
| `LgymApi.Infrastructure/Data/AppDbContext.cs` | Shared EF root for users, plans, trainings, reporting, photos, and push data. | Shared persistence surface | `.sisyphus/evidence/task-3-hotspot-validation.md`, `.sisyphus/evidence/task-5-dependency-proof.md` | Platform / Reference Data |
| `LgymApi.Application/User/UserService*.cs` and `LgymApi.Application/User/IUserServiceDependencies.cs` | Broad feature service with a dependency bag and partial-class split. It owns auth, profile, ranking, session, and onboarding flows. | Feature service breadth and dependency aggregation | `.sisyphus/evidence/task-3-hotspot-validation.md`, `.sisyphus/evidence/task-5-dependency-proof.md` | Identity & Accounts |
| `LgymApi.BackgroundWorker/ServiceProvider.cs` | Broad orchestration root for schedulers, jobs, handlers, and cross-project registrations. | Background orchestration fan-in | `.sisyphus/evidence/task-3-hotspot-validation.md` | Platform / Reference Data |

## Architecture-guard inventory

Current snapshot from the architecture test project: 48 guard test classes and 53 test methods.

### Baseline guard coverage

| Concern | Guard tests | What they enforce | Evidence |
| --- | --- | --- | --- |
| Unit of work and repository commit placement | `UnitOfWorkCommitGuardTests`, `RepositoryUnitOfWorkGuardTests`, `ServiceTransactionHeuristicGuardTests` | Services own commits and transactions, repositories do not, and multi-write service methods need a commit boundary or an allowlist entry. | `.sisyphus/evidence/task-4-guard-summary-validation.md` |
| Service registration and composition root wiring | `ServiceRegistrationGuardTests`, `InfrastructureServiceRegistrationGuardTests`, `RepositoryRegistrationGuardTests`, `CrossBoundaryRegistrationGuardTests`, `CompositionRootRegistrationGuardTests`, `MiddlewareRegistrationGuardTests`, `UnitConverterRegistrationGuardTests` | Application services register in application DI, infrastructure services and repositories register in infrastructure DI, infrastructure cannot register application implementations, and `Program.cs` must call the required composition methods and middleware. | `.sisyphus/evidence/task-4-guard-summary-validation.md` |
| Typed-ID and Guid boundaries | `ApiContractTypedIdGuardTests`, `ApplicationInputModelStringIdGuardTests`, `EntityIdLeakageGuardTests`, `DirectGuidUsageGuardTests`, `StrictGuidBanGuardTests`, `RepositoryTypedIdOrderingGuardTests` | API contracts avoid typed ID value objects, application input models avoid raw string IDs, production code does not leak raw `Guid` IDs, and repositories preserve typed-ID ordering. | `.sisyphus/evidence/task-4-guard-summary-validation.md` |
| Feature and folder placement | `FeatureFolderStructureGuardTests`, `FeatureLocationExclusivityGuardTests`, `BackgroundWorkerLocationGuardTests` | Controllers, contracts, and validators stay in the expected folders, and background worker implementations stay in the background worker project. | `.sisyphus/evidence/task-4-guard-summary-validation.md` |
| API, validation, and contract shape | `ContractsDtoGuardTests`, `ControllerActionCancellationTokenGuardTests`, `ControllerDependencyInjectionGuardTests`, `ControllerDtoConstructionGuardTests`, `ControllerProducesResponseTypeDtoGuardTests`, `ControllerProducesResponseTypeResultDtoGuardTests`, `ControllersInheritanceGuardTests`, `HttpContextRequestAbortedBanGuardTests`, `LegacyContractShapeGuardTests`, `ValidationInheritanceGuardTests`, `ValidationMessageResourceGuardTests`, `ResultDtoCoverageTests`, `SerializationOptionsGuardTests` | DTO shape, controller shape, localization, response contract shape, and shared serialization rules stay consistent. | `.sisyphus/evidence/task-4-guard-summary-validation.md` |
| Domain, enum, resource, and packaging rules | `EnumEvolutionGuardTests`, `ResourceKeysCoverageGuardTests`, `CentralPackageManagementGuardTests`, `FileLengthGuardTests` | Enum evolution stays explicit, resource keys stay complete, package versions stay centralized, and production file length stays bounded. | `.sisyphus/evidence/task-4-guard-summary-validation.md` |
| Data seeding, mapping, and repository hygiene | `DataSeederToolingGuardTests`, `EntitySeedCoverageGuardTests`, `EnumStorageConventionGuardTests`, `MapperRegistrationGuardTests`, `RepositoryTypedIdOrderingGuardTests` | Seeder tooling stays wired, entities have seed coverage, enum storage uses string conversion, mappings are declared, and repository ordering respects typed IDs. | `.sisyphus/evidence/task-4-guard-summary-validation.md` |

### Explicit exclusions and allowlists

| Guard test | Explicit exception or allowlist detail | Evidence |
| --- | --- | --- |
| `UnitOfWorkCommitGuardTests` | Contains allowed segments, test segments, and file suffix allowlist entries for the current design. | `.sisyphus/evidence/task-4-guard-summary-validation.md` |
| `ServiceRegistrationGuardTests` | Encodes its own exceptions for expected registrations instead of relying on a shared allowlist file. | `.sisyphus/evidence/task-4-guard-summary-validation.md` |
| `ServiceTransactionHeuristicGuardTests` | Has an allowlist hook, but the current list is empty. | `.sisyphus/evidence/task-4-guard-summary-validation.md` |
| `FileLengthGuardTests` | Uses file-specific exclusions as part of the guard design. | `.sisyphus/evidence/task-4-guard-summary-validation.md` |
| `EntityIdLeakageGuardTests` | Uses hard-coded path or identifier exclusions. | `.sisyphus/evidence/task-4-guard-summary-validation.md` |
| `ApplicationInputModelStringIdGuardTests` | Contains explicit carve-outs for the current boundary model. | `.sisyphus/evidence/task-4-guard-summary-validation.md` |
| `StrictGuidBanGuardTests` | Encodes hard-coded exclusions for generated files and the internal ID implementation. | `.sisyphus/evidence/task-4-guard-summary-validation.md` |
| `DirectGuidUsageGuardTests` | Contains explicit carve-outs for the internal typed-ID implementation. | `.sisyphus/evidence/task-4-guard-summary-validation.md` |
| `BackgroundWorkerLocationGuardTests` | Uses project-scope checks rather than a shared exception list. | `.sisyphus/evidence/task-4-guard-summary-validation.md` |
| `FeatureLocationExclusivityGuardTests` | Applies location rules with explicit scope boundaries for contracts, controllers, and validators. | `.sisyphus/evidence/task-4-guard-summary-validation.md` |
| `SerializationOptionsGuardTests` | Verifies shared serialization rules and treats existing regression baselines as part of the guard design. | `.sisyphus/evidence/task-4-guard-summary-validation.md` |
| `CancellationTokenPropagationGuardTests` | Keeps its own service and controller propagation rules with localized exceptions. | `.sisyphus/evidence/task-4-guard-summary-validation.md` |

The guard inventory is intentionally separated from any future baseline wish list. Existing guards and their explicit exceptions are both part of the current state.

## Cross-feature dependency inventory

Current-state only. Recommendation values use epic #311 bounded-context names.

| Source | Target | Dependency type | Why it exists | Evidence | Recommendation |
| --- | --- | --- | --- | --- | --- |
| `LgymApi.Api/Program.cs` | `Application`, `Infrastructure`, `BackgroundWorker`, `Notifications` composition roots | composition-root registration | The host wires the application, infrastructure, notifications module, and worker module into the runtime at startup. | `LgymApi.Api/Program.cs:54-117` | Platform / Reference Data |
| `LgymApi.Application/ServiceCollectionExtensions.cs` | application feature services | DI registration | The application root registers the current feature services and helper services for user, notifications, reporting, plan, training, and trainer relationships. | `LgymApi.Application/ServiceCollectionExtensions.cs:35-89` | Platform / Reference Data |
| `LgymApi.Infrastructure/ServiceCollectionExtensions.cs` | concrete repositories, `AppDbContext`, UoW, storage, Hangfire, external auth | DI registration + infrastructure wiring | The infrastructure root binds every repository and technical implementation that the application layer consumes. | `LgymApi.Infrastructure/ServiceCollectionExtensions.cs:31-179` | Platform / Reference Data |
| `LgymApi.Application/User/UserService.cs` + `IUserServiceDependencies.cs` | `IPushInstallationRepository` | service-to-repository | User login/logout and device registration manage push-installation ownership and cleanup. | `LgymApi.Application/User/UserService.cs:15-43`; `LgymApi.Application/User/IUserServiceDependencies.cs:11-25,28-75` | Notifications |
| `LgymApi.Application/User/UserService.cs` + `IUserServiceDependencies.cs` | `IUserRepository`, `IRoleRepository`, `IEloRegistryRepository`, `ITokenService`, `ILegacyPasswordService`, `IRankService`, `IUserSessionStore`, `ITutorialService`, `ICommandDispatcher`, `IUnitOfWork` | service-to-repository/service | The current user service owns auth, profile, ranking, session, and onboarding workflows in one place. | `LgymApi.Application/User/UserService.cs:15-43`; `LgymApi.Application/User/IUserServiceDependencies.cs:11-25,28-75` | Identity & Accounts |
| `LgymApi.Application/Notifications/InAppNotificationService.cs` + `IInAppNotificationServiceDependencies.cs` | `IInAppNotificationRepository`, `IUnitOfWork`, `IInAppNotificationPushPublisher`, `INotificationEventBridge` | service-to-repository/service + event bridge | In-app notifications are persisted first, then fanned out to push and background delivery. | `LgymApi.Application/Notifications/InAppNotificationService.cs:16-85`; `LgymApi.Application/Notifications/IInAppNotificationServiceDependencies.cs:5-31`; `LgymApi.Application/Notifications/ServiceCollectionExtensions.cs:5-11` | Notifications |
| `LgymApi.Application/Notifications/PushNotificationService.cs` | `IPushInstallationRepository`, `IPushNotificationMessageRepository`, `IPushBackgroundScheduler`, `IUnitOfWork` | service-to-repository/service | Push enqueueing deduplicates by installation, persists message state, and hands work to the background scheduler. | `LgymApi.Application/Notifications/PushNotificationService.cs:23-160` | Notifications |
| `LgymApi.Application/Notifications/StalePushInstallationCleanupService.cs` | `IPushInstallationRepository`, `IUnitOfWork` | service-to-repository | Stale-installation cleanup marks inactive devices without deleting audit history. | `LgymApi.Application/Notifications/StalePushInstallationCleanupService.cs:1-46` | Notifications |
| `LgymApi.Application/Features/Reporting/ReportingService.cs` + `IReportingServiceDependencies.cs` | `IRoleRepository`, `ITrainerRelationshipRepository`, `IReportingRepository`, `IRecurringReportAssignmentRepository`, `IReportSubmissionMeasurementWriter`, `IPhotoStorageProvider`, `IPhotoUploadInitTracker`, `ICommandDispatcher`, `IUnitOfWork` | service-to-repository/service | Reporting enforces trainer ownership, manages templates, requests, submissions, and emits background commands. | `LgymApi.Application/Features/Reporting/ReportingService.cs:22-96`; `LgymApi.Application/Features/Reporting/IReportingServiceDependencies.cs:9-63` | Reporting |
| `LgymApi.Application/Features/Reporting/RecurringReportAssignmentService.cs` + `IRecurringReportAssignmentServiceDependencies.cs` | `IRoleRepository`, `ITrainerRelationshipRepository`, `IReportingRepository`, `IRecurringReportAssignmentRepository`, `ICommandDispatcher`, `IUnitOfWork` | service-to-repository/service | Recurring assignment processing spans trainer ownership, report requests, and command dispatch. | `LgymApi.Application/Features/Reporting/RecurringReportAssignmentService.cs:18-200`; `LgymApi.Application/Features/Reporting/IRecurringReportAssignmentServiceDependencies.cs:6-40` | Reporting |
| `LgymApi.Application/Plan/PlanService.cs` | `IUserRepository`, `IPlanRepository`, `IPlanDayRepository`, `IUnitOfWork` | service-to-repository | Plan management depends on the owning user plus subordinate plan-day rows. | `LgymApi.Application/Plan/PlanService.cs:7-21` | Training Planning |
| `LgymApi.Application/Training/TrainingService.cs` + `ITrainingServiceDependencies.cs` | `IUserRepository`, `IGymRepository`, `ITrainingRepository`, `IExerciseRepository`, `IExerciseScoreRepository`, `ITrainingExerciseScoreRepository`, `IEloRegistryRepository`, `IRankService`, `ICommandDispatcher`, `IUnitOfWork`, `IExerciseEloCalculator` set | service-to-repository/service | Workout creation and scoring span user, gym, exercise, ranking, ELO, and command orchestration. | `LgymApi.Application/Training/TrainingService.cs:10-37`; `LgymApi.Application/Training/ITrainingServiceDependencies.cs:9-63` | Workout & Progress |
| `LgymApi.Application/TrainerRelationships/TrainerRelationshipService.cs` + `ITrainerRelationshipServiceDependencies.cs` | `IUserRepository`, `IRoleRepository`, `ITrainerRelationshipRepository`, `IPlanRepository`, `ITrainingService`, `IExerciseScoresService`, `IEloRegistryService`, `IMainRecordsService`, `IUnitOfWork` | service-to-repository/service | Trainer and trainee linking fans out into plans, workouts, scores, ELO, and main records. | `LgymApi.Application/TrainerRelationships/TrainerRelationshipService.cs:20-47`; `LgymApi.Application/TrainerRelationships/ITrainerRelationshipServiceDependencies.cs:12-70` | Coaching |
| `LgymApi.Infrastructure/Data/AppDbContext.cs` | `Users`, `Plans`, `Trainings`, `TrainerInvitations`, `TrainerTraineeLinks`, `ReportTemplates`, `ReportRequests`, `ReportSubmissions`, `Photos`, `PushInstallations`, `PushNotificationMessages` | shared persistence root | One EF Core context currently hosts the cross-feature entity surface used by user, plan, training, coaching, reporting, and notifications code. | `LgymApi.Infrastructure/Data/AppDbContext.cs:24-71,80-260` | Platform / Reference Data |
| `LgymApi.Infrastructure/Repositories/UserRepository.cs` | `AppDbContext.Users`, `AppDbContext.EloRegistries`, `RoleSeedDataConfiguration.TesterRoleSeedId` | repository-to-DbContext/shared seed data | User ranking and lookup are backed by shared persistence and seed-data rules. | `LgymApi.Infrastructure/Repositories/UserRepository.cs:16-92` | Identity & Accounts |
| `LgymApi.Infrastructure/Repositories/PlanRepository.cs` | `AppDbContext.Plans` | repository-to-DbContext | Plan queries, cloning, and share-code generation all touch the shared `Plans` set. | `LgymApi.Infrastructure/Repositories/PlanRepository.cs:28-183` | Training Planning |
| `LgymApi.Infrastructure/Repositories/TrainingRepository.cs` | `AppDbContext.Trainings` | repository-to-DbContext | Workout history and date queries are rooted in shared training persistence. | `LgymApi.Infrastructure/Repositories/TrainingRepository.cs:11-76` | Workout & Progress |
| `LgymApi.Infrastructure/Repositories/TrainerRelationshipRepository.cs` | `AppDbContext.TrainerInvitations`, `TrainerTraineeLinks`, `Users`, `IGridifyExecutionService`, `IMapperRegistry` | repository-to-DbContext/shared infra | Trainer dashboard and invitation queries span multiple shared tables plus pagination and mapping infrastructure. | `LgymApi.Infrastructure/Repositories/TrainerRelationshipRepository.cs:15-175` | Coaching |
| `LgymApi.Infrastructure/Repositories/ReportingRepository.cs` | `AppDbContext.ReportTemplates`, `ReportRequests`, `ReportSubmissions`, `Photos` | repository-to-DbContext | Reporting aggregates templates, requests, submissions, and photo metadata in one persistence surface. | `LgymApi.Infrastructure/Repositories/ReportingRepository.cs:12-190` | Reporting |
| `LgymApi.Infrastructure/Repositories/PushInstallationRepository.cs` | `AppDbContext.PushInstallations` | repository-to-DbContext | Push registration and stale cleanup operate on the shared installation set. | `LgymApi.Infrastructure/Repositories/PushInstallationRepository.cs:11-75` | Notifications |
| `LgymApi.Infrastructure/Repositories/PushNotificationMessageRepository.cs` | `AppDbContext.PushNotificationMessages` | repository-to-DbContext | Push delivery lifecycle and retry claims are persisted through one shared message set. | `LgymApi.Infrastructure/Repositories/PushNotificationMessageRepository.cs:11-74` | Notifications |

## Why this matters for #311 next steps

Issue #375 does not start the modular-monolith split. It gives epic #311 a stable current-state map so later issues can change one bounded context at a time without guessing at today’s coupling.

The useful takeaways are:

- `Platform / Reference Data` owns the shared startup, DI, and persistence roots that keep the current system coherent.
- `Identity & Accounts`, `Notifications`, `Reporting`, `Training Planning`, `Workout & Progress`, and `Coaching` already show the clearest feature-level dependency clusters.
- The current guard set already protects the main boundaries, so future work can tighten boundaries by module without first rediscovering the existing rules.
- The integration-test timeout is documented as environmental/flaky, so later #311 work should not treat this baseline as proof of a product regression.

This report is the factual starting point for the next #311 issues.
