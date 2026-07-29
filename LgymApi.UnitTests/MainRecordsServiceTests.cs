using FluentAssertions;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.WorkoutProgress.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Features.AdminManagement.Models;
using LgymApi.Application.Features.MainRecords;
using LgymApi.Application.Features.MainRecords.Models;
using LgymApi.Application.Models;
using LgymApi.Application.Pagination;
using LgymApi.Application.Repositories;
using LgymApi.Application.Platform.ReferenceData.Units;
using LgymApi.Application.WorkoutProgress.ProgressData;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using NUnit.Framework;
using NSubstitute;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class MainRecordsServiceTests
{
    [Test]
    public async Task AddNewRecordAsync_WithEmptyUserId_ReturnsInvalidMainRecordsError()
    {
        var service = CreateService();
        var input = new AddMainRecordInput(Id<AccountReference>.Empty, Id<Exercise>.New(), 100, WeightUnits.Kilograms, DateTime.UtcNow);

        var result = await service.AddNewRecordAsync(input);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<InvalidMainRecordsError>();
    }

    [Test]
    public async Task GetMainRecordsHistoryAsync_WithEmptyUserId_ReturnsInvalidMainRecordsError()
    {
        var service = CreateService();

        var result = await service.GetMainRecordsHistoryAsync(Id<AccountReference>.Empty);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<InvalidMainRecordsError>();
    }

    [Test]
    public async Task GetLastMainRecordsAsync_WithEmptyUserId_ReturnsInvalidMainRecordsError()
    {
        var service = CreateService();

        var result = await service.GetLastMainRecordsAsync(Id<AccountReference>.Empty);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<InvalidMainRecordsError>();
    }

    [Test]
    public async Task DeleteMainRecordAsync_WithEmptyCurrentUserId_ReturnsInvalidMainRecordsError()
    {
        var service = CreateService();

        var result = await service.DeleteMainRecordAsync(Id<AccountReference>.Empty, Id<MainRecord>.New());

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<InvalidMainRecordsError>();
    }

    [Test]
    public async Task UpdateMainRecordAsync_WithEmptyRouteUserId_ReturnsInvalidMainRecordsError()
    {
        var service = CreateService();
        var input = new UpdateMainRecordInput(Id<AccountReference>.Empty, Id<AccountReference>.New(), Id<MainRecord>.New(), Id<Exercise>.New(), 100, WeightUnits.Kilograms, DateTime.UtcNow);

        var result = await service.UpdateMainRecordAsync(input);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<InvalidMainRecordsError>();
    }

    [Test]
    public async Task GetRecordOrPossibleRecordInExerciseAsync_WithEmptyUserId_ReturnsInvalidMainRecordsError()
    {
        var service = CreateService();

        var result = await service.GetRecordOrPossibleRecordInExerciseAsync(Id<AccountReference>.Empty, Id<Exercise>.New());

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<InvalidMainRecordsError>();
    }

    private static MainRecordsService CreateService()
    {
        var progress = NSubstitute.Substitute.For<IWorkoutProgressReadWriteService>();
        progress.AddMainRecordAsync(NSubstitute.Arg.Any<LgymApi.Application.WorkoutProgress.ProgressData.Models.MainRecordCreateWriteModel>(), NSubstitute.Arg.Any<CancellationToken>()).Returns(LgymApi.Application.BuildingBlocks.Results.Result<Unit, AppError>.Failure(new InvalidMainRecordsError("invalid")));
        progress.GetMainRecordHistoryAsync(Id<AccountReference>.Empty, NSubstitute.Arg.Any<CancellationToken>()).Returns(LgymApi.Application.BuildingBlocks.Results.Result<List<LgymApi.Application.WorkoutProgress.ProgressData.Models.MainRecordReadModel>, AppError>.Failure(new InvalidMainRecordsError("invalid")));
        progress.GetBestMainRecordsAsync(Id<AccountReference>.Empty, NSubstitute.Arg.Any<CancellationToken>()).Returns(LgymApi.Application.BuildingBlocks.Results.Result<List<LgymApi.Application.WorkoutProgress.ProgressData.Models.MainRecordBestReadModel>, AppError>.Failure(new InvalidMainRecordsError("invalid")));
        progress.DeleteMainRecordAsync(Id<AccountReference>.Empty, NSubstitute.Arg.Any<Id<MainRecord>>(), NSubstitute.Arg.Any<CancellationToken>()).Returns(LgymApi.Application.BuildingBlocks.Results.Result<Unit, AppError>.Failure(new InvalidMainRecordsError("invalid")));
        progress.UpdateMainRecordAsync(NSubstitute.Arg.Any<LgymApi.Application.WorkoutProgress.ProgressData.Models.MainRecordUpdateWriteModel>(), NSubstitute.Arg.Any<CancellationToken>()).Returns(LgymApi.Application.BuildingBlocks.Results.Result<Unit, AppError>.Failure(new InvalidMainRecordsError("invalid")));
        progress.GetRecordOrPossibleRecordAsync(Id<AccountReference>.Empty, NSubstitute.Arg.Any<Id<Exercise>>(), NSubstitute.Arg.Any<CancellationToken>()).Returns(LgymApi.Application.BuildingBlocks.Results.Result<LgymApi.Application.WorkoutProgress.ProgressData.Models.PossibleRecordReadModel, AppError>.Failure(new InvalidMainRecordsError("invalid")));
        return new MainRecordsService(progress);
    }
}
