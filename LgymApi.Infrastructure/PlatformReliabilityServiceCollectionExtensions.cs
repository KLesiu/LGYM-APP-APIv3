using LgymApi.Application.Repositories;
using LgymApi.Application.Services;
using LgymApi.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;

namespace LgymApi.Infrastructure;

public static partial class ServiceCollectionExtensions
{
    private static void AddPlatformReliabilityDispatcher(IServiceCollection services)
    {
        services.AddScoped<ICommittedIntentDispatcher, CommittedIntentDispatcher>();
    }
}
