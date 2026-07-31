# Issue #380: Approved Current Project-Reference Graph

This is the authoritative current graph after the completed issue #387 extraction. It is derived only from tracked `.csproj` `ProjectReference` items. Namespace imports, runtime registration, and transitive build dependencies are not graph edges.

## Solution projects

1. `LgymApi.Api`
2. `LgymApi.Application`
3. `LgymApi.ArchitectureTests`
4. `LgymApi.BackgroundWorker`
5. `LgymApi.BackgroundWorker.Common`
6. `LgymApi.DataSeeder`
7. `LgymApi.DataSeeder.Tests`
8. `LgymApi.Domain`
9. `LgymApi.Identity`
10. `LgymApi.Infrastructure`
11. `LgymApi.IntegrationTests`
12. `LgymApi.Notifications`
13. `LgymApi.Platform`
14. `LgymApi.Resources`
15. `LgymApi.Resources.Generator`
16. `LgymApi.TestUtils`
17. `LgymApi.TrainingPlanning`
18. `LgymApi.UnitTests`

## Canonical direct edge manifest

Sources and targets are alphabetical. Every item is one direct `ProjectReference` edge.

- `LgymApi.Api` -> `LgymApi.Application`
- `LgymApi.Api` -> `LgymApi.BackgroundWorker`
- `LgymApi.Api` -> `LgymApi.Domain`
- `LgymApi.Api` -> `LgymApi.Identity`
- `LgymApi.Api` -> `LgymApi.Infrastructure`
- `LgymApi.Api` -> `LgymApi.Notifications`
- `LgymApi.Api` -> `LgymApi.Platform`
- `LgymApi.Api` -> `LgymApi.Resources`
- `LgymApi.Api` -> `LgymApi.TrainingPlanning`
- `LgymApi.Application` -> `LgymApi.Domain`
- `LgymApi.Application` -> `LgymApi.Identity`
- `LgymApi.Application` -> `LgymApi.Platform`
- `LgymApi.Application` -> `LgymApi.Resources`
- `LgymApi.Application` -> `LgymApi.TrainingPlanning`
- `LgymApi.ArchitectureTests` -> `LgymApi.Api`
- `LgymApi.ArchitectureTests` -> `LgymApi.Application`
- `LgymApi.ArchitectureTests` -> `LgymApi.BackgroundWorker.Common`
- `LgymApi.ArchitectureTests` -> `LgymApi.Domain`
- `LgymApi.ArchitectureTests` -> `LgymApi.Identity`
- `LgymApi.ArchitectureTests` -> `LgymApi.Infrastructure`
- `LgymApi.ArchitectureTests` -> `LgymApi.Notifications`
- `LgymApi.ArchitectureTests` -> `LgymApi.Platform`
- `LgymApi.ArchitectureTests` -> `LgymApi.Resources`
- `LgymApi.ArchitectureTests` -> `LgymApi.TrainingPlanning`
- `LgymApi.BackgroundWorker` -> `LgymApi.Application`
- `LgymApi.BackgroundWorker` -> `LgymApi.BackgroundWorker.Common`
- `LgymApi.BackgroundWorker` -> `LgymApi.Identity`
- `LgymApi.BackgroundWorker` -> `LgymApi.Infrastructure`
- `LgymApi.BackgroundWorker` -> `LgymApi.Notifications`
- `LgymApi.BackgroundWorker` -> `LgymApi.Platform`
- `LgymApi.BackgroundWorker.Common` -> `LgymApi.Domain`
- `LgymApi.DataSeeder` -> `LgymApi.Domain`
- `LgymApi.DataSeeder` -> `LgymApi.Identity`
- `LgymApi.DataSeeder` -> `LgymApi.Infrastructure`
- `LgymApi.DataSeeder.Tests` -> `LgymApi.DataSeeder`
- `LgymApi.DataSeeder.Tests` -> `LgymApi.Domain`
- `LgymApi.DataSeeder.Tests` -> `LgymApi.Identity`
- `LgymApi.DataSeeder.Tests` -> `LgymApi.Infrastructure`
- `LgymApi.Identity` -> `LgymApi.Domain`
- `LgymApi.Identity` -> `LgymApi.Platform`
- `LgymApi.Identity` -> `LgymApi.Resources`
- `LgymApi.Infrastructure` -> `LgymApi.Application`
- `LgymApi.Infrastructure` -> `LgymApi.BackgroundWorker.Common`
- `LgymApi.Infrastructure` -> `LgymApi.Domain`
- `LgymApi.Infrastructure` -> `LgymApi.Identity`
- `LgymApi.Infrastructure` -> `LgymApi.Notifications`
- `LgymApi.Infrastructure` -> `LgymApi.Platform`
- `LgymApi.Infrastructure` -> `LgymApi.TrainingPlanning`
- `LgymApi.IntegrationTests` -> `LgymApi.Api`
- `LgymApi.IntegrationTests` -> `LgymApi.Application`
- `LgymApi.IntegrationTests` -> `LgymApi.BackgroundWorker`
- `LgymApi.IntegrationTests` -> `LgymApi.BackgroundWorker.Common`
- `LgymApi.IntegrationTests` -> `LgymApi.Domain`
- `LgymApi.IntegrationTests` -> `LgymApi.Identity`
- `LgymApi.IntegrationTests` -> `LgymApi.Infrastructure`
- `LgymApi.IntegrationTests` -> `LgymApi.Notifications`
- `LgymApi.IntegrationTests` -> `LgymApi.Platform`
- `LgymApi.IntegrationTests` -> `LgymApi.Resources`
- `LgymApi.IntegrationTests` -> `LgymApi.TestUtils`
- `LgymApi.IntegrationTests` -> `LgymApi.TrainingPlanning`
- `LgymApi.Notifications` -> `LgymApi.BackgroundWorker.Common`
- `LgymApi.Notifications` -> `LgymApi.Domain`
- `LgymApi.Notifications` -> `LgymApi.Identity`
- `LgymApi.Notifications` -> `LgymApi.Platform`
- `LgymApi.Notifications` -> `LgymApi.Resources`
- `LgymApi.Platform` -> `LgymApi.Domain`
- `LgymApi.Platform` -> `LgymApi.Resources`
- `LgymApi.Resources` -> `LgymApi.Resources.Generator`
- `LgymApi.TestUtils` -> `LgymApi.Application`
- `LgymApi.TestUtils` -> `LgymApi.BackgroundWorker`
- `LgymApi.TestUtils` -> `LgymApi.BackgroundWorker.Common`
- `LgymApi.TestUtils` -> `LgymApi.Domain`
- `LgymApi.TestUtils` -> `LgymApi.Identity`
- `LgymApi.TestUtils` -> `LgymApi.Infrastructure`
- `LgymApi.TrainingPlanning` -> `LgymApi.Domain`
- `LgymApi.TrainingPlanning` -> `LgymApi.Identity`
- `LgymApi.TrainingPlanning` -> `LgymApi.Platform`
- `LgymApi.TrainingPlanning` -> `LgymApi.Resources`
- `LgymApi.UnitTests` -> `LgymApi.Api`
- `LgymApi.UnitTests` -> `LgymApi.Application`
- `LgymApi.UnitTests` -> `LgymApi.BackgroundWorker`
- `LgymApi.UnitTests` -> `LgymApi.BackgroundWorker.Common`
- `LgymApi.UnitTests` -> `LgymApi.Domain`
- `LgymApi.UnitTests` -> `LgymApi.Identity`
- `LgymApi.UnitTests` -> `LgymApi.Infrastructure`
- `LgymApi.UnitTests` -> `LgymApi.Notifications`
- `LgymApi.UnitTests` -> `LgymApi.Platform`
- `LgymApi.UnitTests` -> `LgymApi.Resources`
- `LgymApi.UnitTests` -> `LgymApi.TestUtils`
- `LgymApi.UnitTests` -> `LgymApi.TrainingPlanning`

## Direct-use evidence

Each row is one representative source location whose symbol resolves to the target assembly. The Resources Generator row is the sole analyzer edge and is justified by its analyzer-configured `ProjectReference`.

| Edge | Roslyn-resolved source or analyzer evidence |
| --- | --- |
| `LgymApi.Api -> LgymApi.Application` | `LgymApi.Api/Features/Account/Controllers/AccountController.cs:19` |
| `LgymApi.Api -> LgymApi.BackgroundWorker` | `LgymApi.Api/Program.Hangfire.cs:21` |
| `LgymApi.Api -> LgymApi.Domain` | `LgymApi.Api/Configuration/ApiAuthorizationExtensions.cs:1` |
| `LgymApi.Api -> LgymApi.Identity` | `LgymApi.Api/Features/Account/Controllers/AccountController.cs:8` |
| `LgymApi.Api -> LgymApi.Infrastructure` | `LgymApi.Api/Configuration/LocalPhotoDevelopmentEndpoints.cs:20` |
| `LgymApi.Api -> LgymApi.Notifications` | `LgymApi.Api/Features/InAppNotification/Controllers/InAppNotificationController.cs:6` |
| `LgymApi.Api -> LgymApi.Platform` | `LgymApi.Api/Extensions/ApiJsonOptionsExtensions.cs:3` |
| `LgymApi.Api -> LgymApi.Resources` | `LgymApi.Api/Configuration/ApiAuthenticationExtensions.cs:49` |
| `LgymApi.Api -> LgymApi.TrainingPlanning` | `LgymApi.Api/Features/PlanDay/Controllers/PlanDayController.cs:9` |
| `LgymApi.Application -> LgymApi.Domain` | `LgymApi.Application/Coaching/Access/CoachingRelationshipAccessService.cs:4` |
| `LgymApi.Application -> LgymApi.Identity` | `LgymApi.Application/Coaching/Access/CoachingRelationshipAccessService.cs:3` |
| `LgymApi.Application -> LgymApi.Platform` | `LgymApi.Application/Coaching/ApiAdapters/CoachingApiAdapterMappingProfile.cs:24` |
| `LgymApi.Application -> LgymApi.Resources` | `LgymApi.Application/Coaching/Invitations/Accept/AcceptInvitationUseCase.cs:48` |
| `LgymApi.Application -> LgymApi.TrainingPlanning` | `LgymApi.Application/Coaching/Adapters/PlanDayRelationshipAccessAdapter.cs:2` |
| `LgymApi.ArchitectureTests -> LgymApi.Api` | `LgymApi.ArchitectureTests/CoachingApiContractImmutabilityGuardTests.cs:2` |
| `LgymApi.ArchitectureTests -> LgymApi.Application` | `LgymApi.ArchitectureTests/CanonicalRepositoryRegistrationDiTests.cs:41` |
| `LgymApi.ArchitectureTests -> LgymApi.BackgroundWorker.Common` | `LgymApi.ArchitectureTests/ProjectReferenceGraphGuardTests.cs:1` |
| `LgymApi.ArchitectureTests -> LgymApi.Domain` | `LgymApi.ArchitectureTests/AppConfigAuthorizationBoundaryGuardTests.cs:2` |
| `LgymApi.ArchitectureTests -> LgymApi.Identity` | `LgymApi.ArchitectureTests/CoachingManagedPlanSliceArchitectureTests.cs:13` |
| `LgymApi.ArchitectureTests -> LgymApi.Infrastructure` | `LgymApi.ArchitectureTests/CanonicalRepositoryRegistrationDiTests.cs:37` |
| `LgymApi.ArchitectureTests -> LgymApi.Notifications` | `LgymApi.ArchitectureTests/CanonicalRepositoryRegistrationDiTests.cs:1` |
| `LgymApi.ArchitectureTests -> LgymApi.Platform` | `LgymApi.ArchitectureTests/AppConfigAuthorizationBoundaryGuardTests.cs:1` |
| `LgymApi.ArchitectureTests -> LgymApi.Resources` | `LgymApi.ArchitectureTests/ProjectReferenceGraphGuardTests.cs:27` |
| `LgymApi.ArchitectureTests -> LgymApi.TrainingPlanning` | `LgymApi.ArchitectureTests/CoachingManagedPlanSliceArchitectureTests.cs:14` |
| `LgymApi.BackgroundWorker -> LgymApi.Application` | `LgymApi.BackgroundWorker/Actions/DietPlanUpdatedInAppNotificationCommandHandler.cs:1` |
| `LgymApi.BackgroundWorker -> LgymApi.BackgroundWorker.Common` | `LgymApi.BackgroundWorker/Actions/SendRegistrationEmailHandler.cs:2` |
| `LgymApi.BackgroundWorker -> LgymApi.Identity` | `LgymApi.BackgroundWorker/Actions/LocalizedReportNotificationDispatcher.cs:13` |
| `LgymApi.BackgroundWorker -> LgymApi.Infrastructure` | `LgymApi.BackgroundWorker/ServiceProvider.cs:51` |
| `LgymApi.BackgroundWorker -> LgymApi.Notifications` | `LgymApi.BackgroundWorker/Actions/DietPlanUpdatedInAppNotificationCommandHandler.cs:2` |
| `LgymApi.BackgroundWorker -> LgymApi.Platform` | `LgymApi.BackgroundWorker/Actions/DietPlanUpdatedInAppNotificationCommandHandler.cs:4` |
| `LgymApi.BackgroundWorker.Common -> LgymApi.Domain` | `LgymApi.BackgroundWorker.Common/IdempotencyKeyPolicy.cs:1` |
| `LgymApi.DataSeeder -> LgymApi.Domain` | `LgymApi.DataSeeder/SeedContext.cs:1` |
| `LgymApi.DataSeeder -> LgymApi.Identity` | `LgymApi.DataSeeder/Program.cs:3` |
| `LgymApi.DataSeeder -> LgymApi.Infrastructure` | `LgymApi.DataSeeder/Program.cs:51` |
| `LgymApi.DataSeeder.Tests -> LgymApi.DataSeeder` | `LgymApi.DataSeeder.Tests/ConsolePromptTests.cs:37` |
| `LgymApi.DataSeeder.Tests -> LgymApi.Domain` | `LgymApi.DataSeeder.Tests/DataSeederProgramTests.cs:3` |
| `LgymApi.DataSeeder.Tests -> LgymApi.Identity` | `LgymApi.DataSeeder.Tests/DataSeederProgramTests.cs:1` |
| `LgymApi.DataSeeder.Tests -> LgymApi.Infrastructure` | `LgymApi.DataSeeder.Tests/DataSeederProgramTests.cs:77` |
| `LgymApi.Identity -> LgymApi.Domain` | `LgymApi.Identity/Access/AccountReadService.cs:4` |
| `LgymApi.Identity -> LgymApi.Platform` | `LgymApi.Identity/Access/AccountReadService.cs:2` |
| `LgymApi.Identity -> LgymApi.Resources` | `LgymApi.Identity/Administration/UserRoleAdministrationService.cs:36` |
| `LgymApi.Infrastructure -> LgymApi.Application` | `LgymApi.Infrastructure/CoachingServiceCollectionExtensions.cs:1` |
| `LgymApi.Infrastructure -> LgymApi.BackgroundWorker.Common` | `LgymApi.Infrastructure/Services/CommittedIntentDispatcher.cs:3` |
| `LgymApi.Infrastructure -> LgymApi.Domain` | `LgymApi.Infrastructure/Configuration/InfrastructureMappingRegistration.cs:4` |
| `LgymApi.Infrastructure -> LgymApi.Identity` | `LgymApi.Infrastructure/Configuration/InfrastructureMappingRegistration.cs:12` |
| `LgymApi.Infrastructure -> LgymApi.Notifications` | `LgymApi.Infrastructure/Data/AppDbContext.cs:9` |
| `LgymApi.Infrastructure -> LgymApi.Platform` | `LgymApi.Infrastructure/Configuration/InfrastructureMappingRegistration.cs:1` |
| `LgymApi.Infrastructure -> LgymApi.TrainingPlanning` | `LgymApi.Infrastructure/Data/AppDbContext.cs:11` |
| `LgymApi.IntegrationTests -> LgymApi.Api` | `LgymApi.IntegrationTests/ApiHostConfigurationCharacterizationTests.cs:8` |
| `LgymApi.IntegrationTests -> LgymApi.Application` | `LgymApi.IntegrationTests/CoachingDashboardProgressSliceIntegrationTests.cs:2` |
| `LgymApi.IntegrationTests -> LgymApi.BackgroundWorker` | `LgymApi.IntegrationTests/CoachingLegacyEnvelopeReplayIntegrationTests.cs:245` |
| `LgymApi.IntegrationTests -> LgymApi.BackgroundWorker.Common` | `LgymApi.IntegrationTests/CoachingLegacyEnvelopeReplayIntegrationTests.cs:223` |
| `LgymApi.IntegrationTests -> LgymApi.Domain` | `LgymApi.IntegrationTests/AdminFlagTests.cs:4` |
| `LgymApi.IntegrationTests -> LgymApi.Identity` | `LgymApi.IntegrationTests/CoachingContractCompatibilityTests.cs:8` |
| `LgymApi.IntegrationTests -> LgymApi.Infrastructure` | `LgymApi.IntegrationTests/AdminUserIntegrationTests.cs:86` |
| `LgymApi.IntegrationTests -> LgymApi.Notifications` | `LgymApi.IntegrationTests/CompositionRootStartupTests.cs:16` |
| `LgymApi.IntegrationTests -> LgymApi.Platform` | `LgymApi.IntegrationTests/ApiHostConfigurationCharacterizationTests.cs:9` |
| `LgymApi.IntegrationTests -> LgymApi.Resources` | `LgymApi.IntegrationTests/ApiHostConfigurationCharacterizationTests.cs:179` |
| `LgymApi.IntegrationTests -> LgymApi.TestUtils` | `LgymApi.IntegrationTests/CoachingLegacyEnvelopeReplayIntegrationTests.cs:78` |
| `LgymApi.IntegrationTests -> LgymApi.TrainingPlanning` | `LgymApi.IntegrationTests/CoachingManagedPlanSliceIntegrationTests.cs:6` |
| `LgymApi.Notifications -> LgymApi.BackgroundWorker.Common` | `LgymApi.Notifications/EmailTemplates/EmailServiceCollectionExtensions.cs:1` |
| `LgymApi.Notifications -> LgymApi.Domain` | `LgymApi.Notifications/Adapters/PushInstallationSessionDisassociationAdapter.cs:3` |
| `LgymApi.Notifications -> LgymApi.Identity` | `LgymApi.Notifications/Adapters/PushInstallationSessionDisassociationAdapter.cs:1` |
| `LgymApi.Notifications -> LgymApi.Platform` | `LgymApi.Notifications/CoachingNotificationIntentService.cs:2` |
| `LgymApi.Notifications -> LgymApi.Resources` | `LgymApi.Notifications/CoachingNotificationIntentService.cs:52` |
| `LgymApi.Platform -> LgymApi.Domain` | `LgymApi.Platform/Contracts/Serialization/TypedIdJsonConverter.cs:3` |
| `LgymApi.Platform -> LgymApi.Resources` | `LgymApi.Platform/ReferenceData/AppConfig/AppConfigService.cs:35` |
| `LgymApi.Resources -> LgymApi.Resources.Generator` | `LgymApi.Resources/LgymApi.Resources.csproj:16` analyzer reference |
| `LgymApi.TestUtils -> LgymApi.Application` | `LgymApi.TestUtils/TestServiceProviderFactory.cs:14` |
| `LgymApi.TestUtils -> LgymApi.BackgroundWorker` | `LgymApi.TestUtils/TestServiceProviderFactory.cs:15` |
| `LgymApi.TestUtils -> LgymApi.BackgroundWorker.Common` | `LgymApi.TestUtils/TestEmailSender.cs:1` |
| `LgymApi.TestUtils -> LgymApi.Domain` | `LgymApi.TestUtils/TestDataFactory.cs:3` |
| `LgymApi.TestUtils -> LgymApi.Identity` | `LgymApi.TestUtils/TestDataFactory.cs:7` |
| `LgymApi.TestUtils -> LgymApi.Infrastructure` | `LgymApi.TestUtils/TestDataFactory.cs:40` |
| `LgymApi.TrainingPlanning -> LgymApi.Domain` | `LgymApi.TrainingPlanning/Contracts/ManagedPlans/AssignManagedPlanCommand.cs:1` |
| `LgymApi.TrainingPlanning -> LgymApi.Identity` | `LgymApi.TrainingPlanning/Contracts/ManagedPlans/AssignManagedPlanCommand.cs:2` |
| `LgymApi.TrainingPlanning -> LgymApi.Platform` | `LgymApi.TrainingPlanning/Contracts/ManagedPlans/IAssignManagedPlanUseCase.cs:1` |
| `LgymApi.TrainingPlanning -> LgymApi.Resources` | `LgymApi.TrainingPlanning/ManagedPlans/AssignManagedPlanUseCase.cs:44` |
| `LgymApi.UnitTests -> LgymApi.Api` | `LgymApi.UnitTests/AccountReadServiceTests.cs:195` |
| `LgymApi.UnitTests -> LgymApi.Application` | `LgymApi.UnitTests/AcceptedProgressCommandOutboxTests.cs:6` |
| `LgymApi.UnitTests -> LgymApi.BackgroundWorker` | `LgymApi.UnitTests/AcceptedProgressCommandOutboxTests.cs:11` |
| `LgymApi.UnitTests -> LgymApi.BackgroundWorker.Common` | `LgymApi.UnitTests/AcceptedProgressCommandOutboxTests.cs:21` |
| `LgymApi.UnitTests -> LgymApi.Domain` | `LgymApi.UnitTests/AcceptedProgressCommandOutboxTests.cs:13` |
| `LgymApi.UnitTests -> LgymApi.Identity` | `LgymApi.UnitTests/AcceptedProgressCommandOutboxTests.cs:16` |
| `LgymApi.UnitTests -> LgymApi.Infrastructure` | `LgymApi.UnitTests/AppDbContextFactoryTests.cs:32` |
| `LgymApi.UnitTests -> LgymApi.Notifications` | `LgymApi.UnitTests/ApplicationPushContractCompatibilityTests.cs:7` |
| `LgymApi.UnitTests -> LgymApi.Platform` | `LgymApi.UnitTests/AcceptedProgressCommandOutboxTests.cs:4` |
| `LgymApi.UnitTests -> LgymApi.Resources` | `LgymApi.UnitTests/CoachingNotificationIntentInAppTests.cs:99` |
| `LgymApi.UnitTests -> LgymApi.TestUtils` | `LgymApi.UnitTests/ApplicationApiAdapterRegistrationTests.cs:119` |
| `LgymApi.UnitTests -> LgymApi.TrainingPlanning` | `LgymApi.UnitTests/CoachingManagedPlanSliceTests.cs:17` |

## Locked topology

The graph has exactly 18 projects and 90 `ProjectReference` edges. It is acyclic, each edge is unique, 89 edges have a Roslyn-resolved direct source/import use, and the Resources Generator edge is analyzer-configured. The forbidden cross-project edge complement contains exactly 216 edges.

Topological order: `LgymApi.Domain` -> `LgymApi.BackgroundWorker.Common` -> `LgymApi.Resources.Generator` -> `LgymApi.Resources` -> `LgymApi.Platform` -> `LgymApi.Identity` -> `LgymApi.Notifications` -> `LgymApi.TrainingPlanning` -> `LgymApi.Application` -> `LgymApi.Infrastructure` -> `LgymApi.BackgroundWorker` -> `LgymApi.Api` -> `LgymApi.ArchitectureTests` -> `LgymApi.DataSeeder` -> `LgymApi.DataSeeder.Tests` -> `LgymApi.TestUtils` -> `LgymApi.IntegrationTests` -> `LgymApi.UnitTests`

This topology retains one `AppDbContext`, migration root, snapshot, database, schema, and migration stream. Worker selects environment-specific scheduling and delegates hosted Hangfire server registration to the Infrastructure-owned persistence helper.

[issue-375-project-reference-graph.md](issue-375-project-reference-graph.md) is an unchanged historical capture.
