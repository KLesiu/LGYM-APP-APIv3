# Module Contribution Guide

Use this guide when adding a use case or reviewing a module-boundary change. Start with the owner, then change only the seams that owner needs. This is a workflow, not a replacement for the authorities it links to.

## Authority And Precedence

This guide owns the contribution workflow. [ADR-006](adr/006-lgym-evolves-as-modular-monolith.md) owns the architectural direction, and [ADR-007](adr/007-final-modular-monolith-compatibility-commitments.md) owns final compatibility commitments. The compiled ownership catalog and its tested [issue-376 view](modular-monolith/issue-376-ownership-map.md) own persisted ownership. The [issue-380 graph](modular-monolith/issue-380-project-reference-graph.md) owns dependencies, while [issue-380 background ownership](modular-monolith/issue-380-background-contract-ownership.md) owns messaging. [Issue-392](modular-monolith/issue-392-reporting-boundary.md) and [issue-393](modular-monolith/issue-393-platform-reference-data-boundary.md) define their detailed boundaries. [Issue-395 final verification](modular-monolith/issue-395-final-verification.md) owns final disposition and Todo 22 evidence placeholders. Read the adjacent `<ProjectName>.md` before changing a project, and follow `AGENT.md` and `AGENTS.md` for repository behavior.

| Authority ID | Authority source | Governs |
| --- | --- | --- |
| module-guide.authority.workflow | docs/MODULE_CONTRIBUTION_GUIDE.md | workflow |
| module-guide.authority.adr-decision | docs/adr/006-lgym-evolves-as-modular-monolith.md | architectural-decisions |
| module-guide.authority.ownership | LgymApi.ArchitectureTests/PersistedEntityOwnershipCatalog.cs#PersistedEntityOwnershipCatalog.CanonicalOwners | persisted-ownership |
| module-guide.authority.dependency-graph | docs/modular-monolith/issue-380-project-reference-graph.md | dependencies |
| module-guide.authority.background-messaging | docs/modular-monolith/issue-380-background-contract-ownership.md | background-messaging |
| module-guide.authority.reporting-boundary | docs/modular-monolith/issue-392-reporting-boundary.md | reporting-boundary |
| module-guide.authority.platform-provider-boundary | docs/modular-monolith/issue-393-platform-reference-data-boundary.md | platform-provider-boundary |
| module-guide.authority.final-compatibility | docs/adr/007-final-modular-monolith-compatibility-commitments.md | final-compatibility |
| module-guide.authority.final-verification | docs/modular-monolith/issue-395-final-verification.md | final-verification |

## Start With The Owner

The eight canonical owners are `Platform / Reference Data`, `Identity & Accounts`, `Notifications`, `Reporting`, `Training Planning`, `Workout & Progress`, `Coaching`, and `Nutrition`. The first four stable extracted owners are Platform, Identity, Training Planning, and Notifications. Reporting, Workout & Progress, Coaching, and Nutrition remain Application-owned capabilities. The catalog, not this list, decides persisted ownership. In particular, completed `Training` is owned by Workout & Progress, even when it references a Training Planning plan day.

1. Identify the capability and its canonical owner. Don't start from a familiar project or repository.
2. Check the locked graph before proposing a dependency. There are 18 projects and 90 direct edges. A new edge needs a separately approved topology change.
3. If the owner is extracted, use the extracted-owner layout. If it is Application-owned, use the Application-owned layout. If the work crosses owners, consume the owner's focused contract or consumer-owned port.
4. Keep physical files in the owner project. Retained `LgymApi.Application.*` namespaces can remain for source and wire compatibility.
5. Stop when the applicable owner seams are complete. Don't turn a focused change into a global layer sweep.

## Conditional Layouts

### Extracted-Owner Slice

Edit only applicable rows:

| Concern | Place it when needed |
| --- | --- |
| Action contract and immutable model | The owner project public contract surface |
| Use case and domain policy | The owner project's internal vertical slice |
| Persistence port, repository, and EF configuration | The owner project's internal persistence seam and configuration area |
| Registration | The owner's public module facade |
| HTTP exposure | An API adapter, controller, validator, and registered mapping profile |
| Verification | Focused unit, integration, and architecture tests |

Keep the public surface narrow. New contracts are focused, while implementations stay internal. Don't add foreign entities, foreign repositories, a broad dependency aggregate, or a service locator. A retained dependency aggregate or generated partial class is an existing exception, not a model for new work.

### Application-Owned Slice

Edit only applicable rows:

| Concern | Place it when needed |
| --- | --- |
| Use case, model, and owner policy | The owner Application vertical slice |
| Persistence port and owner DI helper | The owner Application slice and helper |
| Stage-only persistence adapter | The matching Infrastructure owner adapter and helper |
| HTTP exposure | An API adapter, controller, validator, and registered mapping profile |
| Verification | Focused unit, integration, and architecture tests |

Infrastructure supplies technical composition and stage-only adapters. It doesn't register Application business services. Keep vertical-slice names owner-local, rather than creating cosmetic partial classes or a feature-specific broad `Common` area.

## Contribution Rules

| Policy ID | Contract | Evidence |
| --- | --- | --- |
| module-guide.policy.owner | canonical-owners=8; completed-training=Workout & Progress; api-persistence=false | Compiled ownership catalog and issue-376 view |
| module-guide.policy.placement | owner-first=true; foreign-entities=false; foreign-repositories=false | Owner project and focused ports |
| module-guide.policy.namespace-compatibility | physical-path=owner; legacy-namespace=compatible | Extracted module compatibility namespaces |
| module-guide.policy.public-surface | focused-contracts=true; dependency-aggregate=false; high-arity=accepted | Public contracts and module facades |
| module-guide.policy.vertical-slice | owner-local=true; cosmetic-partial=false | Owner-local slices |
| module-guide.policy.command-query | query=read; command=write | Use-case intent |
| module-guide.policy.uow-transactions | repository-save=false; one-save=default; transaction=multi-save-only | UoW boundary |
| module-guide.policy.read-models | no-tracking=default; tracked-read=same-uow-mutation-only | Repository projection |
| module-guide.policy.messaging-outbox | reporting=stage; platform=envelope; worker=forward; workout-progress=consume | Accepted-progress flow |
| module-guide.policy.ef-migrations | AppDbContext=1; PostgreSQL database=1; migration stream=1; physical split=None | Shared persistence topology |
| module-guide.policy.di | owner-facade=true; service-locator=false | Module facade registration |
| module-guide.policy.api-compatibility | endpoint-specific=true | Existing endpoint contracts |
| module-guide.policy.api-adapters | application=25; notifications=3; migration-clr-identities=removed | Owner API adapter facades |
| module-guide.policy.localization | resources=en,pl | Resource-backed user text |
| module-guide.policy.tactical-ddd | invariants=required | Aggregate policy where justified |
| module-guide.policy.architecture-tests | focused-guards=true | Relevant Roslyn guards |
| module-guide.policy.prohibited-patterns | ninth-module=false; application-to-worker=false; worker-common-feature-command=false | Locked topology and messaging boundary |

### Commands, Queries, And Unit Of Work

Command and query describe intent. They aren't strict CQRS. A query reads and doesn't save. A command stages changes, then one `SaveChangesAsync` is the default atomic boundary. Repositories never save or start transactions. Use `BeginTransactionAsync` only for multiple saves, intermediate flushes, rollback across those saves, or a verified owner-specific requirement.

Use no-tracking by default; tracking is permitted only when the same unit of work intentionally mutates the loaded entity. Keep an EF projection owner-local when it is a narrow persistence read model. Mapping between layer models and API DTOs still uses registered `IMapper` profiles.

### Persistence, EF, And DI

The production topology is one `AppDbContext`, one PostgreSQL database, and one migration stream. Logical ownership doesn't create a context, schema, database, migration stream, broker, or deployment split. Add a configuration or migration only when an approved owner change needs it, using the existing fixed registrar and shared Infrastructure bridge.

Register extracted workflows through their owner facade. Register remaining Application services in their owner helper, and technical services in Infrastructure. API composition follows the guarded facade order: Platform, Identity, Training Planning, Notifications, remaining Application, Infrastructure, API adapters, then Worker. Application must not reference Worker or Worker.Common.

### API, Localization, And DDD

Compatibility is endpoint-specific. Preserve the existing route, verb, alias, status, DTO, JSON property names, and legacy fields where that endpoint has them. `ContractCompatibilityTests.Register_ReturnsLegacyMsgField`, `Login_ReturnsLegacyReqField`, and `Gym_GetGyms_ReturnsListWithLegacyIdFields` show distinct legacy shapes. The Reporting guard retains `_id` and `msg`, and has no `req` field. API IDs remain strings, cancellation flows through the call chain, and registered mapping profiles handle cross-layer transformation. The final owner handoff has 25 Application and three Notifications API adapters. Do not reintroduce `Task7`, `ApiCompatibility`, or `Compatibility.Task7` CLR adapter identities to preserve an external contract.

Add new user-facing text to English and Polish resources. Don't hardcode messages. Use tactical DDD only when it protects a real invariant. Don't move business policy into controllers, repositories, or generic technical roots.

## Real-Source Skeletons

These are source-bound paths, not copy-paste implementation templates. Their locators name the current compiled members without line numbers.

### Training Planning Read

The list query is physically owned by Training Planning despite compatible Application namespaces. It reads an owner-local no-tracking `PlanReadModel` projection, doesn't save a unit of work, and uses the module facade. Its API adapter preserves the legacy route and maps the result through the API profile.

| Path ID | Step | Canonical owner | Source locator | Verified invariant |
| --- | --- | --- | --- | --- |
| module-guide.path.training-planning-read.controller | 1 | Training Planning | LgymApi.Api/Features/Plan/Controllers/PlanController.cs#PlanController.GetPlansList | Controller calls the compatibility adapter |
| module-guide.path.training-planning-read.compatibility-adapter | 2 | Training Planning | LgymApi.Application/TrainingPlanning/ApiAdapters/PlanApiAdapter.cs#PlanApiAdapter.GetListAsync | Adapter calls the focused owner contract |
| module-guide.path.training-planning-read.use-case-contract | 3 | Training Planning | LgymApi.TrainingPlanning/Plan/GetPlansList/Contracts/IGetPlansListUseCase.cs#IGetPlansListUseCase.ExecuteAsync | Public interface has one use-case method |
| module-guide.path.training-planning-read.use-case | 4 | Training Planning | LgymApi.TrainingPlanning/Plan/GetPlansList/GetPlansListUseCase.cs#GetPlansListUseCase.ExecuteAsync | Internal owner use case reads without saving |
| module-guide.path.training-planning-read.repository-contract | 5 | Training Planning | LgymApi.TrainingPlanning/Persistence/IPlanRepository.cs#IPlanRepository.GetReadModelsByUserIdAsync(Id<User>,CancellationToken) | Contract declares the owner read projection |
| module-guide.path.training-planning-read.repository-projection | 6 | Training Planning | LgymApi.TrainingPlanning/Persistence/Repositories/PlanRepository.cs#PlanRepository.GetReadModelsByUserIdAsync | No-tracking projection creates PlanReadModel |
| module-guide.path.training-planning-read.api-mapping | 7 | Training Planning | LgymApi.Api/Mapping/Profiles/PlanProfile.cs#PlanProfile.Configure | Registered API mapping preserves the response boundary |
| module-guide.path.training-planning-read.module-registration | 8 | Training Planning | LgymApi.TrainingPlanning/TrainingPlanningModule.cs#TrainingPlanningModule.AddTrainingPlanningModule | Facade registers the focused use case and repository |

Training Planning doesn't own completed `Training` rows. Consumer-owned authorization ports avoid a reverse Coaching dependency. Keep its persistence seam bounded to Plans, PlanDays, and PlanDayExercises, with Infrastructure supplying the shared-context bridge.

### Reporting Accepted-Submission Write And Outbox

Reporting owns accepting the submission, validating its measurement payload, staging its two Reporting persistence changes, and staging its accepted-progress command before the one accepted-submission save. Platform owns the same-database envelope and post-commit dispatcher contract. Worker forwards shared JSON. Workout & Progress owns wire validation, deduplication, and Measurement writes. Reporting never writes Measurements and never calls the consumer.

| Path ID | Step | Canonical owner | Source locator | Verified invariant |
| --- | --- | --- | --- | --- |
| module-guide.path.reporting-write.controller | 1 | Reporting | LgymApi.Api/Features/Trainer/Controllers/TraineeReportingController.cs#TraineeReportingController.SubmitRequest | Controller calls the Reporting API port |
| module-guide.path.reporting-write.compatibility-adapter | 2 | Reporting | LgymApi.Application/Reporting/ApiAdapters/ReportTemplateAndRequestApiAdapters.cs#TraineeReportRequestApiAdapter.SubmitAsync | Adapter calls Reporting service ownership |
| module-guide.path.reporting-write.service | 3 | Reporting | LgymApi.Application/Features/Reporting/ReportingService.Submissions.cs#ReportingService.SubmitReportRequestAsync | Accepted slice stages persistence before outbox, save, and dispatch |
| module-guide.path.reporting-write.persistence-add | 4 | Reporting | LgymApi.Application/Reporting/Persistence/IReportRequestSubmissionPersistence.cs#IReportRequestSubmissionPersistence.AddSubmissionAsync | Submission write is stage-only |
| module-guide.path.reporting-write.persistence-status | 5 | Reporting | LgymApi.Application/Reporting/Persistence/IReportRequestSubmissionPersistence.cs#IReportRequestSubmissionPersistence.SetRequestSubmittedAsync | Request status write is stage-only |
| module-guide.path.reporting-write.outbox-stage | 6 | Platform / Reference Data | LgymApi.Platform/Contracts/BackgroundCommands/ICommandOutboxWriter.cs#ICommandOutboxWriter.StageAsync | Command envelope stages before the accepted-submission save |
| module-guide.path.reporting-write.unit-of-work | 7 | Platform / Reference Data | LgymApi.Platform/Repositories/IUnitOfWork.cs#IUnitOfWork.SaveChangesAsync | One save commits the accepted-submission operation slice |
| module-guide.path.reporting-write.post-commit-dispatch | 8 | Platform / Reference Data | LgymApi.Platform/Contracts/BackgroundCommands/ICommandDispatcher.cs#ICommandDispatcher.EnqueueAsync | Dispatcher runs after commit |
| module-guide.path.reporting-write.worker-handler | 9 | Platform / Reference Data | LgymApi.BackgroundWorker/Actions/ReportSubmissionAcceptedProgressCommandHandler.cs#ReportSubmissionAcceptedProgressCommandHandler.ExecuteAsync | Worker forwards shared JSON to the action port |
| module-guide.path.reporting-write.workout-action-port | 10 | Workout & Progress | LgymApi.Application/WorkoutProgress/BackgroundActions/ReportSubmissionAcceptedProgressActionExecutionPort.cs#ReportSubmissionAcceptedProgressActionExecutionPort.ExecuteAsync | Workout-owned port accepts the delivery |
| module-guide.path.reporting-write.workout-consumer | 11 | Workout & Progress | LgymApi.Application/WorkoutProgress/ReportingIntegration/ReportSubmissionAcceptedProgressConsumer.cs#ReportSubmissionAcceptedProgressConsumer.ConsumeAsync | Consumer validates, deduplicates, stages, and commits missing measurements |

The accepted-submission operation is the slice beginning with the Reporting persistence staging and ending after post-commit dispatch. An earlier expiration-branch save belongs to a different branch and doesn't redefine this accepted outbox flow.

## Exceptions And Prohibited Patterns

| Exception ID | Scope | Constraint |
| --- | --- | --- |
| module-guide.exception.endpoint-specific-legacy-fields | endpoint-specific | legacy-fields=route-contract-only |
| module-guide.exception.tracked-mutation-reads | same-uow-mutation | tracking=same-uow-mutation-only |
| module-guide.exception.direct-high-arity-constructors | focused-service | dependency-aggregate=false; numeric-cap=none |
| module-guide.exception.retained-generated-partial-classes | retained-or-generated | new-cosmetic-partial=false |
| module-guide.exception.legacy-namespaces-command-ids | wire-compatibility | identity=preserve |
| module-guide.exception.query-side-compatibility-cleanup | compatibility-adapter-only | owner-path=unchanged |
| module-guide.exception.shared-physical-persistence | single-context-shared-database | AppDbContext=1; migration stream=1 |

Don't add a ninth module or project, a new project-reference edge, a second production context, a schema per module, a database per module, or a separate migration stream. Don't use foreign entities or repositories, direct Worker dependencies, Worker.Common feature commands, provider leakage, manual controller or service cross-layer mapping, hardcoded messages, a global hotspot workflow, service location, dependency aggregates, or unapproved debt exemptions.

## Contributor And Verification Checklist

1. Name the owner and the evidence for every boundary decision.
2. Use the applicable conditional layout and leave unrelated layers untouched.
3. Confirm dependencies against issue-380 and public contracts against the owner facade.
4. Preserve endpoint-specific compatibility, API string IDs, mapper registration, cancellation, and English and Polish resources.
5. Add focused tests for behavior, persistence/UoW boundaries, API contracts, and architecture rules that apply.
6. Run the narrowest relevant test first, then required regression checks. Record the command and parsed TRX result for documentation-contract work.
