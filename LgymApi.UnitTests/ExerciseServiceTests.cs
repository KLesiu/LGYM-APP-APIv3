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
    public async Task AddUserExercise_ShouldRequireExistingAccount()
    {
        var result = await _service.AddUserExerciseAsync(new AddUserExerciseInput(Id<AccountReference>.New(), "Squat", BodyParts.Quads, null, null));
        result.Error.Should().BeOfType<ExerciseNotFoundError>();
        await _exercises.DidNotReceive().AddAsync(Arg.Any<WorkoutExerciseWriteModel>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task AddUserExercise_ShouldStageMarkerOwnedExerciseAndCommit()
    {
        var accountId = Id<AccountReference>.New();
        _accounts.GetByIdAsync(accountId, Arg.Any<CancellationToken>()).Returns(Facts(accountId));

        var result = await _service.AddUserExerciseAsync(new AddUserExerciseInput(accountId, "Squat", BodyParts.Quads, null, null));

        result.IsSuccess.Should().BeTrue();
        await _exercises.Received(1).AddAsync(Arg.Is<WorkoutExerciseWriteModel>(exercise => exercise.OwnerId == accountId), Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task DeleteGlobalExercise_ShouldRequirePermission()
    {
        var accountId = Id<AccountReference>.New();
        var exerciseId = Id<Exercise>.New();
        _accounts.GetByIdAsync(accountId, Arg.Any<CancellationToken>()).Returns(Facts(accountId));
        _exercises.FindByIdAsync(exerciseId, Arg.Any<CancellationToken>()).Returns(Model(exerciseId, null));

        var result = await _service.DeleteExerciseAsync(accountId, exerciseId);

        result.Error.Should().BeOfType<InvalidExerciseError>();
    }

    [Test]
    public async Task AddGlobalTranslation_ShouldUsePermissionFactsAndCommit()
    {
        var accountId = Id<AccountReference>.New();
        var exerciseId = Id<Exercise>.New();
        var context = Context(accountId, [AuthConstants.Permissions.ManageGlobalExercises]);
        _accounts.GetByIdAsync(accountId, Arg.Any<CancellationToken>()).Returns(Facts(accountId, [AuthConstants.Permissions.ManageGlobalExercises]));
        _exercises.FindByIdAsync(exerciseId, Arg.Any<CancellationToken>()).Returns(Model(exerciseId, null));

        var result = await _service.AddGlobalTranslationAsync(context, new AddGlobalTranslationInput(accountId, exerciseId, "pl-PL", "Przysiad"));

        result.IsSuccess.Should().BeTrue();
        await _exercises.Received(1).UpsertTranslationAsync(exerciseId, "pl-pl", "Przysiad", Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    private static WorkoutExercisePersistenceModel Model(Id<Exercise> id, Id<AccountReference>? ownerId)
        => new(id, ownerId, "Exercise", BodyParts.Chest, ExerciseEloFormula.Standard, null, null, false, default, default);

    private static AccountAccessFacts Facts(Id<AccountReference> id, IReadOnlyList<string>? permissions = null)
        => new(id, false, false, [], permissions ?? []);

    private static AuthenticatedAccountContext Context(Id<AccountReference> id, IReadOnlyList<string>? permissions = null)
        => new(id, null, [], permissions ?? [], false, false);
}
