using FluentAssertions;
using LgymApi.Application.Features.Gym;
using LgymApi.Application.Repositories;
using LgymApi.Application.TrainingPlanning.Contracts.PlanDay;
using LgymApi.Application.WorkoutProgress.Errors;
using LgymApi.Application.WorkoutProgress.Persistence;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using LgymApi.Identity.Contracts.Accounts;
using NSubstitute;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class GymServiceTests
{
    private IWorkoutGymPersistence _gyms = null!;
    private IWorkoutTrainingPersistence _trainings = null!;
    private IPlanDayReferenceReadService _planDays = null!;
    private IUnitOfWork _unitOfWork = null!;
    private GymService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _gyms = Substitute.For<IWorkoutGymPersistence>();
        _trainings = Substitute.For<IWorkoutTrainingPersistence>();
        _planDays = Substitute.For<IPlanDayReferenceReadService>();
        _unitOfWork = Substitute.For<IUnitOfWork>();
        _service = new GymService(_gyms, _trainings, _planDays, _unitOfWork);
    }

    [Test]
    public async Task AddGym_ShouldRejectDifferentRouteAccount()
    {
        var result = await _service.AddGymAsync(Account(Id<AccountReference>.New()), Id<AccountReference>.New(), "Gym", null);
        result.Error.Should().BeOfType<GymForbiddenError>();
    }

    [Test]
    public async Task AddGym_ShouldStageMarkerOwnerAndCommit()
    {
        var accountId = Id<AccountReference>.New();
        var result = await _service.AddGymAsync(Account(accountId), accountId, "Gym", null);
        result.IsSuccess.Should().BeTrue();
        await _gyms.Received(1).AddAsync(Arg.Is<WorkoutGymWriteModel>(gym => gym.OwnerId == accountId), Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task DeleteGym_ShouldRejectForeignOwner()
    {
        var gymId = Id<Gym>.New();
        _gyms.FindByIdAsync(gymId, Arg.Any<CancellationToken>()).Returns(new WorkoutGymPersistenceModel(gymId, Id<AccountReference>.New(), "Gym", null, false, default, default));
        var result = await _service.DeleteGymAsync(Account(Id<AccountReference>.New()), gymId);
        result.Error.Should().BeOfType<GymForbiddenError>();
    }

    private static AuthenticatedAccountContext Account(Id<AccountReference> id) => new(id, null, [], [], false, false);
}
