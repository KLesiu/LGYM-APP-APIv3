using LgymApi.Application.Repositories;
using LgymApi.Application.Services;
using LgymApi.Infrastructure.Repositories;
using LgymApi.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;

namespace LgymApi.Infrastructure;

public static partial class ServiceCollectionExtensions
{
    private static void AddPlatformReliabilityDispatcher(IServiceCollection services)
    {
        services.AddScoped<ICommittedIntentDispatcher, CommittedIntentDispatcher>();
    }

    private static void AddPlatformReliabilityRepositories(IServiceCollection services)
    {
        services.AddScoped<ICommandEnvelopeRepository, CommandEnvelopeRepository>();
        services.AddScoped<IApiIdempotencyRecordRepository, ApiIdempotencyRecordRepository>();
    }
}
