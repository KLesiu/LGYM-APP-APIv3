using FluentAssertions;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Features.Measurements;
using LgymApi.Application.Features.Measurements.Models;
using LgymApi.Application.WorkoutProgress.Contracts.Measurements;
using LgymApi.Application.WorkoutProgress.Errors;
using LgymApi.Application.WorkoutProgress.ProgressData;
using LgymApi.Application.WorkoutProgress.ProgressData.Models;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.Security;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using LgymApi.Identity.Contracts.Accounts;
using NSubstitute;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class MeasurementsServiceTests
{
    private IWorkoutProgressReadWriteService _progress = null!;
    private IAccountAccessReader _accounts = null!;
    private IMeasurementsRelationshipAccessPort _relationships = null!;
    private MeasurementsService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _progress = Substitute.For<IWorkoutProgressReadWriteService>();
        _accounts = Substitute.For<IAccountAccessReader>();
        _relationships = Substitute.For<IMeasurementsRelationshipAccessPort>();
        _service = new MeasurementsService(_progress, _accounts, _relationships);
    }

    [Test]
    public async Task AddMeasurement_ShouldPassAuthenticatedMarker()
    {
        var accountId = Id<AccountReference>.New();
        _progress.AddMeasurementAsync(accountId, BodyParts.BodyWeight, MeasurementUnits.Kilograms, 80, Arg.Any<CancellationToken>()).Returns(Result<Unit, AppError>.Success(Unit.Value));
        var result = await _service.AddMeasurementAsync(Account(accountId), BodyParts.BodyWeight, MeasurementUnits.Kilograms, 80);
        result.IsSuccess.Should().BeTrue();
        await _progress.Received(1).AddMeasurementAsync(accountId, BodyParts.BodyWeight, MeasurementUnits.Kilograms, 80, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task OwnerRead_ShouldBypassTrainerRelationshipLookup()
    {
        var accountId = Id<AccountReference>.New();
        _progress.GetMeasurementsListForOwnerAsync(accountId, null, null, Arg.Any<CancellationToken>()).Returns(Result<List<MeasurementReadModel>, AppError>.Success([]));
        var result = await _service.GetMeasurementsListAsync(Account(accountId), accountId, null, null);
        result.IsSuccess.Should().BeTrue();
        await _relationships.DidNotReceiveWithAnyArgs().HasActiveRelationshipAsync(default, default);
    }

    [Test]
    public async Task TrainerRead_ShouldRequireRoleAndActiveRelationship()
    {
        var trainerId = Id<AccountReference>.New();
        var traineeId = Id<AccountReference>.New();
        _accounts.GetByIdAsync(trainerId, Arg.Any<CancellationToken>()).Returns(new AccountAccessFacts(trainerId, false, false, [AuthConstants.Roles.Trainer], []));
        _relationships.HasActiveRelationshipAsync(trainerId, traineeId, Arg.Any<CancellationToken>()).Returns(true);
        _progress.GetMeasurementsHistoryForOwnerAsync(traineeId, null, null, Arg.Any<CancellationToken>()).Returns(Result<List<MeasurementReadModel>, AppError>.Success([]));

        var result = await _service.GetMeasurementsHistoryAsync(Account(trainerId), traineeId, null, null);

        result.IsSuccess.Should().BeTrue();
        await _relationships.Received(1).HasActiveRelationshipAsync(trainerId, traineeId, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task NonTrainerRead_ShouldReturnForbidden()
    {
        var actorId = Id<AccountReference>.New();
        var ownerId = Id<AccountReference>.New();
        _accounts.GetByIdAsync(actorId, Arg.Any<CancellationToken>()).Returns(new AccountAccessFacts(actorId, false, false, [], []));
        var result = await _service.GetMeasurementsTrendsAsync(Account(actorId), ownerId);
        result.Error.Should().BeOfType<MeasurementForbiddenError>();
    }

    [Test]
    public async Task DetailRead_ShouldResolveOwnerBeforeAuthorization()
    {
        var accountId = Id<AccountReference>.New();
        var measurementId = Id<Measurement>.New();
        _progress.GetMeasurementOwnerAsync(measurementId, Arg.Any<CancellationToken>()).Returns(Result<Id<AccountReference>, AppError>.Success(accountId));
        _progress.GetMeasurementDetailForOwnerAsync(accountId, measurementId, Arg.Any<CancellationToken>()).Returns(Result<MeasurementReadModel, AppError>.Success(new(measurementId, accountId, BodyParts.BodyWeight, MeasurementUnits.Kilograms, 80, default, default)));
        var result = await _service.GetMeasurementDetailAsync(Account(accountId), measurementId);
        result.IsSuccess.Should().BeTrue();
    }

    private static AuthenticatedAccountContext Account(Id<AccountReference> id) => new(id, null, [], [], false, false);
}
