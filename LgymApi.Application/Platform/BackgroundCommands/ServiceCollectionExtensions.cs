using LgymApi.Application.Platform.Contracts.BackgroundCommands;
using Microsoft.Extensions.DependencyInjection;

namespace LgymApi.Application.Platform.BackgroundCommands;

internal static class ServiceCollectionExtensions
{
    internal static IServiceCollection AddCommandEnvelopeRuntime(this IServiceCollection services)
    {
        services.AddScoped<ICommandEnvelopeRuntime, CommandEnvelopeRuntime>();
        return services;
    }
}
