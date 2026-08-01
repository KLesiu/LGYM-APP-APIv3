using LgymApi.Application.Repositories;
using LgymApi.Application.WorkoutProgress.Persistence;
using LgymApi.Infrastructure.Repositories;
using LgymApi.Infrastructure.Repositories.WorkoutProgress;
using Microsoft.Extensions.DependencyInjection;

namespace LgymApi.Infrastructure;

public static partial class ServiceCollectionExtensions
{
    public static IServiceCollection AddWorkoutProgressInfrastructure(this IServiceCollection services)
    {
        services.AddScoped<IEloRegistryRepository, EloRegistryRepository>();
        services.AddScoped<IMainRecordRepository, MainRecordRepository>();
        services.AddScoped<IExerciseRepository, ExerciseRepository>();
        services.AddScoped<ITrainingRepository, TrainingRepository>();
        services.AddScoped<ITrainingExerciseScoreRepository, TrainingExerciseScoreRepository>();
        services.AddScoped<IExerciseScoreRepository, ExerciseScoreRepository>();
        services.AddScoped<IMeasurementRepository, MeasurementRepository>();
        services.AddScoped<IGymRepository, GymRepository>();
        services.AddScoped<IWorkoutExercisePersistence, WorkoutExercisePersistenceRepository>();
        services.AddScoped<IWorkoutExerciseScorePersistence, WorkoutExerciseScorePersistenceRepository>();
        services.AddScoped<IWorkoutGymPersistence, WorkoutGymPersistenceRepository>();
        services.AddScoped<IWorkoutMeasurementPersistence, WorkoutMeasurementPersistenceRepository>();
        services.AddScoped<IWorkoutMainRecordPersistence, WorkoutMainRecordPersistenceRepository>();
        services.AddScoped<IWorkoutEloPersistence, WorkoutEloPersistenceRepository>();
        services.AddScoped<IWorkoutTrainingPersistence, WorkoutTrainingPersistenceRepository>();

        return services;
    }
}
