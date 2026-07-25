using System.Reflection;
using FluentAssertions;
using LgymApi.Application.Coaching;
using LgymApi.Application.Coaching.ManagedPlans.Assign;
using LgymApi.Application.Coaching.ManagedPlans.Create;
using LgymApi.Application.Coaching.ManagedPlans.Delete;
using LgymApi.Application.Coaching.ManagedPlans.GetActive;
using LgymApi.Application.Coaching.ManagedPlans.List;
using LgymApi.Application.Coaching.ManagedPlans.Unassign;
using LgymApi.Application.Coaching.ManagedPlans.Update;
using LgymApi.Application.Repositories;
using LgymApi.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;

namespace LgymApi.ArchitectureTests;

[TestFixture]
public sealed class CoachingManagedPlanSliceArchitectureTests
{
    private static readonly (Type Contract, string ImplementationName)[] Slices =
    [
        (typeof(IListManagedPlansUseCase), "LgymApi.Application.Coaching.ManagedPlans.List.ListManagedPlansUseCase"),
        (typeof(LgymApi.Application.Coaching.ManagedPlans.Create.ICreateTraineeManagedPlanUseCase), "LgymApi.Application.Coaching.ManagedPlans.Create.CreateTraineeManagedPlanUseCase"),
        (typeof(LgymApi.Application.Coaching.ManagedPlans.Update.IUpdateTraineeManagedPlanUseCase), "LgymApi.Application.Coaching.ManagedPlans.Update.UpdateTraineeManagedPlanUseCase"),
        (typeof(LgymApi.Application.Coaching.ManagedPlans.Delete.IDeleteTraineeManagedPlanUseCase), "LgymApi.Application.Coaching.ManagedPlans.Delete.DeleteTraineeManagedPlanUseCase"),
        (typeof(LgymApi.Application.Coaching.ManagedPlans.Assign.IAssignTraineeManagedPlanUseCase), "LgymApi.Application.Coaching.ManagedPlans.Assign.AssignTraineeManagedPlanUseCase"),
        (typeof(LgymApi.Application.Coaching.ManagedPlans.Unassign.IUnassignTraineeManagedPlanUseCase), "LgymApi.Application.Coaching.ManagedPlans.Unassign.UnassignTraineeManagedPlanUseCase"),
        (typeof(IGetActiveManagedPlanUseCase), "LgymApi.Application.Coaching.ManagedPlans.GetActive.GetActiveManagedPlanUseCase")
    ];

    [Test]
    public void ManagedPlanSlices_ExposeOneMethodContractsWithInternalImplementations()
    {
        var assembly = typeof(IListManagedPlansUseCase).Assembly;

        foreach (var slice in Slices)
        {
            slice.Contract.IsPublic.Should().BeTrue();
            slice.Contract.GetMethods(BindingFlags.Public | BindingFlags.Instance).Should().ContainSingle();
            assembly.GetType(slice.ImplementationName)!.IsNotPublic.Should().BeTrue();
        }
    }

    [Test]
    public void ManagedPlanSlices_AreRegisteredExactlyOnceByCoachingModule()
    {
        var services = new ServiceCollection();
        services.AddCoachingModule();

        foreach (var slice in Slices)
        {
            services.Count(descriptor => descriptor.ServiceType == slice.Contract).Should().Be(1);
        }
    }

    [Test]
    public void ManagedPlanPublicInputs_AreSealedRecordsWithOnlyScalarsAndTypedIdentifiers()
    {
        var models = new[]
        {
            typeof(ListManagedPlansQuery),
            typeof(LgymApi.Application.Coaching.ManagedPlans.Create.CreateTraineeManagedPlanCommand),
            typeof(LgymApi.Application.Coaching.ManagedPlans.Update.UpdateTraineeManagedPlanCommand),
            typeof(LgymApi.Application.Coaching.ManagedPlans.Delete.DeleteTraineeManagedPlanCommand),
            typeof(LgymApi.Application.Coaching.ManagedPlans.Assign.AssignTraineeManagedPlanCommand),
            typeof(LgymApi.Application.Coaching.ManagedPlans.Unassign.UnassignTraineeManagedPlanCommand),
            typeof(GetActiveManagedPlanQuery)
        };

        foreach (var model in models)
        {
            model.IsSealed.Should().BeTrue();
            model.GetProperties().Select(property => property.PropertyType).Should().OnlyContain(type => IsAllowedInputType(type));
        }
    }

    [Test]
    public void ManagedPlanSlices_DoNotExposeOrAccessForeignRepositoriesOrEntityValues()
    {
        var assembly = typeof(IListManagedPlansUseCase).Assembly;
        var managedPlanTypes = assembly.GetTypes()
            .Where(type => type.Namespace?.StartsWith("LgymApi.Application.Coaching.ManagedPlans", StringComparison.Ordinal) == true)
            .ToArray();
        var exposedTypes = managedPlanTypes
            .SelectMany(type => type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Select(field => field.FieldType)
                .Concat(type.GetProperties().Select(property => property.PropertyType))
                .Concat(type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    .SelectMany(method => method.GetParameters().Select(parameter => parameter.ParameterType))))
            .ToArray();

        exposedTypes.Should().NotContain(typeof(IPlanRepository));
        exposedTypes.Should().NotContain(typeof(IUserRepository));
        exposedTypes.Should().NotContain(typeof(Plan));
        exposedTypes.Should().NotContain(typeof(User));

        var root = ArchitectureTestHelpers.ResolveRepositoryRoot();
        var directory = Path.Combine(root, "LgymApi.Application", "Coaching", "ManagedPlans");
        var source = Directory.GetFiles(directory, "*.cs", SearchOption.AllDirectories)
            .Select(File.ReadAllText)
            .ToArray();
        source.Should().NotContain(text => text.Contains("IPlanRepository", StringComparison.Ordinal));
        source.Should().NotContain(text => text.Contains("IUserRepository", StringComparison.Ordinal));
        source.Should().NotContain(text => text.Contains("TrainerRelationshipService", StringComparison.Ordinal));
        source.Should().NotContain(text => text.Contains("LgymApi.Infrastructure", StringComparison.Ordinal));
        source.Should().NotContain(text => text.Contains("SaveChangesAsync", StringComparison.Ordinal));
        source.Should().NotContain(text => text.Contains("BeginTransactionAsync", StringComparison.Ordinal));
    }

    [Test]
    public void ManagedPlanSlices_UseRegisteredInputMappingAndDoNotCreateOwnerInputsInline()
    {
        var root = ArchitectureTestHelpers.ResolveRepositoryRoot();
        var directory = Path.Combine(root, "LgymApi.Application", "Coaching", "ManagedPlans");
        var profile = File.ReadAllText(Path.Combine(directory, "ManagedPlanCollaborationMappingProfile.cs"));
        var useCases = Directory.GetFiles(directory, "*UseCase.cs", SearchOption.AllDirectories)
            .Where(path => !Path.GetFileName(path).StartsWith("I", StringComparison.Ordinal))
            .Select(File.ReadAllText)
            .ToArray();

        useCases.Should().OnlyContain(source => source.Contains("_mapper.CreateContext()", StringComparison.Ordinal));
        useCases.Should().NotContain(source => source.Contains("new Owner", StringComparison.Ordinal));
        profile.Split("configuration.CreateMap<", StringSplitOptions.None).Length.Should().Be(8);
    }

    [Test]
    public void TrainingPlanningManagedPlanOwner_DoesNotDependOnCoaching()
    {
        var root = ArchitectureTestHelpers.ResolveRepositoryRoot();
        var directory = Path.Combine(root, "LgymApi.Application", "TrainingPlanning");
        var source = Directory.GetFiles(directory, "*.cs", SearchOption.AllDirectories)
            .Select(File.ReadAllText);

        source.Should().NotContain(text => text.Contains("LgymApi.Application.Coaching", StringComparison.Ordinal));
    }

    [Test]
    public void TrainingPlanningManagedPlanOwner_UsesTrainingPlanningPlanErrors()
    {
        var root = ArchitectureTestHelpers.ResolveRepositoryRoot();
        var directory = Path.Combine(root, "LgymApi.Application", "TrainingPlanning", "ManagedPlans");
        var useCaseSources = Directory.GetFiles(directory, "*UseCase.cs", SearchOption.AllDirectories)
            .Where(path => !Path.GetFileName(path).StartsWith("I", StringComparison.Ordinal))
            .Select(File.ReadAllText);

        useCaseSources.Should().OnlyContain(source =>
            source.Contains("LgymApi.Application.TrainingPlanning.Errors", StringComparison.Ordinal));
    }

    private static bool IsAllowedInputType(Type type)
    {
        if (type == typeof(string))
        {
            return true;
        }

        if (!type.IsGenericType || type.GetGenericTypeDefinition() != typeof(LgymApi.Domain.ValueObjects.Id<>))
        {
            return false;
        }

        return type.GetGenericArguments().Single() is var argument
            && (argument == typeof(User) || argument == typeof(Plan));
    }
}
