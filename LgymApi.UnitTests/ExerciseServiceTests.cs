using FluentAssertions;
using LgymApi.Application.Features.Exercise;
using LgymApi.Application.Features.Exercise.Models;
using LgymApi.Application.Repositories;
using LgymApi.Application.TrainingPlanning.Contracts.PlanDay;
using LgymApi.Application.WorkoutProgress.Errors;
using LgymApi.Application.WorkoutProgress.Persistence;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.Security;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using LgymApi.Identity.Contracts.Accounts;
using LgymApi.TrainingPlanning.Contracts;
using NSubstitute;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class ExerciseServiceTests
{
    private IAccountAccessReader _accounts = null!;
    private IWorkoutExercisePersistence _exercises = null!;
    private IWorkoutExerciseScorePersistence _scores = null!;
    private IPlanDayReferenceReadService _planDays = null!;
    private IUnitOfWork _unitOfWork = null!;
    private ExerciseService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _accounts = Substitute.For<IAccountAccessReader>();
        _exercises = Substitute.For<IWorkoutExercisePersistence>();
        _scores = Substitute.For<IWorkoutExerciseScorePersistence>();
        _planDays = Substitute.For<IPlanDayReferenceReadService>();
        _unitOfWork = Substitute.For<IUnitOfWork>();
        _service = new ExerciseService(_accounts, _exercises, _scores, _planDays, _unitOfWork);
    }

    [Test]
    public async Task AddGlobalExercise_WithoutPermission_ReturnsForbiddenWithoutWriting()
    {
        var actor = Context(Id<AccountReference>.New());

        var result = await _service.AddExerciseAsync(actor, "Squat", BodyParts.Quads, null, null);

        result.Error.Should().BeOfType<ExerciseForbiddenError>();
        await _exercises.DidNotReceive().AddAsync(Arg.Any<WorkoutExerciseWriteModel>(), Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task AddGlobalExercise_AsManager_StagesGlobalExerciseAndCommits()
    {
        var manager = Context(Id<AccountReference>.New(), [AuthConstants.Permissions.ManageGlobalExercises]);

        var result = await _service.AddExerciseAsync(manager, "Squat", BodyParts.Quads, null, null);

        result.IsSuccess.Should().BeTrue();
        await _exercises.Received(1).AddAsync(
            Arg.Is<WorkoutExerciseWriteModel>(exercise => exercise.OwnerId == null),
            Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task AddUserExercise_WhenRouteDoesNotMatchActor_ReturnsForbiddenWithoutWriting()
    {
        var actor = Context(Id<AccountReference>.New());
        var victimId = Id<AccountReference>.New();

        var result = await _service.AddUserExerciseAsync(
            actor,
            new AddUserExerciseInput(victimId, "Squat", BodyParts.Quads, null, null));

        result.Error.Should().BeOfType<ExerciseForbiddenError>();
        await _exercises.DidNotReceive().AddAsync(Arg.Any<WorkoutExerciseWriteModel>(), Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task AddUserExercise_AsOwner_StagesMarkerOwnedExerciseAndCommits()
    {
        var accountId = Id<AccountReference>.New();
        var actor = Context(accountId);

        var result = await _service.AddUserExerciseAsync(
            actor,
            new AddUserExerciseInput(accountId, "Squat", BodyParts.Quads, null, null));

        result.IsSuccess.Should().BeTrue();
        await _exercises.Received(1).AddAsync(
            Arg.Is<WorkoutExerciseWriteModel>(exercise => exercise.OwnerId == accountId),
            Arg.Any<CancellationToken>());
        await _accounts.DidNotReceive().GetByIdAsync(Arg.Any<Id<AccountReference>>(), Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task AddUserExerciseWithFormula_AsManager_CanTargetExistingAccount()
    {
        var manager = Context(Id<AccountReference>.New(), [AuthConstants.Permissions.ManageGlobalExercises]);
        var targetId = Id<AccountReference>.New();
        _accounts.GetByIdAsync(targetId, Arg.Any<CancellationToken>()).Returns(Facts(targetId));

        var result = await _service.AddUserExerciseWithFormulaAsync(
            manager,
            new AddUserExerciseWithFormulaInput(targetId, "Squat", BodyParts.Quads, ExerciseEloFormula.VolumeWeighted, null, null));

        result.IsSuccess.Should().BeTrue();
        await _exercises.Received(1).AddAsync(
            Arg.Is<WorkoutExerciseWriteModel>(exercise => exercise.OwnerId == targetId && exercise.EloFormula == ExerciseEloFormula.VolumeWeighted),
            Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task DeleteExercise_WhenRouteDoesNotMatchActor_ReturnsForbiddenWithoutLookupOrSave()
    {
        var actor = Context(Id<AccountReference>.New());

        var result = await _service.DeleteExerciseAsync(actor, Id<AccountReference>.New(), Id<Exercise>.New());

        result.Error.Should().BeOfType<ExerciseForbiddenError>();
        await _exercises.DidNotReceive().FindOwnedByAccountAsync(Arg.Any<Id<Exercise>>(), Arg.Any<Id<AccountReference>>(), Arg.Any<CancellationToken>());
        await _exercises.DidNotReceive().FindUnrestrictedByIdAsync(Arg.Any<Id<Exercise>>(), Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task DeleteExercise_AsOwner_UsesOwnedLookupAndCommits()
    {
        var accountId = Id<AccountReference>.New();
        var exerciseId = Id<Exercise>.New();
        var actor = Context(accountId);
        _exercises.FindOwnedByAccountAsync(exerciseId, accountId, Arg.Any<CancellationToken>()).Returns(Model(exerciseId, accountId));

        var result = await _service.DeleteExerciseAsync(actor, accountId, exerciseId);

        result.IsSuccess.Should().BeTrue();
        await _exercises.Received(1).UpdateAsync(
            Arg.Is<WorkoutExerciseWriteModel>(exercise => exercise.Id == exerciseId && exercise.IsDeleted),
            Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task DeleteExercise_AsManager_UsesUnrestrictedLookupAndCommits()
    {
        var managerId = Id<AccountReference>.New();
        var ownerId = Id<AccountReference>.New();
        var exerciseId = Id<Exercise>.New();
        var manager = Context(managerId, [AuthConstants.Permissions.ManageGlobalExercises]);
        _exercises.FindUnrestrictedByIdAsync(exerciseId, Arg.Any<CancellationToken>()).Returns(Model(exerciseId, ownerId));

        var result = await _service.DeleteExerciseAsync(manager, managerId, exerciseId);

        result.IsSuccess.Should().BeTrue();
        await _exercises.Received(1).UpdateAsync(
            Arg.Is<WorkoutExerciseWriteModel>(exercise => exercise.Id == exerciseId && exercise.OwnerId == ownerId && exercise.IsDeleted),
            Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GetAllUserExercises_WhenRouteDoesNotMatchActor_ReturnsForbiddenWithoutReading()
    {
        var actor = Context(Id<AccountReference>.New());

        var result = await _service.GetAllUserExercisesAsync(actor, Id<AccountReference>.New(), []);

        result.Error.Should().BeOfType<ExerciseForbiddenError>();
        await _exercises.DidNotReceive().GetAccountExercisesAsync(Arg.Any<Id<AccountReference>>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GetExerciseByBodyPart_WhenRouteDoesNotMatchActor_ReturnsForbiddenWithoutReading()
    {
        var actor = Context(Id<AccountReference>.New());

        var result = await _service.GetExerciseByBodyPartAsync(actor, Id<AccountReference>.New(), BodyParts.Chest, []);

        result.Error.Should().BeOfType<ExerciseForbiddenError>();
        await _exercises.DidNotReceive().GetByBodyPartAsync(Arg.Any<Id<AccountReference>>(), Arg.Any<BodyParts>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GetExercise_WhenForeignCustomIsNotVisible_ReturnsNotFoundWithoutUnrestrictedLookup()
    {
        var actor = Context(Id<AccountReference>.New());
        var exerciseId = Id<Exercise>.New();
        _exercises.FindVisibleToAccountAsync(exerciseId, actor.Id, Arg.Any<CancellationToken>()).Returns((WorkoutExercisePersistenceModel?)null);

        var result = await _service.GetExerciseAsync(actor, exerciseId, []);

        result.Error.Should().BeOfType<ExerciseNotFoundError>();
        await _exercises.DidNotReceive().FindUnrestrictedByIdAsync(Arg.Any<Id<Exercise>>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GetExercise_AsManager_UsesUnrestrictedLookup()
    {
        var manager = Context(Id<AccountReference>.New(), [AuthConstants.Permissions.ManageGlobalExercises]);
        var ownerId = Id<AccountReference>.New();
        var exerciseId = Id<Exercise>.New();
        _exercises.FindUnrestrictedByIdAsync(exerciseId, Arg.Any<CancellationToken>()).Returns(Model(exerciseId, ownerId));

        var result = await _service.GetExerciseAsync(manager, exerciseId, []);

        result.IsSuccess.Should().BeTrue();
        result.Value.Exercise.UserId.Should().Be(ownerId);
        await _exercises.DidNotReceive().FindVisibleToAccountAsync(Arg.Any<Id<Exercise>>(), Arg.Any<Id<AccountReference>>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task UpdateExercise_WhenForeignCustomIsNotOwned_ReturnsNotFoundWithoutSave()
    {
        var actor = Context(Id<AccountReference>.New());
        var exerciseId = Id<Exercise>.New();
        _exercises.FindOwnedByAccountAsync(exerciseId, actor.Id, Arg.Any<CancellationToken>()).Returns((WorkoutExercisePersistenceModel?)null);

        var result = await _service.UpdateExerciseAsync(
            actor,
            new UpdateExerciseInput(exerciseId, "Changed", BodyParts.Back, null, null));

        result.Error.Should().BeOfType<ExerciseNotFoundError>();
        await _exercises.DidNotReceive().UpdateAsync(Arg.Any<WorkoutExerciseWriteModel>(), Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task AddGlobalTranslation_ShouldUsePermissionFactsAndCommit()
    {
        var accountId = Id<AccountReference>.New();
        var exerciseId = Id<Exercise>.New();
        var context = Context(accountId, [AuthConstants.Permissions.ManageGlobalExercises]);
        _exercises.FindUnrestrictedByIdAsync(exerciseId, Arg.Any<CancellationToken>()).Returns(Model(exerciseId, null));

        var result = await _service.AddGlobalTranslationAsync(context, new AddGlobalTranslationInput(accountId, exerciseId, "pl-PL", "Przysiad"));

        result.IsSuccess.Should().BeTrue();
        await _exercises.Received(1).UpsertTranslationAsync(exerciseId, "pl-pl", "Przysiad", Arg.Any<CancellationToken>());
        await _accounts.DidNotReceive().GetByIdAsync(Arg.Any<Id<AccountReference>>(), Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GetExerciseScoresFromTrainingByExercise_WhenPlanDayIsMissing_KeepsHistoryWithEmptyName()
    {
        var accountId = Id<AccountReference>.New();
        var exerciseId = Id<Exercise>.New();
        var trainingId = Id<Training>.New();
        var gymId = Id<Gym>.New();
        var planDayId = Id<PlanDayReference>.New();
        var gym = new WorkoutGymPersistenceModel(
            gymId,
            accountId,
            "History Gym",
            null,
            false,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);
        var training = new WorkoutTrainingPersistenceModel(
            trainingId,
            accountId,
            planDayId,
            gymId,
            DateTimeOffset.UtcNow,
            gym);
        var score = new WorkoutExerciseScorePersistenceModel(
            Id<ExerciseScore>.New(),
            exerciseId,
            accountId,
            8,
            1,
            100,
            WeightUnits.Kilograms,
            trainingId,
            0,
            DateTimeOffset.UtcNow,
            null,
            training);
        _exercises.FindVisibleToAccountAsync(exerciseId, accountId, Arg.Any<CancellationToken>())
            .Returns(Model(exerciseId, accountId));
        _scores.GetByAccountAndExerciseAsync(accountId, exerciseId, Arg.Any<CancellationToken>())
            .Returns([score]);
        _planDays.GetByIdsAsync(
                Arg.Is<IReadOnlyList<Id<PlanDayReference>>>(ids => ids.Count == 1 && ids[0] == planDayId),
                Arg.Any<CancellationToken>())
            .Returns([
                new PlanDayReferenceReadModel(
                    planDayId,
                    Id<PlanReference>.Empty,
                    string.Empty,
                    Exists: false,
                    IsDeleted: false)
            ]);

        var result = await _service.GetExerciseScoresFromTrainingByExerciseAsync(accountId, exerciseId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle();
        result.Value[0].TrainingName.Should().BeEmpty();
        result.Value[0].GymName.Should().Be("History Gym");
        result.Value[0].SeriesScores.Should().ContainSingle();
        result.Value[0].SeriesScores[0].Score!.Weight.Should().Be(100);
    }

    private static WorkoutExercisePersistenceModel Model(Id<Exercise> id, Id<AccountReference>? ownerId)
        => new(id, ownerId, "Exercise", BodyParts.Chest, ExerciseEloFormula.Standard, null, null, false, default, default);

    private static AccountAccessFacts Facts(Id<AccountReference> id, IReadOnlyList<string>? permissions = null)
        => new(id, false, false, [], permissions ?? []);

    private static AuthenticatedAccountContext Context(Id<AccountReference> id, IReadOnlyList<string>? permissions = null)
        => new(id, null, [], permissions ?? [], false, false);
}
