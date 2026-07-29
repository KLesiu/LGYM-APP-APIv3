using System.Reflection;
using FluentAssertions;
using LgymApi.Application.Mapping;
using Microsoft.Extensions.DependencyInjection;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class ModuleMappingDiscoveryFailureTests
{
    public static IEnumerable<TestCaseData> RequiredAssemblies()
    {
        return LgymApi.Api.Mapping.MappingAssemblyMarkers.All
            .Select(assembly => new TestCaseData(assembly).SetName($"Missing_{assembly.GetName().Name}_Fails"));
    }

    [TestCaseSource(nameof(RequiredAssemblies))]
    public void AddApplicationMapping_WhenARequiredModuleAssemblyIsMissing_Throws(Assembly omittedAssembly)
    {
        var assemblies = LgymApi.Api.Mapping.MappingAssemblyMarkers.All
            .Where(assembly => assembly != omittedAssembly)
            .ToArray();
        var services = new ServiceCollection();

        var action = () => services.AddApplicationMapping(assemblies);

        action.Should().Throw<ArgumentException>()
            .WithMessage("Mapping discovery requires exactly six module assemblies.*");
    }

    [Test]
    public void AddApplicationMapping_WhenAnAssemblyIsDuplicated_Throws()
    {
        var assemblies = LgymApi.Api.Mapping.MappingAssemblyMarkers.All
            .Append(LgymApi.Api.Mapping.MappingAssemblyMarkers.All[0])
            .ToArray();
        var services = new ServiceCollection();

        var action = () => services.AddApplicationMapping(assemblies);

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("Mapping assembly '*' was supplied more than once.");
    }
}
