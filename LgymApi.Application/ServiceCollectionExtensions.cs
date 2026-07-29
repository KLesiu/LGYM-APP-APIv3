using LgymApi.Application.Coaching;
using LgymApi.Application.Nutrition;
using LgymApi.Application.Reporting;
using LgymApi.Application.WorkoutProgress;
using LgymApi.Application.Platform.BackgroundCommands;
using Microsoft.Extensions.DependencyInjection;

namespace LgymApi.Application;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddCommandEnvelopeRuntime();
        services.AddReportingModule();
        services.AddWorkoutAndProgressModule();
        services.AddCoachingModule();
        services.AddNutritionModule();

        return services;
    }
}
