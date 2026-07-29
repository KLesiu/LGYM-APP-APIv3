using FluentAssertions;
using LgymApi.Api.Features.Trainer.Controllers;
using LgymApi.Application.Coaching.Compatibility;
using LgymApi.Application.Coaching.Invitations.Accept;
using LgymApi.Application.Coaching.Invitations.Reject;
using LgymApi.Application.Coaching.ManagedPlans.GetActive;
using LgymApi.Application.Coaching.Relationships.DetachFromTrainer;
using LgymApi.Application.Coaching.Relationships.GetCurrentTrainer;
using LgymApi.Application.Coaching.TraineeNotes.Create;
using LgymApi.Application.Coaching.TraineeNotes.Delete;
using LgymApi.Application.Coaching.TraineeNotes.History;
using LgymApi.Application.Coaching.TraineeNotes.Models;
using LgymApi.Application.Coaching.TraineeNotes.TrainerList;
using LgymApi.Application.Coaching.TraineeNotes.Update;
using LgymApi.Application.Coaching.TraineeNotes.VisibleList;
using LgymApi.Application.Coaching.TraineeNotes.VisibleSingle;
using LgymApi.Application.Mapping.Core;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;

namespace LgymApi.ArchitectureTests;

[TestFixture]
public sealed class CoachingTraineeControllerAdapterArchitectureTests
{
    [Test]
    public void TraineeControllerAdapters_UseOnlyFocusedCoachingUseCasesAndMapper()
    {
        AssertConstructor(
            typeof(TraineeRelationshipController),
            typeof(ITraineeRelationshipApiPort),
            typeof(IMapper));
        AssertConstructor(
            typeof(TrainerTraineeNotesController),
            typeof(ITrainerTraineeNotesApiPort),
            typeof(IMapper));
        AssertConstructor(
            typeof(TraineeNotesController),
            typeof(ITraineeNotesApiPort),
            typeof(IMapper));
    }

    [Test]
    public void CoachingFacadeServicesDependencyBagsAndTestSubstitutes_AreAbsent()
    {
        var root = ArchitectureTestHelpers.ResolveRepositoryRoot();
        var source = Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(path => path.StartsWith(Path.Combine(root, "LgymApi.Application"), StringComparison.Ordinal)
                || path.StartsWith(Path.Combine(root, "LgymApi.UnitTests"), StringComparison.Ordinal))
            .Select(File.ReadAllText)
            .ToArray();

        source.Should().NotContain(text => text.Contains("ITrainerRelationshipService", StringComparison.Ordinal));
        source.Should().NotContain(text => text.Contains("TrainerRelationshipService", StringComparison.Ordinal));
        source.Should().NotContain(text => text.Contains("ITraineeNoteService", StringComparison.Ordinal));
        source.Should().NotContain(text => text.Contains("TraineeNoteService", StringComparison.Ordinal));
    }

    [Test]
    public void TraineeControllerInputs_UseOnlyTypedUserIdentifiers()
    {
        var inputs = new[]
        {
            typeof(AcceptInvitationCommand),
            typeof(RejectInvitationCommand),
            typeof(DetachFromTrainerCommand),
            typeof(GetCurrentTrainerQuery),
            typeof(GetActiveManagedPlanQuery),
            typeof(ListTrainerNotesQuery),
            typeof(CreateTraineeNoteCommand),
            typeof(UpdateTraineeNoteCommand),
            typeof(DeleteTraineeNoteCommand),
            typeof(GetTraineeNoteHistoryQuery),
            typeof(ListVisibleTraineeNotesQuery),
            typeof(GetVisibleTraineeNoteQuery)
        };

        inputs.Should().OnlyContain(type => type.GetProperties().All(property =>
            IsAllowedIdentifier(property.PropertyType)
            || property.PropertyType == typeof(TraineeNoteUpsertData)));
    }

    private static void AssertConstructor(Type controllerType, params Type[] dependencies)
    {
        var constructors = controllerType.GetConstructors();

        constructors.Should().ContainSingle();
        constructors[0].GetParameters().Select(parameter => parameter.ParameterType).Should().Equal(dependencies);
    }

    private static bool IsAllowedIdentifier(Type type)
        => type == typeof(Id<User>)
            || type == typeof(Id<AccountReference>)
            || type == typeof(Id<TraineeNote>)
            || type == typeof(Id<TrainerInvitation>);
}
