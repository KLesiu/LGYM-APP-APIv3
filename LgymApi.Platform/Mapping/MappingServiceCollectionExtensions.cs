using System.Reflection;
using LgymApi.Application.Mapping.Core;
using Microsoft.Extensions.DependencyInjection;

namespace LgymApi.Application.Mapping;

public static class MappingServiceCollectionExtensions
{
    public static IServiceCollection AddApplicationMapping(this IServiceCollection services, params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);

        var duplicateAssembly = assemblies
            .GroupBy(assembly => assembly.FullName, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicateAssembly != null)
        {
            throw new InvalidOperationException($"Mapping assembly '{duplicateAssembly.Key}' was supplied more than once.");
        }

        if (assemblies.Length != 6)
        {
            throw new ArgumentException("Mapping discovery requires exactly six module assemblies.", nameof(assemblies));
        }

        var profiles = assemblies
            .SelectMany(GetLoadableTypes)
            .Where(type => typeof(IMappingProfile).IsAssignableFrom(type))
            .Where(type => type is { IsAbstract: false, IsInterface: false })
            .ToList();

        foreach (var profileType in profiles)
        {
            services.AddSingleton(typeof(IMappingProfile), profileType);
        }

        services.AddSingleton<IMapper>(sp => new Mapper(sp.GetRequiredService<IEnumerable<IMappingProfile>>()));

        return services;
    }

    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t != null)!;
        }
    }
}
