using LgymApi.Application.Coaching.Persistence;
using LgymApi.Infrastructure.Repositories.Coaching;
using Microsoft.Extensions.DependencyInjection;

namespace LgymApi.Infrastructure;

public static partial class ServiceCollectionExtensions
{
    public static IServiceCollection AddCoachingInfrastructure(this IServiceCollection services)
    {
        services.AddScoped<ICoachingInvitationPersistence, CoachingInvitationPersistenceRepository>();
        services.AddScoped<ICoachingActiveLinkPersistence, CoachingActiveLinkPersistenceRepository>();
        services.AddScoped<ICoachingFactReader, CoachingFactReader>();
        services.AddScoped<ICoachingTraineeNotePersistence, CoachingTraineeNotePersistenceRepository>();

        return services;
    }
}
