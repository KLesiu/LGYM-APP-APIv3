namespace LgymApi.ArchitectureTests;

public static class ModuleBoundaryDebtAllowlistRegistry
{
    public const int MaximumAllowedEntryCount = 130;

    private static readonly IReadOnlyList<ModuleBoundaryDebtEntry> Entries =
    [
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "ModuleDependencyGuardTests",
            sourceModule: "Reporting",
            targetModule: "Workout & Progress",
            sourceSymbolOrPath: "LgymApi.Application.Features.Reporting.ReportSubmissionAcceptedProgressCommandFactory @ LgymApi.Application/Features/Reporting/ReportSubmissionAcceptedProgressCommandFactory.cs",
            targetSymbolOrPath: "LgymApi.Application.WorkoutProgress.Contracts.ReportingIntegration.ReportSubmissionAcceptedMeasurement @ LgymApi.Application/WorkoutProgress/Contracts/ReportingIntegration/ReportSubmissionAcceptedProgressEvent.cs",
            rationale: "Reporting may construct the explicit accepted-progress publication contract but must not consume or persist Workout & Progress data.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "ModuleDependencyGuardTests",
            sourceModule: "Reporting",
            targetModule: "Workout & Progress",
            sourceSymbolOrPath: "LgymApi.Application.Reporting.Contracts.BackgroundCommands.ReportSubmissionAcceptedProgressCommand @ LgymApi.Application/Reporting/Contracts/BackgroundCommands/ReportSubmissionAcceptedProgressCommand.cs",
            targetSymbolOrPath: "LgymApi.Application.WorkoutProgress.Contracts.ReportingIntegration.ReportSubmissionAcceptedProgressEvent @ LgymApi.Application/WorkoutProgress/Contracts/ReportingIntegration/ReportSubmissionAcceptedProgressEvent.cs",
            rationale: "Reporting may publish the explicit accepted-progress event contract but must not consume or persist Workout & Progress data.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "ModuleDependencyGuardTests",
            sourceModule: "Reporting",
            targetModule: "Workout & Progress",
            sourceSymbolOrPath: "LgymApi.Application.Reporting.Contracts.BackgroundCommands.ReportSubmissionAcceptedProgressCommand @ LgymApi.Application/Reporting/Contracts/BackgroundCommands/ReportSubmissionAcceptedProgressCommand.cs",
            targetSymbolOrPath: "LgymApi.Application.WorkoutProgress.Contracts.ReportingIntegration.ReportSubmissionAcceptedProgressValidationResult @ LgymApi.Application/WorkoutProgress/Contracts/ReportingIntegration/ReportSubmissionAcceptedProgressEvent.cs",
            rationale: "Reporting may validate the explicit accepted-progress event contract before publication but must not consume or persist Workout & Progress data.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "CrossModuleEntityLeakage",
            sourceModule: "Reporting",
            targetModule: "Identity & Accounts",
            sourceSymbolOrPath: "LgymApi.Application/Features/Reporting/ReportSubmissionAcceptedProgressCommandFactory.cs",
            targetSymbolOrPath: "LgymApi.Domain.Entities.User",
            rationale: "Reporting constructs the accepted-progress publication using the established trainee typed ID required by the explicit event contract.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "ModuleDependencyGuardTests",
            sourceModule: "Training Planning",
            targetModule: "Workout & Progress",
            sourceSymbolOrPath: "LgymApi.Application.Features.PlanDay.IPlanDayServiceDependencies @ LgymApi.Application/PlanDay/IPlanDayServiceDependencies.cs",
            targetSymbolOrPath: "LgymApi.Application.Repositories.IExerciseRepository @ LgymApi.Application/Repositories/IExerciseRepository.cs",
            rationale: "Current modular-boundary debt: training-planning plan-day flows still depend on workout/progress repositories directly.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "ModuleDependencyGuardTests",
            sourceModule: "Training Planning",
            targetModule: "Workout & Progress",
            sourceSymbolOrPath: "LgymApi.Application.Features.PlanDay.IPlanDayServiceDependencies @ LgymApi.Application/PlanDay/IPlanDayServiceDependencies.cs",
            targetSymbolOrPath: "LgymApi.Application.Repositories.ITrainingRepository @ LgymApi.Application/Repositories/ITrainingRepository.cs",
            rationale: "Current modular-boundary debt: training-planning plan-day flows still depend on workout/progress repositories directly.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "ModuleDependencyGuardTests",
            sourceModule: "Training Planning",
            targetModule: "Workout & Progress",
            sourceSymbolOrPath: "LgymApi.Application.Features.PlanDay.PlanDayService @ LgymApi.Application/PlanDay/PlanDayService.cs",
            targetSymbolOrPath: "LgymApi.Application.Repositories.IExerciseRepository @ LgymApi.Application/Repositories/IExerciseRepository.cs",
            rationale: "Current modular-boundary debt: training-planning plan-day flows still depend on workout/progress repositories directly.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "ModuleDependencyGuardTests",
            sourceModule: "Training Planning",
            targetModule: "Workout & Progress",
            sourceSymbolOrPath: "LgymApi.Application.Features.PlanDay.PlanDayService @ LgymApi.Application/PlanDay/PlanDayService.cs",
            targetSymbolOrPath: "LgymApi.Application.Repositories.ITrainingRepository @ LgymApi.Application/Repositories/ITrainingRepository.cs",
            rationale: "Current modular-boundary debt: training-planning plan-day flows still depend on workout/progress repositories directly.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "ModuleDependencyGuardTests",
            sourceModule: "Training Planning",
            targetModule: "Workout & Progress",
            sourceSymbolOrPath: "LgymApi.Application.Features.PlanDay.PlanDayServiceDependencies @ LgymApi.Application/PlanDay/IPlanDayServiceDependencies.cs",
            targetSymbolOrPath: "LgymApi.Application.Repositories.IExerciseRepository @ LgymApi.Application/Repositories/IExerciseRepository.cs",
            rationale: "Current modular-boundary debt: training-planning plan-day flows still depend on workout/progress repositories directly.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "ModuleDependencyGuardTests",
            sourceModule: "Training Planning",
            targetModule: "Workout & Progress",
            sourceSymbolOrPath: "LgymApi.Application.Features.PlanDay.PlanDayServiceDependencies @ LgymApi.Application/PlanDay/IPlanDayServiceDependencies.cs",
            targetSymbolOrPath: "LgymApi.Application.Repositories.ITrainingRepository @ LgymApi.Application/Repositories/ITrainingRepository.cs",
            rationale: "Current modular-boundary debt: training-planning plan-day flows still depend on workout/progress repositories directly.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "CrossModuleEntityLeakage",
            sourceModule: "Reporting",
            targetModule: "Identity & Accounts",
            sourceSymbolOrPath: "CompletePhotoUploadAsync",
            targetSymbolOrPath: "LgymApi.Domain.Entities.User",
            rationale: "Pre-existing cross-module entity/repository coupling tracked as shrink-only debt while explicit contracts/read models/events are introduced.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "CrossModuleEntityLeakage",
            sourceModule: "Reporting",
            targetModule: "Identity & Accounts",
            sourceSymbolOrPath: "CreateAndPersistPhotoAsync",
            targetSymbolOrPath: "LgymApi.Domain.Entities.User",
            rationale: "Pre-existing cross-module entity/repository coupling tracked as shrink-only debt while explicit contracts/read models/events are introduced.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "CrossModuleEntityLeakage",
            sourceModule: "Reporting",
            targetModule: "Identity & Accounts",
            sourceSymbolOrPath: "CreateAsync",
            targetSymbolOrPath: "LgymApi.Domain.Entities.User",
            rationale: "Pre-existing cross-module entity/repository coupling tracked as shrink-only debt while explicit contracts/read models/events are introduced.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "CrossModuleEntityLeakage",
            sourceModule: "Reporting",
            targetModule: "Identity & Accounts",
            sourceSymbolOrPath: "CreateReportRequestAsync",
            targetSymbolOrPath: "LgymApi.Domain.Entities.User",
            rationale: "Pre-existing cross-module entity/repository coupling tracked as shrink-only debt while explicit contracts/read models/events are introduced.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "CrossModuleEntityLeakage",
            sourceModule: "Reporting",
            targetModule: "Identity & Accounts",
            sourceSymbolOrPath: "CreateTemplateAsync",
            targetSymbolOrPath: "LgymApi.Domain.Entities.User",
            rationale: "Pre-existing cross-module entity/repository coupling tracked as shrink-only debt while explicit contracts/read models/events are introduced.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "CrossModuleEntityLeakage",
            sourceModule: "Reporting",
            targetModule: "Identity & Accounts",
            sourceSymbolOrPath: "DeleteAsync",
            targetSymbolOrPath: "LgymApi.Domain.Entities.User",
            rationale: "Pre-existing cross-module entity/repository coupling tracked as shrink-only debt while explicit contracts/read models/events are introduced.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "CrossModuleEntityLeakage",
            sourceModule: "Reporting",
            targetModule: "Identity & Accounts",
            sourceSymbolOrPath: "DeleteTemplateAsync",
            targetSymbolOrPath: "LgymApi.Domain.Entities.User",
            rationale: "Pre-existing cross-module entity/repository coupling tracked as shrink-only debt while explicit contracts/read models/events are introduced.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "CrossModuleEntityLeakage",
            sourceModule: "Reporting",
            targetModule: "Identity & Accounts",
            sourceSymbolOrPath: "EnsureOwnedTemplateAsync",
            targetSymbolOrPath: "LgymApi.Domain.Entities.User",
            rationale: "Pre-existing cross-module entity/repository coupling tracked as shrink-only debt while explicit contracts/read models/events are introduced.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "CrossModuleEntityLeakage",
            sourceModule: "Reporting",
            targetModule: "Identity & Accounts",
            sourceSymbolOrPath: "EnsureStorageKeyHasExpectedPrefix",
            targetSymbolOrPath: "LgymApi.Domain.Entities.User",
            rationale: "Pre-existing cross-module entity/repository coupling tracked as shrink-only debt while explicit contracts/read models/events are introduced.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "CrossModuleEntityLeakage",
            sourceModule: "Reporting",
            targetModule: "Identity & Accounts",
            sourceSymbolOrPath: "EnsureTrainerAsync",
            targetSymbolOrPath: "LgymApi.Application.Repositories.IRoleRepository",
            rationale: "Pre-existing cross-module entity/repository coupling tracked as shrink-only debt while explicit contracts/read models/events are introduced.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "CrossModuleEntityLeakage",
            sourceModule: "Reporting",
            targetModule: "Identity & Accounts",
            sourceSymbolOrPath: "EnsureTrainerAsync",
            targetSymbolOrPath: "LgymApi.Domain.Entities.User",
            rationale: "Pre-existing cross-module entity/repository coupling tracked as shrink-only debt while explicit contracts/read models/events are introduced.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "CrossModuleEntityLeakage",
            sourceModule: "Reporting",
            targetModule: "Identity & Accounts",
            sourceSymbolOrPath: "EnsureTrainerOwnsTraineeAsync",
            targetSymbolOrPath: "LgymApi.Domain.Entities.User",
            rationale: "Pre-existing cross-module entity/repository coupling tracked as shrink-only debt while explicit contracts/read models/events are introduced.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "CrossModuleEntityLeakage",
            sourceModule: "Reporting",
            targetModule: "Identity & Accounts",
            sourceSymbolOrPath: "GetForTraineeAsync",
            targetSymbolOrPath: "LgymApi.Domain.Entities.User",
            rationale: "Pre-existing cross-module entity/repository coupling tracked as shrink-only debt while explicit contracts/read models/events are introduced.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "CrossModuleEntityLeakage",
            sourceModule: "Reporting",
            targetModule: "Identity & Accounts",
            sourceSymbolOrPath: "GetOwnSubmissionsAsync",
            targetSymbolOrPath: "LgymApi.Domain.Entities.User",
            rationale: "Pre-existing cross-module entity/repository coupling tracked as shrink-only debt while explicit contracts/read models/events are introduced.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "CrossModuleEntityLeakage",
            sourceModule: "Reporting",
            targetModule: "Identity & Accounts",
            sourceSymbolOrPath: "GetOwnedAssignmentAsync",
            targetSymbolOrPath: "LgymApi.Domain.Entities.User",
            rationale: "Pre-existing cross-module entity/repository coupling tracked as shrink-only debt while explicit contracts/read models/events are introduced.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "CrossModuleEntityLeakage",
            sourceModule: "Reporting",
            targetModule: "Identity & Accounts",
            sourceSymbolOrPath: "GetPendingRequestsForTraineeAsync",
            targetSymbolOrPath: "LgymApi.Domain.Entities.User",
            rationale: "Pre-existing cross-module entity/repository coupling tracked as shrink-only debt while explicit contracts/read models/events are introduced.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "CrossModuleEntityLeakage",
            sourceModule: "Reporting",
            targetModule: "Identity & Accounts",
            sourceSymbolOrPath: "GetPhotoHistoryAsync",
            targetSymbolOrPath: "LgymApi.Domain.Entities.User",
            rationale: "Pre-existing cross-module entity/repository coupling tracked as shrink-only debt while explicit contracts/read models/events are introduced.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "CrossModuleEntityLeakage",
            sourceModule: "Reporting",
            targetModule: "Identity & Accounts",
            sourceSymbolOrPath: "GetSignedReadUrlAsync",
            targetSymbolOrPath: "LgymApi.Domain.Entities.User",
            rationale: "Pre-existing cross-module entity/repository coupling tracked as shrink-only debt while explicit contracts/read models/events are introduced.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "CrossModuleEntityLeakage",
            sourceModule: "Reporting",
            targetModule: "Identity & Accounts",
            sourceSymbolOrPath: "GetTraineeSubmissionsAsync",
            targetSymbolOrPath: "LgymApi.Domain.Entities.User",
            rationale: "Pre-existing cross-module entity/repository coupling tracked as shrink-only debt while explicit contracts/read models/events are introduced.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "CrossModuleEntityLeakage",
            sourceModule: "Reporting",
            targetModule: "Identity & Accounts",
            sourceSymbolOrPath: "GetTrainerTemplateAsync",
            targetSymbolOrPath: "LgymApi.Domain.Entities.User",
            rationale: "Pre-existing cross-module entity/repository coupling tracked as shrink-only debt while explicit contracts/read models/events are introduced.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "CrossModuleEntityLeakage",
            sourceModule: "Reporting",
            targetModule: "Identity & Accounts",
            sourceSymbolOrPath: "GetTrainerTemplatesAsync",
            targetSymbolOrPath: "LgymApi.Domain.Entities.User",
            rationale: "Pre-existing cross-module entity/repository coupling tracked as shrink-only debt while explicit contracts/read models/events are introduced.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "CrossModuleEntityLeakage",
            sourceModule: "Reporting",
            targetModule: "Identity & Accounts",
            sourceSymbolOrPath: "InitiatePhotoUploadAsync",
            targetSymbolOrPath: "LgymApi.Domain.Entities.User",
            rationale: "Pre-existing cross-module entity/repository coupling tracked as shrink-only debt while explicit contracts/read models/events are introduced.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "CrossModuleEntityLeakage",
            sourceModule: "Reporting",
            targetModule: "Identity & Accounts",
            sourceSymbolOrPath: "LgymApi.Application/Features/Reporting/IRecurringReportAssignmentService.cs",
            targetSymbolOrPath: "LgymApi.Domain.Entities.User",
            rationale: "Pre-existing cross-module entity/repository coupling tracked as shrink-only debt while explicit contracts/read models/events are introduced.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "CrossModuleEntityLeakage",
            sourceModule: "Reporting",
            targetModule: "Identity & Accounts",
            sourceSymbolOrPath: "LgymApi.Application/Features/Reporting/IReportingService.cs",
            targetSymbolOrPath: "LgymApi.Domain.Entities.User",
            rationale: "Pre-existing cross-module entity/repository coupling tracked as shrink-only debt while explicit contracts/read models/events are introduced.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "CrossModuleEntityLeakage",
            sourceModule: "Reporting",
            targetModule: "Identity & Accounts",
            sourceSymbolOrPath: "LgymApi.Application/Features/Reporting/RecurringReportAssignmentService.Support.cs",
            targetSymbolOrPath: "LgymApi.Domain.Entities.User",
            rationale: "Pre-existing cross-module entity/repository coupling tracked as shrink-only debt while explicit contracts/read models/events are introduced.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "CrossModuleEntityLeakage",
            sourceModule: "Reporting",
            targetModule: "Identity & Accounts",
            sourceSymbolOrPath: "LgymApi.Application/Features/Reporting/RecurringReportAssignmentService.cs",
            targetSymbolOrPath: "LgymApi.Domain.Entities.User",
            rationale: "Pre-existing cross-module entity/repository coupling tracked as shrink-only debt while explicit contracts/read models/events are introduced.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "CrossModuleEntityLeakage",
            sourceModule: "Reporting",
            targetModule: "Identity & Accounts",
            sourceSymbolOrPath: "LgymApi.Application/Features/Reporting/ReportingService.Photos.Read.cs",
            targetSymbolOrPath: "LgymApi.Domain.Entities.User",
            rationale: "Pre-existing cross-module entity/repository coupling tracked as shrink-only debt while explicit contracts/read models/events are introduced.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "CrossModuleEntityLeakage",
            sourceModule: "Reporting",
            targetModule: "Identity & Accounts",
            sourceSymbolOrPath: "LgymApi.Application/Features/Reporting/ReportingService.Photos.Refactor.cs",
            targetSymbolOrPath: "LgymApi.Domain.Entities.User",
            rationale: "Pre-existing cross-module entity/repository coupling tracked as shrink-only debt while explicit contracts/read models/events are introduced.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "CrossModuleEntityLeakage",
            sourceModule: "Reporting",
            targetModule: "Identity & Accounts",
            sourceSymbolOrPath: "LgymApi.Application/Features/Reporting/ReportingService.Photos.Support.cs",
            targetSymbolOrPath: "LgymApi.Domain.Entities.User",
            rationale: "Pre-existing cross-module entity/repository coupling tracked as shrink-only debt while explicit contracts/read models/events are introduced.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "CrossModuleEntityLeakage",
            sourceModule: "Reporting",
            targetModule: "Identity & Accounts",
            sourceSymbolOrPath: "LgymApi.Application/Features/Reporting/ReportingService.Photos.cs",
            targetSymbolOrPath: "LgymApi.Domain.Entities.User",
            rationale: "Pre-existing cross-module entity/repository coupling tracked as shrink-only debt while explicit contracts/read models/events are introduced.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "CrossModuleEntityLeakage",
            sourceModule: "Reporting",
            targetModule: "Identity & Accounts",
            sourceSymbolOrPath: "LgymApi.Application/Features/Reporting/ReportingService.Requests.cs",
            targetSymbolOrPath: "LgymApi.Domain.Entities.User",
            rationale: "Pre-existing cross-module entity/repository coupling tracked as shrink-only debt while explicit contracts/read models/events are introduced.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "CrossModuleEntityLeakage",
            sourceModule: "Reporting",
            targetModule: "Identity & Accounts",
            sourceSymbolOrPath: "LgymApi.Application/Features/Reporting/ReportingService.Submissions.cs",
            targetSymbolOrPath: "LgymApi.Domain.Entities.User",
            rationale: "Pre-existing cross-module entity/repository coupling tracked as shrink-only debt while explicit contracts/read models/events are introduced.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "CrossModuleEntityLeakage",
            sourceModule: "Reporting",
            targetModule: "Identity & Accounts",
            sourceSymbolOrPath: "LgymApi.Application/Features/Reporting/ReportingService.Templates.cs",
            targetSymbolOrPath: "LgymApi.Domain.Entities.User",
            rationale: "Pre-existing cross-module entity/repository coupling tracked as shrink-only debt while explicit contracts/read models/events are introduced.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "CrossModuleEntityLeakage",
            sourceModule: "Reporting",
            targetModule: "Identity & Accounts",
            sourceSymbolOrPath: "LgymApi.Application/Features/Reporting/ReportingService.cs",
            targetSymbolOrPath: "LgymApi.Domain.Entities.User",
            rationale: "Pre-existing cross-module entity/repository coupling tracked as shrink-only debt while explicit contracts/read models/events are introduced.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "CrossModuleEntityLeakage",
            sourceModule: "Reporting",
            targetModule: "Identity & Accounts",
            sourceSymbolOrPath: "MarkTrainerFeedbackAsReadAsync",
            targetSymbolOrPath: "LgymApi.Domain.Entities.User",
            rationale: "Pre-existing cross-module entity/repository coupling tracked as shrink-only debt while explicit contracts/read models/events are introduced.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "CrossModuleEntityLeakage",
            sourceModule: "Reporting",
            targetModule: "Identity & Accounts",
            sourceSymbolOrPath: "PauseAsync",
            targetSymbolOrPath: "LgymApi.Domain.Entities.User",
            rationale: "Pre-existing cross-module entity/repository coupling tracked as shrink-only debt while explicit contracts/read models/events are introduced.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "CrossModuleEntityLeakage",
            sourceModule: "Reporting",
            targetModule: "Identity & Accounts",
            sourceSymbolOrPath: "ReportingServiceDependencies",
            targetSymbolOrPath: "LgymApi.Application.Repositories.IRoleRepository",
            rationale: "Pre-existing cross-module entity/repository coupling tracked as shrink-only debt while explicit contracts/read models/events are introduced.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "CrossModuleEntityLeakage",
            sourceModule: "Reporting",
            targetModule: "Identity & Accounts",
            sourceSymbolOrPath: "ReportingService",
            targetSymbolOrPath: "LgymApi.Application.Repositories.IRoleRepository",
            rationale: "Pre-existing cross-module entity/repository coupling tracked as shrink-only debt while explicit contracts/read models/events are introduced.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "CrossModuleEntityLeakage",
            sourceModule: "Reporting",
            targetModule: "Identity & Accounts",
            sourceSymbolOrPath: "ResumeAsync",
            targetSymbolOrPath: "LgymApi.Domain.Entities.User",
            rationale: "Pre-existing cross-module entity/repository coupling tracked as shrink-only debt while explicit contracts/read models/events are introduced.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "CrossModuleEntityLeakage",
            sourceModule: "Reporting",
            targetModule: "Identity & Accounts",
            sourceSymbolOrPath: "RoleRepository",
            targetSymbolOrPath: "LgymApi.Application.Repositories.IRoleRepository",
            rationale: "Pre-existing cross-module entity/repository coupling tracked as shrink-only debt while explicit contracts/read models/events are introduced.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "CrossModuleEntityLeakage",
            sourceModule: "Reporting",
            targetModule: "Identity & Accounts",
            sourceSymbolOrPath: "SubmitReportRequestAsync",
            targetSymbolOrPath: "LgymApi.Domain.Entities.User",
            rationale: "Pre-existing cross-module entity/repository coupling tracked as shrink-only debt while explicit contracts/read models/events are introduced.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "CrossModuleEntityLeakage",
            sourceModule: "Reporting",
            targetModule: "Identity & Accounts",
            sourceSymbolOrPath: "UpdateAsync",
            targetSymbolOrPath: "LgymApi.Domain.Entities.User",
            rationale: "Pre-existing cross-module entity/repository coupling tracked as shrink-only debt while explicit contracts/read models/events are introduced.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "CrossModuleEntityLeakage",
            sourceModule: "Reporting",
            targetModule: "Identity & Accounts",
            sourceSymbolOrPath: "UpdateTemplateAsync",
            targetSymbolOrPath: "LgymApi.Domain.Entities.User",
            rationale: "Pre-existing cross-module entity/repository coupling tracked as shrink-only debt while explicit contracts/read models/events are introduced.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "CrossModuleEntityLeakage",
            sourceModule: "Reporting",
            targetModule: "Identity & Accounts",
            sourceSymbolOrPath: "UpdateTrainerFeedbackAsync",
            targetSymbolOrPath: "LgymApi.Domain.Entities.User",
            rationale: "Pre-existing cross-module entity/repository coupling tracked as shrink-only debt while explicit contracts/read models/events are introduced.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "CrossModuleEntityLeakage",
            sourceModule: "Reporting",
            targetModule: "Identity & Accounts",
            sourceSymbolOrPath: "ValidateCompletePhotoUploadRequestAsync",
            targetSymbolOrPath: "LgymApi.Domain.Entities.User",
            rationale: "Pre-existing cross-module entity/repository coupling tracked as shrink-only debt while explicit contracts/read models/events are introduced.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "CrossModuleEntityLeakage",
            sourceModule: "Reporting",
            targetModule: "Identity & Accounts",
            sourceSymbolOrPath: "ValidatePendingUpload",
            targetSymbolOrPath: "LgymApi.Domain.Entities.User",
            rationale: "Pre-existing cross-module entity/repository coupling tracked as shrink-only debt while explicit contracts/read models/events are introduced.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "CrossModuleEntityLeakage",
            sourceModule: "Reporting",
            targetModule: "Identity & Accounts",
            sourceSymbolOrPath: "ValidatePhotoAccessAsync",
            targetSymbolOrPath: "LgymApi.Domain.Entities.User",
            rationale: "Pre-existing cross-module entity/repository coupling tracked as shrink-only debt while explicit contracts/read models/events are introduced.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "CrossModuleEntityLeakage",
            sourceModule: "Reporting",
            targetModule: "Identity & Accounts",
            sourceSymbolOrPath: "ValidateTrainerAndCommandAsync",
            targetSymbolOrPath: "LgymApi.Domain.Entities.User",
            rationale: "Pre-existing cross-module entity/repository coupling tracked as shrink-only debt while explicit contracts/read models/events are introduced.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "CrossModuleEntityLeakage",
            sourceModule: "Reporting",
            targetModule: "Identity & Accounts",
            sourceSymbolOrPath: "_roleRepository",
            targetSymbolOrPath: "LgymApi.Application.Repositories.IRoleRepository",
            rationale: "Pre-existing cross-module entity/repository coupling tracked as shrink-only debt while explicit contracts/read models/events are introduced.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "CrossModuleEntityLeakage",
            sourceModule: "Training Planning",
            targetModule: "Identity & Accounts",
            sourceSymbolOrPath: "CanAccessPlanAsync",
            targetSymbolOrPath: "LgymApi.Domain.Entities.User",
            rationale: "Pre-existing cross-module entity/repository coupling tracked as shrink-only debt while explicit contracts/read models/events are introduced.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "CrossModuleEntityLeakage",
            sourceModule: "Training Planning",
            targetModule: "Identity & Accounts",
            sourceSymbolOrPath: "CreatePlanDayAsync",
            targetSymbolOrPath: "LgymApi.Domain.Entities.User",
            rationale: "Pre-existing cross-module entity/repository coupling tracked as shrink-only debt while explicit contracts/read models/events are introduced.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "CrossModuleEntityLeakage",
            sourceModule: "Training Planning",
            targetModule: "Identity & Accounts",
            sourceSymbolOrPath: "DeletePlanDayAsync",
            targetSymbolOrPath: "LgymApi.Domain.Entities.User",
            rationale: "Pre-existing cross-module entity/repository coupling tracked as shrink-only debt while explicit contracts/read models/events are introduced.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "CrossModuleEntityLeakage",
            sourceModule: "Training Planning",
            targetModule: "Identity & Accounts",
            sourceSymbolOrPath: "GetPlanDayAsync",
            targetSymbolOrPath: "LgymApi.Domain.Entities.User",
            rationale: "Pre-existing cross-module entity/repository coupling tracked as shrink-only debt while explicit contracts/read models/events are introduced.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "CrossModuleEntityLeakage",
            sourceModule: "Training Planning",
            targetModule: "Identity & Accounts",
            sourceSymbolOrPath: "GetPlanDaysAsync",
            targetSymbolOrPath: "LgymApi.Domain.Entities.User",
            rationale: "Pre-existing cross-module entity/repository coupling tracked as shrink-only debt while explicit contracts/read models/events are introduced.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "CrossModuleEntityLeakage",
            sourceModule: "Training Planning",
            targetModule: "Identity & Accounts",
            sourceSymbolOrPath: "GetPlanDaysInfoAsync",
            targetSymbolOrPath: "LgymApi.Domain.Entities.User",
            rationale: "Pre-existing cross-module entity/repository coupling tracked as shrink-only debt while explicit contracts/read models/events are introduced.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "CrossModuleEntityLeakage",
            sourceModule: "Training Planning",
            targetModule: "Identity & Accounts",
            sourceSymbolOrPath: "GetPlanDaysTypesAsync",
            targetSymbolOrPath: "LgymApi.Domain.Entities.User",
            rationale: "Pre-existing cross-module entity/repository coupling tracked as shrink-only debt while explicit contracts/read models/events are introduced.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "CrossModuleEntityLeakage",
            sourceModule: "Training Planning",
            targetModule: "Identity & Accounts",
            sourceSymbolOrPath: "LgymApi.Application/PlanDay/IPlanDayService.cs",
            targetSymbolOrPath: "LgymApi.Domain.Entities.User",
            rationale: "Pre-existing cross-module entity/repository coupling tracked as shrink-only debt while explicit contracts/read models/events are introduced.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "CrossModuleEntityLeakage",
            sourceModule: "Training Planning",
            targetModule: "Identity & Accounts",
            sourceSymbolOrPath: "LgymApi.Application/PlanDay/PlanDayService.Mutations.cs",
            targetSymbolOrPath: "LgymApi.Domain.Entities.User",
            rationale: "Pre-existing cross-module entity/repository coupling tracked as shrink-only debt while explicit contracts/read models/events are introduced.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "CrossModuleEntityLeakage",
            sourceModule: "Training Planning",
            targetModule: "Identity & Accounts",
            sourceSymbolOrPath: "LgymApi.Application/PlanDay/PlanDayService.Queries.cs",
            targetSymbolOrPath: "LgymApi.Domain.Entities.User",
            rationale: "Pre-existing cross-module entity/repository coupling tracked as shrink-only debt while explicit contracts/read models/events are introduced.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "CrossModuleEntityLeakage",
            sourceModule: "Training Planning",
            targetModule: "Identity & Accounts",
            sourceSymbolOrPath: "LgymApi.Application/PlanDay/PlanDayService.cs",
            targetSymbolOrPath: "LgymApi.Domain.Entities.User",
            rationale: "Pre-existing cross-module entity/repository coupling tracked as shrink-only debt while explicit contracts/read models/events are introduced.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "CrossModuleEntityLeakage",
            sourceModule: "Training Planning",
            targetModule: "Identity & Accounts",
            sourceSymbolOrPath: "UpdatePlanDayAsync",
            targetSymbolOrPath: "LgymApi.Domain.Entities.User",
            rationale: "Pre-existing cross-module entity/repository coupling tracked as shrink-only debt while explicit contracts/read models/events are introduced.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "CrossModuleEntityLeakage",
            sourceModule: "Training Planning",
            targetModule: "Workout & Progress",
            sourceSymbolOrPath: "ExerciseMap",
            targetSymbolOrPath: "LgymApi.Domain.Entities.Exercise",
            rationale: "Pre-existing cross-module entity/repository coupling tracked as shrink-only debt while explicit contracts/read models/events are introduced.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "CrossModuleEntityLeakage",
            sourceModule: "Training Planning",
            targetModule: "Workout & Progress",
            sourceSymbolOrPath: "ExerciseRepository",
            targetSymbolOrPath: "LgymApi.Application.Repositories.IExerciseRepository",
            rationale: "Pre-existing cross-module entity/repository coupling tracked as shrink-only debt while explicit contracts/read models/events are introduced.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "CrossModuleEntityLeakage",
            sourceModule: "Training Planning",
            targetModule: "Workout & Progress",
            sourceSymbolOrPath: "GetPlanDayAsync",
            targetSymbolOrPath: "LgymApi.Application.Repositories.IExerciseRepository",
            rationale: "Pre-existing cross-module entity/repository coupling tracked as shrink-only debt while explicit contracts/read models/events are introduced.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "CrossModuleEntityLeakage",
            sourceModule: "Training Planning",
            targetModule: "Workout & Progress",
            sourceSymbolOrPath: "GetPlanDayAsync",
            targetSymbolOrPath: "LgymApi.Domain.Entities.Exercise",
            rationale: "Pre-existing cross-module entity/repository coupling tracked as shrink-only debt while explicit contracts/read models/events are introduced.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "CrossModuleEntityLeakage",
            sourceModule: "Training Planning",
            targetModule: "Workout & Progress",
            sourceSymbolOrPath: "GetPlanDaysAsync",
            targetSymbolOrPath: "LgymApi.Application.Repositories.IExerciseRepository",
            rationale: "Pre-existing cross-module entity/repository coupling tracked as shrink-only debt while explicit contracts/read models/events are introduced.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "CrossModuleEntityLeakage",
            sourceModule: "Training Planning",
            targetModule: "Workout & Progress",
            sourceSymbolOrPath: "GetPlanDaysAsync",
            targetSymbolOrPath: "LgymApi.Domain.Entities.Exercise",
            rationale: "Pre-existing cross-module entity/repository coupling tracked as shrink-only debt while explicit contracts/read models/events are introduced.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "CrossModuleEntityLeakage",
            sourceModule: "Training Planning",
            targetModule: "Workout & Progress",
            sourceSymbolOrPath: "GetPlanDaysInfoAsync",
            targetSymbolOrPath: "LgymApi.Application.Repositories.ITrainingRepository",
            rationale: "Pre-existing cross-module entity/repository coupling tracked as shrink-only debt while explicit contracts/read models/events are introduced.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "CrossModuleEntityLeakage",
            sourceModule: "Training Planning",
            targetModule: "Workout & Progress",
            sourceSymbolOrPath: "LgymApi.Application/PlanDay/Models/PlanDayDetailsContext.cs",
            targetSymbolOrPath: "LgymApi.Domain.Entities.Exercise",
            rationale: "Pre-existing cross-module entity/repository coupling tracked as shrink-only debt while explicit contracts/read models/events are introduced.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "CrossModuleEntityLeakage",
            sourceModule: "Training Planning",
            targetModule: "Workout & Progress",
            sourceSymbolOrPath: "LgymApi.Application/PlanDay/Models/PlanDaysContext.cs",
            targetSymbolOrPath: "LgymApi.Domain.Entities.Exercise",
            rationale: "Pre-existing cross-module entity/repository coupling tracked as shrink-only debt while explicit contracts/read models/events are introduced.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "CrossModuleEntityLeakage",
            sourceModule: "Training Planning",
            targetModule: "Workout & Progress",
            sourceSymbolOrPath: "PlanDayServiceDependencies",
            targetSymbolOrPath: "LgymApi.Application.Repositories.IExerciseRepository",
            rationale: "Pre-existing cross-module entity/repository coupling tracked as shrink-only debt while explicit contracts/read models/events are introduced.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "CrossModuleEntityLeakage",
            sourceModule: "Training Planning",
            targetModule: "Workout & Progress",
            sourceSymbolOrPath: "PlanDayServiceDependencies",
            targetSymbolOrPath: "LgymApi.Application.Repositories.ITrainingRepository",
            rationale: "Pre-existing cross-module entity/repository coupling tracked as shrink-only debt while explicit contracts/read models/events are introduced.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "CrossModuleEntityLeakage",
            sourceModule: "Training Planning",
            targetModule: "Workout & Progress",
            sourceSymbolOrPath: "PlanDayService",
            targetSymbolOrPath: "LgymApi.Application.Repositories.IExerciseRepository",
            rationale: "Pre-existing cross-module entity/repository coupling tracked as shrink-only debt while explicit contracts/read models/events are introduced.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "CrossModuleEntityLeakage",
            sourceModule: "Training Planning",
            targetModule: "Workout & Progress",
            sourceSymbolOrPath: "PlanDayService",
            targetSymbolOrPath: "LgymApi.Application.Repositories.ITrainingRepository",
            rationale: "Pre-existing cross-module entity/repository coupling tracked as shrink-only debt while explicit contracts/read models/events are introduced.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "CrossModuleEntityLeakage",
            sourceModule: "Training Planning",
            targetModule: "Workout & Progress",
            sourceSymbolOrPath: "TrainingRepository",
            targetSymbolOrPath: "LgymApi.Application.Repositories.ITrainingRepository",
            rationale: "Pre-existing cross-module entity/repository coupling tracked as shrink-only debt while explicit contracts/read models/events are introduced.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "CrossModuleEntityLeakage",
            sourceModule: "Training Planning",
            targetModule: "Workout & Progress",
            sourceSymbolOrPath: "_exerciseRepository",
            targetSymbolOrPath: "LgymApi.Application.Repositories.IExerciseRepository",
            rationale: "Pre-existing cross-module entity/repository coupling tracked as shrink-only debt while explicit contracts/read models/events are introduced.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "CrossModuleEntityLeakage",
            sourceModule: "Training Planning",
            targetModule: "Workout & Progress",
            sourceSymbolOrPath: "_trainingRepository",
            targetSymbolOrPath: "LgymApi.Application.Repositories.ITrainingRepository",
            rationale: "Pre-existing cross-module entity/repository coupling tracked as shrink-only debt while explicit contracts/read models/events are introduced.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "CrossModuleEntityLeakage",
            sourceModule: "Workout & Progress",
            targetModule: "Identity & Accounts",
            sourceSymbolOrPath: "AddGlobalTranslationAsync",
            targetSymbolOrPath: "LgymApi.Application.Repositories.IRoleRepository",
            rationale: "Pre-existing cross-module entity/repository coupling tracked as shrink-only debt while explicit contracts/read models/events are introduced.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "CrossModuleEntityLeakage",
            sourceModule: "Workout & Progress",
            targetModule: "Identity & Accounts",
            sourceSymbolOrPath: "AddGlobalTranslationAsync",
            targetSymbolOrPath: "LgymApi.Domain.Entities.User",
            rationale: "Pre-existing cross-module entity/repository coupling tracked as shrink-only debt while explicit contracts/read models/events are introduced.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "CrossModuleEntityLeakage",
            sourceModule: "Workout & Progress",
            targetModule: "Identity & Accounts",
            sourceSymbolOrPath: "AddGymAsync",
            targetSymbolOrPath: "LgymApi.Domain.Entities.User",
            rationale: "Pre-existing cross-module entity/repository coupling tracked as shrink-only debt while explicit contracts/read models/events are introduced.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "CrossModuleEntityLeakage",
            sourceModule: "Workout & Progress",
            targetModule: "Identity & Accounts",
            sourceSymbolOrPath: "AddMeasurementAsync",
            targetSymbolOrPath: "LgymApi.Domain.Entities.User",
            rationale: "Pre-existing cross-module entity/repository coupling tracked as shrink-only debt while explicit contracts/read models/events are introduced.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "CrossModuleEntityLeakage",
            sourceModule: "Workout & Progress",
            targetModule: "Identity & Accounts",
            sourceSymbolOrPath: "AddMeasurementsAsync",
            targetSymbolOrPath: "LgymApi.Domain.Entities.User",
            rationale: "Pre-existing cross-module entity/repository coupling tracked as shrink-only debt while explicit contracts/read models/events are introduced.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "CrossModuleEntityLeakage",
            sourceModule: "Workout & Progress",
            targetModule: "Identity & Accounts",
            sourceSymbolOrPath: "AddTrainingAsync",
            targetSymbolOrPath: "LgymApi.Application.Repositories.IUserRepository",
            rationale: "Pre-existing cross-module entity/repository coupling tracked as shrink-only debt while explicit contracts/read models/events are introduced.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "CrossModuleEntityLeakage",
            sourceModule: "Workout & Progress",
            targetModule: "Identity & Accounts",
            sourceSymbolOrPath: "CreateExerciseAsync",
            targetSymbolOrPath: "LgymApi.Application.Repositories.IUserRepository",
            rationale: "Pre-existing cross-module entity/repository coupling tracked as shrink-only debt while explicit contracts/read models/events are introduced.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "CrossModuleEntityLeakage",
            sourceModule: "Workout & Progress",
            targetModule: "Identity & Accounts",
            sourceSymbolOrPath: "DeleteExerciseAsync",
            targetSymbolOrPath: "LgymApi.Application.Repositories.IRoleRepository",
            rationale: "Pre-existing cross-module entity/repository coupling tracked as shrink-only debt while explicit contracts/read models/events are introduced.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "CrossModuleEntityLeakage",
            sourceModule: "Workout & Progress",
            targetModule: "Identity & Accounts",
            sourceSymbolOrPath: "DeleteExerciseAsync",
            targetSymbolOrPath: "LgymApi.Application.Repositories.IUserRepository",
            rationale: "Pre-existing cross-module entity/repository coupling tracked as shrink-only debt while explicit contracts/read models/events are introduced.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "CrossModuleEntityLeakage",
            sourceModule: "Workout & Progress",
            targetModule: "Identity & Accounts",
            sourceSymbolOrPath: "DeleteGymAsync",
            targetSymbolOrPath: "LgymApi.Domain.Entities.User",
            rationale: "Pre-existing cross-module entity/repository coupling tracked as shrink-only debt while explicit contracts/read models/events are introduced.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "CrossModuleEntityLeakage",
            sourceModule: "Workout & Progress",
            targetModule: "Identity & Accounts",
            sourceSymbolOrPath: "ExerciseService",
            targetSymbolOrPath: "LgymApi.Application.Repositories.IRoleRepository",
            rationale: "Pre-existing cross-module entity/repository coupling tracked as shrink-only debt while explicit contracts/read models/events are introduced.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "CrossModuleEntityLeakage",
            sourceModule: "Workout & Progress",
            targetModule: "Identity & Accounts",
            sourceSymbolOrPath: "ExerciseService",
            targetSymbolOrPath: "LgymApi.Application.Repositories.IUserRepository",
            rationale: "Pre-existing cross-module entity/repository coupling tracked as shrink-only debt while explicit contracts/read models/events are introduced.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "CrossModuleEntityLeakage",
            sourceModule: "Workout & Progress",
            targetModule: "Identity & Accounts",
            sourceSymbolOrPath: "GetAllExercisesAsync",
            targetSymbolOrPath: "LgymApi.Application.Repositories.IUserRepository",
            rationale: "Pre-existing cross-module entity/repository coupling tracked as shrink-only debt while explicit contracts/read models/events are introduced.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "CrossModuleEntityLeakage",
            sourceModule: "Workout & Progress",
            targetModule: "Identity & Accounts",
            sourceSymbolOrPath: "GetAllUserExercisesAsync",
            targetSymbolOrPath: "LgymApi.Application.Repositories.IUserRepository",
            rationale: "Pre-existing cross-module entity/repository coupling tracked as shrink-only debt while explicit contracts/read models/events are introduced.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "CrossModuleEntityLeakage",
            sourceModule: "Workout & Progress",
            targetModule: "Identity & Accounts",
            sourceSymbolOrPath: "GetExerciseByBodyPartAsync",
            targetSymbolOrPath: "LgymApi.Application.Repositories.IUserRepository",
            rationale: "Pre-existing cross-module entity/repository coupling tracked as shrink-only debt while explicit contracts/read models/events are introduced.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "CrossModuleEntityLeakage",
            sourceModule: "Workout & Progress",
            targetModule: "Identity & Accounts",
            sourceSymbolOrPath: "GetGymAsync",
            targetSymbolOrPath: "LgymApi.Domain.Entities.User",
            rationale: "Pre-existing cross-module entity/repository coupling tracked as shrink-only debt while explicit contracts/read models/events are introduced.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "CrossModuleEntityLeakage",
            sourceModule: "Workout & Progress",
            targetModule: "Identity & Accounts",
            sourceSymbolOrPath: "GetGymsAsync",
            targetSymbolOrPath: "LgymApi.Domain.Entities.User",
            rationale: "Pre-existing cross-module entity/repository coupling tracked as shrink-only debt while explicit contracts/read models/events are introduced.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "CrossModuleEntityLeakage",
            sourceModule: "Workout & Progress",
            targetModule: "Identity & Accounts",
            sourceSymbolOrPath: "GetMeasurementDetailAsync",
            targetSymbolOrPath: "LgymApi.Domain.Entities.User",
            rationale: "Pre-existing cross-module entity/repository coupling tracked as shrink-only debt while explicit contracts/read models/events are introduced.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "CrossModuleEntityLeakage",
            sourceModule: "Workout & Progress",
            targetModule: "Identity & Accounts",
            sourceSymbolOrPath: "GetMeasurementsHistoryAsync",
            targetSymbolOrPath: "LgymApi.Domain.Entities.User",
            rationale: "Pre-existing cross-module entity/repository coupling tracked as shrink-only debt while explicit contracts/read models/events are introduced.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "CrossModuleEntityLeakage",
            sourceModule: "Workout & Progress",
            targetModule: "Identity & Accounts",
            sourceSymbolOrPath: "GetMeasurementsListAsync",
            targetSymbolOrPath: "LgymApi.Domain.Entities.User",
            rationale: "Pre-existing cross-module entity/repository coupling tracked as shrink-only debt while explicit contracts/read models/events are introduced.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "CrossModuleEntityLeakage",
            sourceModule: "Workout & Progress",
            targetModule: "Identity & Accounts",
            sourceSymbolOrPath: "GetMeasurementsTrendAsync",
            targetSymbolOrPath: "LgymApi.Domain.Entities.User",
            rationale: "Pre-existing cross-module entity/repository coupling tracked as shrink-only debt while explicit contracts/read models/events are introduced.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "CrossModuleEntityLeakage",
            sourceModule: "Workout & Progress",
            targetModule: "Identity & Accounts",
            sourceSymbolOrPath: "GetMeasurementsTrendsAsync",
            targetSymbolOrPath: "LgymApi.Domain.Entities.User",
            rationale: "Pre-existing cross-module entity/repository coupling tracked as shrink-only debt while explicit contracts/read models/events are introduced.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "CrossModuleEntityLeakage",
            sourceModule: "Workout & Progress",
            targetModule: "Identity & Accounts",
            sourceSymbolOrPath: "LgymApi.Application.Features.Exercise.ExerciseService.UpdateExerciseRequest",
            targetSymbolOrPath: "LgymApi.Domain.Entities.User",
            rationale: "Pre-existing cross-module entity/repository coupling tracked as shrink-only debt while explicit contracts/read models/events are introduced.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "CrossModuleEntityLeakage",
            sourceModule: "Workout & Progress",
            targetModule: "Identity & Accounts",
            sourceSymbolOrPath: "LgymApi.Application/Exercise/ExerciseService.Management.cs",
            targetSymbolOrPath: "LgymApi.Domain.Entities.User",
            rationale: "Pre-existing cross-module entity/repository coupling tracked as shrink-only debt while explicit contracts/read models/events are introduced.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "CrossModuleEntityLeakage",
            sourceModule: "Workout & Progress",
            targetModule: "Identity & Accounts",
            sourceSymbolOrPath: "LgymApi.Application/Exercise/ExerciseService.Queries.cs",
            targetSymbolOrPath: "LgymApi.Domain.Entities.User",
            rationale: "Pre-existing cross-module entity/repository coupling tracked as shrink-only debt while explicit contracts/read models/events are introduced.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "CrossModuleEntityLeakage",
            sourceModule: "Workout & Progress",
            targetModule: "Identity & Accounts",
            sourceSymbolOrPath: "LgymApi.Application/Exercise/ExerciseService.Scores.cs",
            targetSymbolOrPath: "LgymApi.Domain.Entities.User",
            rationale: "Pre-existing cross-module entity/repository coupling tracked as shrink-only debt while explicit contracts/read models/events are introduced.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "CrossModuleEntityLeakage",
            sourceModule: "Workout & Progress",
            targetModule: "Identity & Accounts",
            sourceSymbolOrPath: "LgymApi.Application/Exercise/IExerciseService.cs",
            targetSymbolOrPath: "LgymApi.Domain.Entities.User",
            rationale: "Pre-existing cross-module entity/repository coupling tracked as shrink-only debt while explicit contracts/read models/events are introduced.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "CrossModuleEntityLeakage",
            sourceModule: "Workout & Progress",
            targetModule: "Identity & Accounts",
            sourceSymbolOrPath: "LgymApi.Application/Gym/GymService.cs",
            targetSymbolOrPath: "LgymApi.Domain.Entities.User",
            rationale: "Pre-existing cross-module entity/repository coupling tracked as shrink-only debt while explicit contracts/read models/events are introduced.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "CrossModuleEntityLeakage",
            sourceModule: "Workout & Progress",
            targetModule: "Identity & Accounts",
            sourceSymbolOrPath: "LgymApi.Application/Gym/IGymService.cs",
            targetSymbolOrPath: "LgymApi.Domain.Entities.User",
            rationale: "Pre-existing cross-module entity/repository coupling tracked as shrink-only debt while explicit contracts/read models/events are introduced.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "CrossModuleEntityLeakage",
            sourceModule: "Workout & Progress",
            targetModule: "Identity & Accounts",
            sourceSymbolOrPath: "LgymApi.Application/Measurements/IMeasurementsService.cs",
            targetSymbolOrPath: "LgymApi.Domain.Entities.User",
            rationale: "Pre-existing cross-module entity/repository coupling tracked as shrink-only debt while explicit contracts/read models/events are introduced.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "CrossModuleEntityLeakage",
            sourceModule: "Workout & Progress",
            targetModule: "Identity & Accounts",
            sourceSymbolOrPath: "LgymApi.Application/Measurements/MeasurementsService.cs",
            targetSymbolOrPath: "LgymApi.Domain.Entities.User",
            rationale: "Pre-existing cross-module entity/repository coupling tracked as shrink-only debt while explicit contracts/read models/events are introduced.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "CrossModuleEntityLeakage",
            sourceModule: "Workout & Progress",
            targetModule: "Identity & Accounts",
            sourceSymbolOrPath: "LgymApi.Application/Training/ITrainingService.cs",
            targetSymbolOrPath: "LgymApi.Domain.Entities.User",
            rationale: "Pre-existing cross-module entity/repository coupling tracked as shrink-only debt while explicit contracts/read models/events are introduced.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "CrossModuleEntityLeakage",
            sourceModule: "Workout & Progress",
            targetModule: "Identity & Accounts",
            sourceSymbolOrPath: "MainRecordsServiceDependencies",
            targetSymbolOrPath: "LgymApi.Application.Repositories.IUserRepository",
            rationale: "Pre-existing cross-module entity/repository coupling tracked as shrink-only debt while explicit contracts/read models/events are introduced.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "CrossModuleEntityLeakage",
            sourceModule: "Workout & Progress",
            targetModule: "Identity & Accounts",
            sourceSymbolOrPath: "TrainingServiceDependencies",
            targetSymbolOrPath: "LgymApi.Application.Repositories.IUserRepository",
            rationale: "Pre-existing cross-module entity/repository coupling tracked as shrink-only debt while explicit contracts/read models/events are introduced.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "CrossModuleEntityLeakage",
            sourceModule: "Workout & Progress",
            targetModule: "Identity & Accounts",
            sourceSymbolOrPath: "UpdateExerciseAsync",
            targetSymbolOrPath: "LgymApi.Domain.Entities.User",
            rationale: "Pre-existing cross-module entity/repository coupling tracked as shrink-only debt while explicit contracts/read models/events are introduced.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "CrossModuleEntityLeakage",
            sourceModule: "Workout & Progress",
            targetModule: "Identity & Accounts",
            sourceSymbolOrPath: "UpdateExerciseCoreAsync",
            targetSymbolOrPath: "LgymApi.Application.Repositories.IRoleRepository",
            rationale: "Pre-existing cross-module entity/repository coupling tracked as shrink-only debt while explicit contracts/read models/events are introduced.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "CrossModuleEntityLeakage",
            sourceModule: "Workout & Progress",
            targetModule: "Identity & Accounts",
            sourceSymbolOrPath: "UpdateExerciseCoreAsync",
            targetSymbolOrPath: "LgymApi.Domain.Entities.User",
            rationale: "Pre-existing cross-module entity/repository coupling tracked as shrink-only debt while explicit contracts/read models/events are introduced.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "CrossModuleEntityLeakage",
            sourceModule: "Workout & Progress",
            targetModule: "Identity & Accounts",
            sourceSymbolOrPath: "UpdateExerciseWithFormulaAsync",
            targetSymbolOrPath: "LgymApi.Domain.Entities.User",
            rationale: "Pre-existing cross-module entity/repository coupling tracked as shrink-only debt while explicit contracts/read models/events are introduced.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "CrossModuleEntityLeakage",
            sourceModule: "Workout & Progress",
            targetModule: "Identity & Accounts",
            sourceSymbolOrPath: "UpdateGymAsync",
            targetSymbolOrPath: "LgymApi.Domain.Entities.User",
            rationale: "Pre-existing cross-module entity/repository coupling tracked as shrink-only debt while explicit contracts/read models/events are introduced.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "CrossModuleEntityLeakage",
            sourceModule: "Workout & Progress",
            targetModule: "Identity & Accounts",
            sourceSymbolOrPath: "UserRepository",
            targetSymbolOrPath: "LgymApi.Application.Repositories.IUserRepository",
            rationale: "Pre-existing cross-module entity/repository coupling tracked as shrink-only debt while explicit contracts/read models/events are introduced.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "CrossModuleEntityLeakage",
            sourceModule: "Workout & Progress",
            targetModule: "Identity & Accounts",
            sourceSymbolOrPath: "ValidateAccessAsync",
            targetSymbolOrPath: "LgymApi.Domain.Entities.User",
            rationale: "Pre-existing cross-module entity/repository coupling tracked as shrink-only debt while explicit contracts/read models/events are introduced.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "CrossModuleEntityLeakage",
            sourceModule: "Workout & Progress",
            targetModule: "Identity & Accounts",
            sourceSymbolOrPath: "_roleRepository",
            targetSymbolOrPath: "LgymApi.Application.Repositories.IRoleRepository",
            rationale: "Pre-existing cross-module entity/repository coupling tracked as shrink-only debt while explicit contracts/read models/events are introduced.")),
        new ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey.Create(
            guardId: "CrossModuleEntityLeakage",
            sourceModule: "Workout & Progress",
            targetModule: "Identity & Accounts",
            sourceSymbolOrPath: "_userRepository",
            targetSymbolOrPath: "LgymApi.Application.Repositories.IUserRepository",
            rationale: "Pre-existing cross-module entity/repository coupling tracked as shrink-only debt while explicit contracts/read models/events are introduced.")),
    ];

    public static IReadOnlyList<ModuleBoundaryDebtEntry> AllEntries
    {
        get
        {
            AssertRegistryDoesNotGrow();
            return Entries;
        }
    }

    public static IReadOnlyList<ModuleBoundaryDebtEntry> GetEntriesForGuard(string guardId)
    {
        AssertRegistryDoesNotGrow();
        var normalizedGuardId = ModuleBoundaryDebtKey.NormalizeRequiredValue(guardId, nameof(guardId));

        return Entries
            .Where(entry => entry.Key.GuardId.Equals(normalizedGuardId, StringComparison.Ordinal))
            .ToList();
    }

    public static ModuleBoundaryDebtAllowlistEvaluation Evaluate(string guardId, IEnumerable<ModuleBoundaryObservedViolation> observedViolations)
    {
        ArgumentNullException.ThrowIfNull(observedViolations);

        var normalizedGuardId = ModuleBoundaryDebtKey.NormalizeRequiredValue(guardId, nameof(guardId));
        var allowlistedEntries = GetEntriesForGuard(normalizedGuardId);

        return ModuleBoundaryDebtAllowlistEvaluator.Evaluate(allowlistedEntries, observedViolations, normalizedGuardId);
    }

    public static void AssertNoUnexpectedViolations(string guardId, IEnumerable<ModuleBoundaryObservedViolation> observedViolations)
    {
        var evaluation = Evaluate(guardId, observedViolations);
        if (evaluation.IsSuccess)
        {
            return;
        }

        throw new AssertionException(evaluation.BuildFailureMessage());
    }

    private static void AssertRegistryDoesNotGrow()
    {
        if (Entries.Count <= MaximumAllowedEntryCount)
        {
            return;
        }

        throw new AssertionException(
            $"Module-boundary debt allowlist contains {Entries.Count} entries, but the approved baseline is {MaximumAllowedEntryCount}; debt must not grow.");
    }
}

public static class ModuleBoundaryDebtAllowlistEvaluator
{
    public static ModuleBoundaryDebtAllowlistEvaluation Evaluate(
        IEnumerable<ModuleBoundaryDebtEntry> allowlistedEntries,
        IEnumerable<ModuleBoundaryObservedViolation> observedViolations,
        string? guardId = null)
    {
        ArgumentNullException.ThrowIfNull(allowlistedEntries);
        ArgumentNullException.ThrowIfNull(observedViolations);

        var entries = allowlistedEntries.ToList();
        var observed = observedViolations.ToList();
        var normalizedGuardId = NormalizeGuardId(guardId, entries, observed);

        ValidateEntries(entries, normalizedGuardId);
        ValidateObservedViolations(observed, normalizedGuardId);

        var matchedObservedKeys = observed
            .Select(violation => violation.IdentityKey)
            .ToHashSet(StringComparer.Ordinal);

        var unexpectedViolations = observed
            .Where(violation => entries.All(entry => !entry.Matches(violation)))
            .OrderBy(violation => violation.IdentityKey, StringComparer.Ordinal)
            .ToList();

        var staleEntries = entries
            .Where(entry => !matchedObservedKeys.Contains(entry.IdentityKey))
            .OrderBy(entry => entry.IdentityKey, StringComparer.Ordinal)
            .ToList();

        return new ModuleBoundaryDebtAllowlistEvaluation(normalizedGuardId, unexpectedViolations, staleEntries);
    }

    private static void ValidateEntries(IReadOnlyCollection<ModuleBoundaryDebtEntry> entries, string normalizedGuardId)
    {
        if (entries.Count > ModuleBoundaryDebtAllowlistRegistry.MaximumAllowedEntryCount)
        {
            throw new AssertionException(
                $"Module-boundary debt allowlist for guard '{normalizedGuardId}' contains {entries.Count} entries, but the approved baseline is {ModuleBoundaryDebtAllowlistRegistry.MaximumAllowedEntryCount}; debt must not grow.");
        }

        var broadEntries = entries
            .Where(entry => entry.Key.ContainsWildcardIdentityValue())
            .Select(entry => entry.Key.ToDisplayString())
            .OrderBy(entry => entry, StringComparer.Ordinal)
            .ToList();

        if (broadEntries.Count > 0)
        {
            throw new AssertionException(
                $"Module-boundary debt allowlist for guard '{normalizedGuardId}' contains wildcard entries. Allowlist entries must identify one exact current violation:{Environment.NewLine}" +
                string.Join(Environment.NewLine, broadEntries));
        }

        var duplicateEntryKeys = entries
            .GroupBy(entry => entry.Key.NormalizedKey, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToList();

        if (duplicateEntryKeys.Count > 0)
        {
            throw new AssertionException(
                $"Module-boundary debt allowlist for guard '{normalizedGuardId}' contains duplicate exact entries:{Environment.NewLine}" +
                string.Join(Environment.NewLine, duplicateEntryKeys));
        }

        var duplicateIdentityKeys = entries
            .GroupBy(entry => entry.IdentityKey, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToList();

        if (duplicateIdentityKeys.Count > 0)
        {
            throw new AssertionException(
                $"Module-boundary debt allowlist for guard '{normalizedGuardId}' contains duplicate identity matches. Keep one exact entry per live violation:{Environment.NewLine}" +
                string.Join(Environment.NewLine, duplicateIdentityKeys));
        }
    }

    private static void ValidateObservedViolations(IReadOnlyCollection<ModuleBoundaryObservedViolation> observedViolations, string normalizedGuardId)
    {
        var invalidGuardViolations = observedViolations
            .Where(violation => !violation.GuardId.Equals(normalizedGuardId, StringComparison.Ordinal))
            .Select(violation => violation.IdentityKey)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToList();

        if (invalidGuardViolations.Count > 0)
        {
            throw new AssertionException(
                $"Observed module-boundary violations must use the requested guard id '{normalizedGuardId}':{Environment.NewLine}" +
                string.Join(Environment.NewLine, invalidGuardViolations));
        }
    }

    private static string NormalizeGuardId(
        string? guardId,
        IReadOnlyCollection<ModuleBoundaryDebtEntry> entries,
        IReadOnlyCollection<ModuleBoundaryObservedViolation> observedViolations)
    {
        if (!string.IsNullOrWhiteSpace(guardId))
        {
            return ModuleBoundaryDebtKey.NormalizeRequiredValue(guardId, nameof(guardId));
        }

        var discoveredGuardId = entries.Select(entry => entry.Key.GuardId)
            .Concat(observedViolations.Select(violation => violation.GuardId))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return discoveredGuardId.Count switch
        {
            0 => throw new AssertionException("Module-boundary debt allowlist evaluation requires a guard id when no entries or observed violations are present."),
            1 => discoveredGuardId[0],
            _ => throw new AssertionException(
                "Module-boundary debt allowlist evaluation requires exactly one guard id. Found:" + Environment.NewLine +
                string.Join(Environment.NewLine, discoveredGuardId.OrderBy(id => id, StringComparer.Ordinal)))
        };
    }
}

public sealed record ModuleBoundaryDebtAllowlistEvaluation(
    string GuardId,
    IReadOnlyList<ModuleBoundaryObservedViolation> UnexpectedViolations,
    IReadOnlyList<ModuleBoundaryDebtEntry> StaleEntries)
{
    public bool IsSuccess => UnexpectedViolations.Count == 0 && StaleEntries.Count == 0;

    public string BuildFailureMessage()
    {
        var sections = new List<string>();

        if (UnexpectedViolations.Count > 0)
        {
            sections.Add(
                "New module-boundary violations must be fixed or explicitly allowlisted as exact current debt:" + Environment.NewLine +
                string.Join(Environment.NewLine + Environment.NewLine, UnexpectedViolations.Select(violation => violation.ToString())));
        }

        if (StaleEntries.Count > 0)
        {
            sections.Add(
                "Stale module-boundary allowlist entries must be removed once the live violation disappears:" + Environment.NewLine +
                string.Join(Environment.NewLine + Environment.NewLine, StaleEntries.Select(entry => entry.ToString())));
        }

        return $"Module-boundary shrink-only debt allowlist failed for guard '{GuardId}'.{Environment.NewLine}" +
               string.Join(Environment.NewLine + Environment.NewLine, sections);
    }
}

public sealed record ModuleBoundaryDebtEntry(ModuleBoundaryDebtKey Key)
{
    public string IdentityKey => Key.IdentityKey;

    public bool Matches(ModuleBoundaryObservedViolation observedViolation)
    {
        ArgumentNullException.ThrowIfNull(observedViolation);
        return IdentityKey.Equals(observedViolation.IdentityKey, StringComparison.Ordinal);
    }

    public override string ToString() => Key.ToDisplayString();
}

public sealed record ModuleBoundaryObservedViolation(
    string GuardId,
    string SourceModule,
    string TargetModule,
    string SourceSymbolOrPath,
    string TargetSymbolOrPath)
{
    public string IdentityKey => ModuleBoundaryDebtKey.BuildIdentityKey(GuardId, SourceModule, TargetModule, SourceSymbolOrPath, TargetSymbolOrPath);

    public override string ToString()
    {
        return $"Rule: {GuardId}{Environment.NewLine}" +
               $"Source module: {SourceModule}{Environment.NewLine}" +
               $"Target module: {TargetModule}{Environment.NewLine}" +
               $"Source symbol/file: {SourceSymbolOrPath}{Environment.NewLine}" +
               $"Target symbol/file: {TargetSymbolOrPath}";
    }
}

public sealed record ModuleBoundaryDebtKey(
    string GuardId,
    string SourceModule,
    string TargetModule,
    string SourceSymbolOrPath,
    string TargetSymbolOrPath,
    string Rationale)
{
    public string IdentityKey => BuildIdentityKey(GuardId, SourceModule, TargetModule, SourceSymbolOrPath, TargetSymbolOrPath);

    public string NormalizedKey => $"{IdentityKey}|rationale:{Rationale}";

    public string ToDisplayString()
    {
        return $"Rule: {GuardId}{Environment.NewLine}" +
               $"Source module: {SourceModule}{Environment.NewLine}" +
               $"Target module: {TargetModule}{Environment.NewLine}" +
               $"Source symbol/file: {SourceSymbolOrPath}{Environment.NewLine}" +
               $"Target symbol/file: {TargetSymbolOrPath}{Environment.NewLine}" +
               $"Rationale: {Rationale}";
    }

    public static ModuleBoundaryDebtKey Create(
        string guardId,
        string sourceModule,
        string targetModule,
        string sourceSymbolOrPath,
        string targetSymbolOrPath,
        string rationale)
    {
        return new ModuleBoundaryDebtKey(
            NormalizeRequiredExactIdentityValue(guardId, nameof(guardId)),
            NormalizeRequiredExactIdentityValue(sourceModule, nameof(sourceModule)),
            NormalizeRequiredExactIdentityValue(targetModule, nameof(targetModule)),
            NormalizeRequiredExactPathOrSymbol(sourceSymbolOrPath, nameof(sourceSymbolOrPath)),
            NormalizeRequiredExactPathOrSymbol(targetSymbolOrPath, nameof(targetSymbolOrPath)),
            NormalizeRequiredValue(rationale, nameof(rationale)));
    }

    public static string BuildIdentityKey(
        string guardId,
        string sourceModule,
        string targetModule,
        string sourceSymbolOrPath,
        string targetSymbolOrPath)
    {
        return string.Join(
            "|",
            $"guard:{NormalizeRequiredExactIdentityValue(guardId, nameof(guardId))}",
            $"source-module:{NormalizeRequiredExactIdentityValue(sourceModule, nameof(sourceModule))}",
            $"target-module:{NormalizeRequiredExactIdentityValue(targetModule, nameof(targetModule))}",
            $"source:{NormalizeRequiredExactPathOrSymbol(sourceSymbolOrPath, nameof(sourceSymbolOrPath))}",
            $"target:{NormalizeRequiredExactPathOrSymbol(targetSymbolOrPath, nameof(targetSymbolOrPath))}");
    }

    public static string NormalizeRequiredPathOrSymbol(string value, string paramName)
    {
        return NormalizeRequiredValue(ArchitectureTestHelpers.NormalizePath(value), paramName);
    }

    internal bool ContainsWildcardIdentityValue()
    {
        return ContainsWildcard(GuardId) ||
               ContainsWildcard(SourceModule) ||
               ContainsWildcard(TargetModule) ||
               ContainsWildcard(SourceSymbolOrPath) ||
               ContainsWildcard(TargetSymbolOrPath);
    }

    private static string NormalizeRequiredExactPathOrSymbol(string value, string paramName)
    {
        return NormalizeRequiredExactIdentityValue(ArchitectureTestHelpers.NormalizePath(value), paramName);
    }

    private static string NormalizeRequiredExactIdentityValue(string value, string paramName)
    {
        var normalizedValue = NormalizeRequiredValue(value, paramName);
        if (ContainsWildcard(normalizedValue))
        {
            throw new ArgumentException($"{paramName} must identify one exact violation and cannot contain wildcards.", paramName);
        }

        return normalizedValue;
    }

    private static bool ContainsWildcard(string value)
    {
        return value.Contains('*') || value.Contains('?');
    }

    public static string NormalizeRequiredValue(string value, string paramName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, paramName);
        return value.Trim();
    }
}

public sealed class ModuleBoundaryDebtOwnerRekey
{
    private readonly ModuleBoundaryDebtEntry _existingEntry;
    private readonly string _sourceModule;
    private readonly string _targetModule;

    private ModuleBoundaryDebtOwnerRekey(
        ModuleBoundaryDebtEntry existingEntry,
        string sourceModule,
        string targetModule)
    {
        _existingEntry = existingEntry;
        _sourceModule = sourceModule;
        _targetModule = targetModule;
    }

    public static ModuleBoundaryDebtOwnerRekey FromCurrentViolation(
        ModuleBoundaryDebtEntry existingEntry,
        ModuleBoundaryObservedViolation currentViolation)
    {
        ArgumentNullException.ThrowIfNull(existingEntry);
        ArgumentNullException.ThrowIfNull(currentViolation);

        AssertUnchanged(
            ModuleBoundaryDebtKey.NormalizeRequiredValue(existingEntry.Key.GuardId, nameof(existingEntry.Key.GuardId)),
            ModuleBoundaryDebtKey.NormalizeRequiredValue(currentViolation.GuardId, nameof(currentViolation.GuardId)),
            "kind");
        AssertUnchanged(
            ModuleBoundaryDebtKey.NormalizeRequiredPathOrSymbol(existingEntry.Key.SourceSymbolOrPath, nameof(existingEntry.Key.SourceSymbolOrPath)),
            ModuleBoundaryDebtKey.NormalizeRequiredPathOrSymbol(currentViolation.SourceSymbolOrPath, nameof(currentViolation.SourceSymbolOrPath)),
            "source symbol/path");
        AssertUnchanged(
            ModuleBoundaryDebtKey.NormalizeRequiredPathOrSymbol(existingEntry.Key.TargetSymbolOrPath, nameof(existingEntry.Key.TargetSymbolOrPath)),
            ModuleBoundaryDebtKey.NormalizeRequiredPathOrSymbol(currentViolation.TargetSymbolOrPath, nameof(currentViolation.TargetSymbolOrPath)),
            "target symbol/path");

        return new ModuleBoundaryDebtOwnerRekey(
            existingEntry,
            ModuleBoundaryDebtKey.NormalizeRequiredValue(currentViolation.SourceModule, nameof(currentViolation.SourceModule)),
            ModuleBoundaryDebtKey.NormalizeRequiredValue(currentViolation.TargetModule, nameof(currentViolation.TargetModule)));
    }

    public ModuleBoundaryDebtEntry ToEntry()
    {
        return _existingEntry with
        {
            Key = ModuleBoundaryDebtKey.Create(
                _existingEntry.Key.GuardId,
                _sourceModule,
                _targetModule,
                _existingEntry.Key.SourceSymbolOrPath,
                _existingEntry.Key.TargetSymbolOrPath,
                _existingEntry.Key.Rationale)
        };
    }

    private static void AssertUnchanged(string existingValue, string currentValue, string fieldName)
    {
        if (existingValue.Equals(currentValue, StringComparison.Ordinal))
        {
            return;
        }

        throw new AssertionException($"A module-boundary debt re-key may only change owner metadata; the {fieldName} changed.");
    }
}
