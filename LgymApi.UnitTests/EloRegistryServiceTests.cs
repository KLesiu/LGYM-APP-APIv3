using FluentAssertions;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.WorkoutProgress.Errors;
using LgymApi.Application.Identity.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Features.EloRegistry;
using LgymApi.Application.Features.User.Models;
using LgymApi.Application.Identity.Contracts.Registration;
using LgymApi.Application.Repositories;
using LgymApi.Application.WorkoutProgress.ProgressData;
using LgymApi.Application.WorkoutProgress.ProgressData.Models;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using NSubstitute;
using NUnit.Framework;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class EloRegistryServiceTests
{
    [Test]
    public async Task GetChartAsync_WithEmptyUserId_ReturnsInvalidEloRegistryError()
    {
        var progress = Substitute.For<IWorkoutProgressReadWriteService>();
        progress.GetEloChartAsync(Id<User>.Empty, Arg.Any<CancellationToken>()).Returns(Result<List<EloChartPoint>, AppError>.Failure(new InvalidEloRegistryError("invalid")));
        var service = new EloRegistryService(
            progress,
            Substitute.For<IUserRegistrationService>(),
            Substitute.For<IUnitOfWork>());

        var result = await service.GetChartAsync(Id<User>.Empty);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<InvalidEloRegistryError>();
    }

    [Test]
    public async Task GetUserEloAsync_WithEmptyUserId_ReturnsInvalidUserError()
    {
        var progress = Substitute.For<IWorkoutProgressReadWriteService>();
        progress.GetLatestEloAsync(Id<User>.Empty, Arg.Any<CancellationToken>()).Returns(Result<int, AppError>.Failure(new InvalidUserError("invalid")));
        var service = new EloRegistryService(
            progress,
            Substitute.For<IUserRegistrationService>(),
            Substitute.For<IUnitOfWork>());

        var result = await service.GetUserEloAsync(Id<User>.Empty);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<InvalidUserError>();
    }

    [Test]
    public async Task GetChartAsync_WhenEntriesExist_PreservesRepositoryOrder()
    {
        var userId = Id<User>.New();
        var entries = new List<EloRegistry>
        {
            new()
            {
                Id = Id<EloRegistry>.New(),
                UserId = userId,
                Elo = 1020,
                Date = new DateTimeOffset(2026, 2, 12, 8, 0, 0, TimeSpan.Zero)
            },
            new()
            {
                Id = Id<EloRegistry>.New(),
                UserId = userId,
                Elo = 1010,
                Date = new DateTimeOffset(2026, 1, 10, 8, 0, 0, TimeSpan.Zero)
            }
        };
        var progress = Substitute.For<IWorkoutProgressReadWriteService>();
        progress.GetEloChartAsync(userId, Arg.Any<CancellationToken>()).Returns(Result<List<EloChartPoint>, AppError>.Success(entries.Select(entry => new EloChartPoint(entry.Id, entry.Elo, entry.Date.UtcDateTime.ToString("MM/dd", System.Globalization.CultureInfo.InvariantCulture))).ToList()));
        var service = new EloRegistryService(
            progress,
            Substitute.For<IUserRegistrationService>(),
            Substitute.For<IUnitOfWork>());

        var result = await service.GetChartAsync(userId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Select(entry => entry.Id).Should().Equal(entries.Select(entry => entry.Id));
        result.Value.Select(entry => entry.Value).Should().Equal(1020, 1010);
        result.Value.Select(entry => entry.Date).Should().Equal("02/12", "01/10");
    }

    [Test]
    public async Task RegisterUserAsync_CreatesInitialEloAndCommitsOuterTransaction_AfterUserRegistration()
    {
        using var cancellationSource = new CancellationTokenSource();
        var cancellationToken = cancellationSource.Token;
        var userId = Id<User>.New();
        var input = new RegisterUserInput("new-user", "new@example.com", "password123", "password123", true, null);
        var userRegistrationService = Substitute.For<IUserRegistrationService>();
        var progress = Substitute.For<IWorkoutProgressReadWriteService>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var transaction = Substitute.For<IUnitOfWorkTransaction>();
        userRegistrationService.RegisterAsync(input, cancellationToken).Returns(Task.FromResult(Result<Id<User>, AppError>.Success(userId)));
        progress.InitializeEloAsync(userId, cancellationToken).Returns(Task.CompletedTask);
        unitOfWork.BeginTransactionAsync(cancellationToken).Returns(Task.FromResult<IUnitOfWorkTransaction>(transaction));
        unitOfWork.SaveChangesAsync(cancellationToken).Returns(Task.FromResult(1));
        transaction.CommitAsync(cancellationToken).Returns(Task.CompletedTask);

        var result = await new EloRegistryService(progress, userRegistrationService, unitOfWork).RegisterUserAsync(input, trainer: false, cancellationToken);

        result.IsSuccess.Should().BeTrue();
        Received.InOrder(() =>
        {
            userRegistrationService.RegisterAsync(input, cancellationToken);
            progress.InitializeEloAsync(userId, cancellationToken);
            unitOfWork.SaveChangesAsync(cancellationToken);
            transaction.CommitAsync(cancellationToken);
        });
    }

    [Test]
    public async Task RegisterUserAsync_DelegatesTrainerRegistrationToFocusedService()
    {
        var input = new RegisterUserInput("trainer", "trainer@example.com", "password123", "password123", false, null);
        var userId = Id<User>.New();
        var userRegistrationService = Substitute.For<IUserRegistrationService>();
        var progress = Substitute.For<IWorkoutProgressReadWriteService>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var transaction = Substitute.For<IUnitOfWorkTransaction>();
        userRegistrationService.RegisterTrainerAsync(input, Arg.Any<CancellationToken>()).Returns(Result<Id<User>, AppError>.Success(userId));
        progress.InitializeEloAsync(userId, Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        unitOfWork.BeginTransactionAsync(Arg.Any<CancellationToken>()).Returns(transaction);
        unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(1);
        transaction.CommitAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var result = await new EloRegistryService(progress, userRegistrationService, unitOfWork).RegisterUserAsync(input, trainer: true);

        result.IsSuccess.Should().BeTrue();
        await userRegistrationService.Received(1).RegisterTrainerAsync(input, Arg.Any<CancellationToken>());
        await userRegistrationService.DidNotReceive().RegisterAsync(Arg.Any<RegisterUserInput>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RegisterUserAsync_RollsBackWithoutCreatingEloOrCommitting_WhenRegistrationFails()
    {
        var input = new RegisterUserInput("user", "user@example.com", "password123", "password123", true, null);
        var userRegistrationService = Substitute.For<IUserRegistrationService>();
        var progress = Substitute.For<IWorkoutProgressReadWriteService>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var transaction = Substitute.For<IUnitOfWorkTransaction>();
        userRegistrationService.RegisterAsync(input, Arg.Any<CancellationToken>()).Returns(Result<Id<User>, AppError>.Failure(new InvalidUserError("invalid")));
        unitOfWork.BeginTransactionAsync(Arg.Any<CancellationToken>()).Returns(transaction);
        transaction.RollbackAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var result = await new EloRegistryService(progress, userRegistrationService, unitOfWork).RegisterUserAsync(input, trainer: false);

        result.IsFailure.Should().BeTrue();
        await transaction.Received(1).RollbackAsync(Arg.Any<CancellationToken>());
        await progress.DidNotReceive().InitializeEloAsync(Arg.Any<Id<User>>(), Arg.Any<CancellationToken>());
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await transaction.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
    }

}
