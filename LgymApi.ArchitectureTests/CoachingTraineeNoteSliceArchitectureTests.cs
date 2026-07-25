using System.Reflection;
using FluentAssertions;
using LgymApi.Application.Coaching;
using LgymApi.Application.Coaching.Persistence;
using LgymApi.Application.Coaching.TraineeNotes.Create;
using LgymApi.Application.Coaching.TraineeNotes.Delete;
using LgymApi.Application.Coaching.TraineeNotes.History;
using LgymApi.Application.Coaching.TraineeNotes.Models;
using LgymApi.Application.Coaching.TraineeNotes.TrainerList;
using LgymApi.Application.Coaching.TraineeNotes.Update;
using LgymApi.Application.Coaching.TraineeNotes.VisibleList;
using LgymApi.Application.Coaching.TraineeNotes.VisibleSingle;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Mapping.Core;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using Microsoft.Extensions.DependencyInjection;

namespace LgymApi.ArchitectureTests;

[TestFixture]
public sealed class CoachingTraineeNoteSliceArchitectureTests
{
    private static readonly (Type Contract, string ImplementationName)[] Slices =
    [
        (typeof(IListTrainerNotesUseCase), "LgymApi.Application.Coaching.TraineeNotes.TrainerList.ListTrainerNotesUseCase"),
        (typeof(ICreateTraineeNoteUseCase), "LgymApi.Application.Coaching.TraineeNotes.Create.CreateTraineeNoteUseCase"),
        (typeof(IUpdateTraineeNoteUseCase), "LgymApi.Application.Coaching.TraineeNotes.Update.UpdateTraineeNoteUseCase"),
        (typeof(IDeleteTraineeNoteUseCase), "LgymApi.Application.Coaching.TraineeNotes.Delete.DeleteTraineeNoteUseCase"),
        (typeof(IGetTraineeNoteHistoryUseCase), "LgymApi.Application.Coaching.TraineeNotes.History.GetTraineeNoteHistoryUseCase"),
        (typeof(IListVisibleTraineeNotesUseCase), "LgymApi.Application.Coaching.TraineeNotes.VisibleList.ListVisibleTraineeNotesUseCase"),
        (typeof(IGetVisibleTraineeNoteUseCase), "LgymApi.Application.Coaching.TraineeNotes.VisibleSingle.GetVisibleTraineeNoteUseCase")
    ];

    [Test]
    public void TrainerNoteSlices_ExposeOneMethodContractsWithInternalImplementations()
    {
        var assembly = typeof(IListTrainerNotesUseCase).Assembly;

        foreach (var slice in Slices)
        {
            slice.Contract.IsPublic.Should().BeTrue();
            slice.Contract.GetMethods(BindingFlags.Public | BindingFlags.Instance).Should().ContainSingle();
            assembly.GetType(slice.ImplementationName)!.IsNotPublic.Should().BeTrue();
        }
    }

    [Test]
    public void TrainerNoteSlices_AreRegisteredExactlyOnceByCoachingModule()
    {
        var services = new ServiceCollection();
        services.AddCoachingModule();

        foreach (var slice in Slices)
        {
            services.Count(descriptor => descriptor.ServiceType == slice.Contract).Should().Be(1);
        }
    }

    [Test]
    public void TrainerNotePublicInputs_AreImmutableAndUseOnlyTypedIdentifiersAndUpsertData()
    {
        var inputs = new[]
        {
            typeof(ListTrainerNotesQuery),
            typeof(CreateTraineeNoteCommand),
            typeof(UpdateTraineeNoteCommand),
            typeof(DeleteTraineeNoteCommand),
            typeof(GetTraineeNoteHistoryQuery),
            typeof(ListVisibleTraineeNotesQuery),
            typeof(GetVisibleTraineeNoteQuery),
            typeof(TraineeNoteUpsertData)
        };

        inputs.Should().OnlyContain(type => type.IsSealed);
        typeof(ListTrainerNotesQuery).GetProperties().Should().OnlyContain(property => IsTypedId(property.PropertyType));
        typeof(DeleteTraineeNoteCommand).GetProperties().Should().OnlyContain(property => IsTypedId(property.PropertyType));
        typeof(GetTraineeNoteHistoryQuery).GetProperties().Should().OnlyContain(property => IsTypedId(property.PropertyType));
        typeof(ListVisibleTraineeNotesQuery).GetProperties().Should().OnlyContain(property => IsTypedId(property.PropertyType));
        typeof(GetVisibleTraineeNoteQuery).GetProperties().Should().OnlyContain(property => IsTypedId(property.PropertyType));
        typeof(CreateTraineeNoteCommand).GetProperty(nameof(CreateTraineeNoteCommand.Data))!.PropertyType
            .Should().Be(typeof(TraineeNoteUpsertData));
        typeof(UpdateTraineeNoteCommand).GetProperty(nameof(UpdateTraineeNoteCommand.Data))!.PropertyType
            .Should().Be(typeof(TraineeNoteUpsertData));
        typeof(TraineeNoteUpsertData).GetProperties().Select(property => property.PropertyType)
            .Should().OnlyContain(type => type == typeof(string) || type == typeof(bool));
    }

    [Test]
    public void TraineeVisibleNoteSlices_ExposeOnlyReadDependenciesAndImmutableReadModels()
    {
        var assembly = typeof(IListVisibleTraineeNotesUseCase).Assembly;
        var implementationNames = new[]
        {
            "LgymApi.Application.Coaching.TraineeNotes.VisibleList.ListVisibleTraineeNotesUseCase",
            "LgymApi.Application.Coaching.TraineeNotes.VisibleSingle.GetVisibleTraineeNoteUseCase"
        };

        foreach (var implementationName in implementationNames)
        {
            assembly.GetType(implementationName)!.GetConstructors().Single().GetParameters()
                .Select(parameter => parameter.ParameterType)
                .Should().BeEquivalentTo(new[] { typeof(ICoachingTraineeNotePersistence), typeof(IMapper) });
        }

        typeof(IListVisibleTraineeNotesUseCase).GetMethods().Single().ReturnType.Should().Be(
            typeof(Task<Result<IReadOnlyList<TraineeNoteReadModel>, AppError>>));
        typeof(IGetVisibleTraineeNoteUseCase).GetMethods().Single().ReturnType.Should().Be(
            typeof(Task<Result<TraineeNoteReadModel, AppError>>));
    }

    [Test]
    public void TrainerNoteSlices_DoNotDependOnLegacyServicesRepositoriesOrUserValues()
    {
        var root = ArchitectureTestHelpers.ResolveRepositoryRoot();
        var directory = Path.Combine(root, "LgymApi.Application", "Coaching", "TraineeNotes");
        var source = Directory.GetFiles(directory, "*.cs", SearchOption.AllDirectories)
            .Select(File.ReadAllText)
            .ToArray();

        source.Should().NotContain(text => text.Contains("IUserRepository", StringComparison.Ordinal));
        source.Should().NotContain(text => text.Contains("ITraineeNoteService", StringComparison.Ordinal));
        source.Should().NotContain(text => text.Contains("ITraineeNoteRepository", StringComparison.Ordinal));
        source.Should().NotContain(text => text.Contains("LgymApi.Infrastructure", StringComparison.Ordinal));
    }

    [Test]
    public void TrainerNoteSlices_UseRegisteredMappingForEveryFactWriteAndReadTransformation()
    {
        var root = ArchitectureTestHelpers.ResolveRepositoryRoot();
        var directory = Path.Combine(root, "LgymApi.Application", "Coaching", "TraineeNotes");
        var profile = File.ReadAllText(Path.Combine(directory, "TraineeNoteMappingProfile.cs"));
        var useCases = Directory.GetFiles(directory, "*UseCase.cs", SearchOption.AllDirectories)
            .Where(path => !Path.GetFileName(path).StartsWith("I", StringComparison.Ordinal))
            .Select(File.ReadAllText)
            .ToArray();

        profile.Split("configuration.CreateMap<", StringSplitOptions.None).Length.Should().Be(7);
        useCases.Should().OnlyContain(source => source.Contains("_mapper.CreateContext()", StringComparison.Ordinal));
        useCases.Should().NotContain(source => source.Contains("new CoachingTraineeNoteWriteModel", StringComparison.Ordinal));
        useCases.Should().NotContain(source => source.Contains("new CoachingTraineeNoteHistoryWriteModel", StringComparison.Ordinal));
        useCases.Should().NotContain(source => source.Contains("new TraineeNoteReadModel", StringComparison.Ordinal));
        useCases.Should().NotContain(source => source.Contains("new TraineeNoteHistoryReadModel", StringComparison.Ordinal));
    }

    private static bool IsTypedId(Type type)
        => type.IsGenericType
            && type.GetGenericTypeDefinition() == typeof(Id<>)
            && (type.GetGenericArguments().Single() == typeof(User)
                || type.GetGenericArguments().Single() == typeof(TraineeNote));
}
