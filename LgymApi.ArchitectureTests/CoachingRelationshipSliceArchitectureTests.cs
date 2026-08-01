using System.Reflection;
using FluentAssertions;
using LgymApi.Application.Coaching;
using LgymApi.Application.Coaching.Relationships.DetachFromTrainer;
using LgymApi.Application.Coaching.Relationships.GetCurrentTrainer;
using LgymApi.Application.Coaching.Relationships.UnlinkTrainee;
using Microsoft.Extensions.DependencyInjection;

namespace LgymApi.ArchitectureTests;

[TestFixture]
public sealed class CoachingRelationshipSliceArchitectureTests
{
    private static readonly (Type Contract, string ImplementationName)[] Slices =
    [
        (typeof(IUnlinkTraineeUseCase), "LgymApi.Application.Coaching.Relationships.UnlinkTrainee.UnlinkTraineeUseCase"),
        (typeof(IDetachFromTrainerUseCase), "LgymApi.Application.Coaching.Relationships.DetachFromTrainer.DetachFromTrainerUseCase"),
        (typeof(IGetCurrentTrainerUseCase), "LgymApi.Application.Coaching.Relationships.GetCurrentTrainer.GetCurrentTrainerUseCase")
    ];

    [Test]
    public void RelationshipSlices_ExposeOneMethodPublicContractsWithInternalImplementations()
    {
        var assembly = typeof(IUnlinkTraineeUseCase).Assembly;

        foreach (var slice in Slices)
        {
            slice.Contract.IsPublic.Should().BeTrue();
            slice.Contract.GetMethods(BindingFlags.Public | BindingFlags.Instance).Should().ContainSingle();
            assembly.GetType(slice.ImplementationName)!.IsNotPublic.Should().BeTrue();
        }
    }

    [Test]
    public void RelationshipSlices_AreRegisteredExactlyOnceByTheCoachingModule()
    {
        var services = new ServiceCollection();
        services.AddCoachingModule();

        foreach (var slice in Slices)
        {
            services.Count(descriptor => descriptor.ServiceType == slice.Contract).Should().Be(1);
        }
    }

    [Test]
    public void RelationshipSlicePublicModels_AreSealedRecordsWithTypedIdentifiers()
    {
        typeof(UnlinkTraineeCommand).IsSealed.Should().BeTrue();
        typeof(DetachFromTrainerCommand).IsSealed.Should().BeTrue();
        typeof(GetCurrentTrainerQuery).IsSealed.Should().BeTrue();
        typeof(CurrentTrainerReadModel).IsSealed.Should().BeTrue();

        typeof(UnlinkTraineeCommand).GetProperties().Should().OnlyContain(property => IsUserId(property.PropertyType));
        typeof(DetachFromTrainerCommand).GetProperties().Should().OnlyContain(property => IsUserId(property.PropertyType));
        typeof(GetCurrentTrainerQuery).GetProperties().Should().OnlyContain(property => IsUserId(property.PropertyType));
        typeof(CurrentTrainerReadModel).GetProperty(nameof(CurrentTrainerReadModel.TrainerId))!.PropertyType
            .Should().Be(typeof(LgymApi.Domain.ValueObjects.Id<LgymApi.Domain.Entities.User>));
    }

    [Test]
    public void RelationshipSlices_DoNotDependOnLegacyUserOrRelationshipRepositories()
    {
        var root = ArchitectureTestHelpers.ResolveRepositoryRoot();
        var relationshipDirectory = Path.Combine(root, "LgymApi.Application", "Coaching", "Relationships");
        var source = Directory.GetFiles(relationshipDirectory, "*.cs", SearchOption.AllDirectories)
            .Select(File.ReadAllText)
            .ToArray();

        source.Should().NotContain(text => text.Contains("IUserRepository", StringComparison.Ordinal));
        source.Should().NotContain(text => text.Contains("ITrainerRelationshipRepository", StringComparison.Ordinal));
        source.Should().NotContain(text => text.Contains(" User ", StringComparison.Ordinal));
    }

    [Test]
    public void GetCurrentTrainer_DelegatesProfileTransformationToTheRegisteredMapper()
    {
        var root = ArchitectureTestHelpers.ResolveRepositoryRoot();
        var useCase = Path.Combine(
            root,
            "LgymApi.Application",
            "Coaching",
            "Relationships",
            "GetCurrentTrainer",
            "GetCurrentTrainerUseCase.cs");
        var mappingProfile = Path.Combine(
            root,
            "LgymApi.Application",
            "Coaching",
            "Relationships",
            "RelationshipMappingProfile.cs");
        var source = File.ReadAllText(useCase);

        source.Should().NotContain("new CurrentTrainerReadModel(");
        source.Should().Contain("_mapper.Map<CurrentTrainerSource, CurrentTrainerReadModel>(");
        source.Should().Contain("_mapper.CreateContext()");
        File.ReadAllText(mappingProfile).Should().Contain("CreateMap<CurrentTrainerSource, CurrentTrainerReadModel>");
    }

    private static bool IsUserId(Type type)
        => type.IsGenericType
            && type.GetGenericTypeDefinition() == typeof(LgymApi.Domain.ValueObjects.Id<>)
            && type.GetGenericArguments().Single() == typeof(LgymApi.Domain.Entities.User);
}
