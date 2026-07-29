using LgymApi.Application.WorkoutProgress.Scoring.Elo;
using LgymApi.Application.Features.Exercise;
using LgymApi.Application.Features.EloRegistry;
using LgymApi.Application.Features.Gym;
using LgymApi.Application.Features.Measurements;
using LgymApi.Application.Features.MainRecords;
using LgymApi.Application.Features.ExerciseScores;
using LgymApi.Application.Features.Training;
using LgymApi.Application.Repositories;
using LgymApi.Application.Platform.ReferenceData.Units;
using LgymApi.Application.WorkoutProgress.ProgressData;
using LgymApi.Application.WorkoutProgress.Dashboard;
using LgymApi.Application.WorkoutProgress.Ranking;
using LgymApi.Application.WorkoutProgress.TrainingExecution;
using LgymApi.Application.WorkoutProgress.Contracts.ReportingIntegration;
using LgymApi.Application.TrainingPlanning.Contracts.PlanDay;
using LgymApi.Application.WorkoutProgress.Adapters;
using LgymApi.Application.WorkoutProgress.Contracts.BackgroundActions;
using LgymApi.Application.WorkoutProgress.BackgroundActions;
using LgymApi.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using CompleteTrainingDependencies = LgymApi.Application.WorkoutProgress.TrainingExecution.TrainingServiceDependencies;
using LegacyTrainingServiceDependencies = LgymApi.Application.Features.Training.TrainingServiceDependencies;

namespace LgymApi.Application.WorkoutProgress;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddWorkoutAndProgressModule(this IServiceCollection services)
    {
        services.AddScoped<WorkoutProgressReadWriteServiceDependencies>();
        services.AddScoped<IWorkoutProgressReadWriteService, WorkoutProgressReadWriteService>();
        services.AddScoped<IWorkoutProgressDashboardReadService, WorkoutProgressDashboardReadService>();
        services.AddScoped<IWorkoutProgressRankingReadService, WorkoutProgressRankingReadService>();
        services.AddScoped<IReportSubmissionAcceptedProgressConsumer, ReportSubmissionAcceptedProgressConsumer>();
        services.AddScoped<IReportSubmissionAcceptedProgressActionExecutionPort, ReportSubmissionAcceptedProgressActionExecutionPort>();
        services.AddScoped<TrainingCompletedExercisePreparationData>();
        services.AddScoped<ITrainingCompletedEmailPreparationPort, TrainingCompletedEmailPreparationService>();
        services.AddScoped<TrainingMainRecordsData>();
        services.AddScoped<ITrainingMainRecordsUpdatePort, TrainingMainRecordsUpdateService>();
        services.AddScoped<IExerciseService, ExerciseService>();
        services.AddScoped<IExerciseScoresService, ExerciseScoresService>();
        services.AddScoped<IEloRegistryService, EloRegistryService>();
        services.AddScoped<IGymService, GymService>();
        services.AddScoped<IMeasurementsServiceDependencies, MeasurementsServiceDependencies>();
        services.AddScoped<IMeasurementsService, MeasurementsService>();
        services.AddScoped<IMainRecordsService, MainRecordsService>();
        services.AddScoped<ICompleteTrainingUseCaseDependencies, CompleteTrainingDependencies>();
        services.AddScoped<ICompleteTrainingUseCase, CompleteTrainingUseCase>();
        services.AddScoped<ITrainingHistoryReadServiceDependencies, TrainingHistoryReadServiceDependencies>();
        services.AddScoped<ITrainingHistoryReadService, TrainingHistoryReadService>();
        services.AddScoped<ITrainingServiceDependencies, LegacyTrainingServiceDependencies>();
        services.AddScoped<ITrainingService, TrainingService>();
        services.AddScoped<IPlanExerciseCatalogPort, PlanExerciseCatalogAdapter>();
        services.AddScoped<IPlanExerciseClonePort, PlanExerciseCloneAdapter>();
        services.AddScoped<IPlanTrainingActivityPort, PlanTrainingActivityAdapter>();
        services.AddScoped<IExerciseEloCalculator, StandardExerciseEloCalculator>();
        services.AddScoped<IExerciseEloCalculator, StrengthWeightedExerciseEloCalculator>();
        services.AddScoped<IExerciseEloCalculator, VolumeWeightedExerciseEloCalculator>();
        services.AddScoped<IExerciseEloCalculator, PullupWeightedExerciseEloCalculator>();

        return services;
    }
}
