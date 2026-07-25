using FluentAssertions;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Features.AdminManagement.Models;
using LgymApi.Application.Models;
using LgymApi.Application.Notifications;
using LgymApi.Application.Notifications.Models;
using LgymApi.Application.Pagination;
using LgymApi.Application.Repositories;
using LgymApi.BackgroundWorker.Actions;
using LgymApi.Application.Reporting.Contracts.BackgroundCommands;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Notifications;
using LgymApi.Domain.ValueObjects;
using Microsoft.Extensions.Logging.Abstractions;
using System.Globalization;

namespace LgymApi.UnitTests.InAppNotifications;

[TestFixture]
public sealed class ReportRequestCreatedInAppNotificationCommandHandlerTests
{
    [Test]
    public async Task ExecuteAsync_Success_CreatesExpectedNotification()
    {
        var service = new FakeNotificationService(Result<InAppNotificationResult, AppError>.Success(CreateResult()));
        var trainerId = Id<User>.New();
        var traineeId = Id<User>.New();
        var requestId = Id<ReportRequest>.New();
        var previousUiCulture = CultureInfo.CurrentUICulture;
        var handler = new ReportRequestCreatedInAppNotificationCommandHandler(
            service,
            new FakeUserRepository(
                new User { Id = trainerId, Name = "Jan Kowalski" },
                new User { Id = traineeId, Name = "Adam", PreferredLanguage = "en-US" }),
            new Application.Options.AppDefaultsOptions { PreferredLanguage = "pl-PL" },
            NullLogger<ReportRequestCreatedInAppNotificationCommandHandler>.Instance);
        var command = new ReportRequestCreatedInAppNotificationCommand
        {
            RequestId = requestId,
            TraineeId = traineeId,
            TrainerId = trainerId,
            TemplateName = "Fit Raport"
        };

        try
        {
            await handler.ExecuteAsync(command);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previousUiCulture;
        }

        service.Calls.Should().Be(1);
        service.LastInput!.RecipientId.Should().Be(command.TraineeId);
        service.LastInput.SenderUserId.Should().Be(command.TrainerId);
        service.LastInput.IsSystemNotification.Should().BeFalse();
        service.LastInput.Message.Should().Be("Jan Kowalski sent you a report request: Fit Raport.");
        service.LastInput.RedirectUrl.Should().Be($"/trainer/report-requests/{requestId}");
        service.LastInput.Type.Should().Be(InAppNotificationTypes.ReportRequestReceived);
    }

    [Test]
    public async Task ExecuteAsync_UsesTraineePreferredLanguage_WhenBuildingMessage()
    {
        var service = new FakeNotificationService(Result<InAppNotificationResult, AppError>.Success(CreateResult()));
        var trainerId = Id<User>.New();
        var traineeId = Id<User>.New();
        var previousUiCulture = CultureInfo.CurrentUICulture;
        var handler = new ReportRequestCreatedInAppNotificationCommandHandler(
            service,
            new FakeUserRepository(
                new User { Id = trainerId, Name = "Trainer" },
                new User { Id = traineeId, Name = "Uczeń", PreferredLanguage = "pl-PL" }),
            new Application.Options.AppDefaultsOptions { PreferredLanguage = "en-US" },
            NullLogger<ReportRequestCreatedInAppNotificationCommandHandler>.Instance);

        try
        {
            await handler.ExecuteAsync(new ReportRequestCreatedInAppNotificationCommand
            {
                RequestId = Id<ReportRequest>.New(),
                TraineeId = traineeId,
                TrainerId = trainerId,
                TemplateName = string.Empty
            });
        }
        finally
        {
            CultureInfo.CurrentUICulture = previousUiCulture;
        }

        service.LastInput!.Message.Should().Be("Trainer wysłał Ci prośbę o raport: raport.");
    }

    [Test]
    public async Task ExecuteAsync_UsesLocalizedFallbacks_WhenTrainerAndTemplateNamesAreMissing()
    {
        var service = new FakeNotificationService(Result<InAppNotificationResult, AppError>.Success(CreateResult()));
        var trainerId = Id<User>.New();
        var traineeId = Id<User>.New();
        var previousUiCulture = CultureInfo.CurrentUICulture;
        var handler = new ReportRequestCreatedInAppNotificationCommandHandler(
            service,
            new FakeUserRepository(
                new User { Id = trainerId, Name = string.Empty },
                new User { Id = traineeId, Name = "Uczeń", PreferredLanguage = "en-US" }),
            new Application.Options.AppDefaultsOptions { PreferredLanguage = "pl-PL" },
            NullLogger<ReportRequestCreatedInAppNotificationCommandHandler>.Instance);

        try
        {
            await handler.ExecuteAsync(new ReportRequestCreatedInAppNotificationCommand
            {
                RequestId = Id<ReportRequest>.New(),
                TraineeId = traineeId,
                TrainerId = trainerId,
                TemplateName = string.Empty
            });
        }
        finally
        {
            CultureInfo.CurrentUICulture = previousUiCulture;
        }

        service.LastInput!.Message.Should().Be("Trainer sent you a report request: report.");
    }

    private static InAppNotificationResult CreateResult()
        => new(Id<InAppNotification>.New(), Id<User>.New(), "message", null, false, InAppNotificationTypes.ReportRequestReceived, false, null, DateTimeOffset.UtcNow);

    private sealed class FakeNotificationService : IInAppNotificationService
    {
        private readonly Result<InAppNotificationResult, AppError> _result;

        public FakeNotificationService(Result<InAppNotificationResult, AppError> result) => _result = result;

        public int Calls { get; private set; }
        public CreateInAppNotificationInput? LastInput { get; private set; }

        public Task<Result<InAppNotificationResult, AppError>> CreateAsync(CreateInAppNotificationInput input, CancellationToken cancellationToken = default)
        {
            Calls++;
            LastInput = input;
            return Task.FromResult(_result);
        }

        public Task<Result<PagedResult<InAppNotificationResult>, AppError>> GetForUserAsync(Id<User> userId, CursorPaginationQuery query, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<Result<Unit, AppError>> MarkAsReadAsync(Id<InAppNotification> notificationId, Id<User> requestingUserId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<Result<Unit, AppError>> MarkAllAsReadAsync(Id<User> userId, DateTimeOffset? before, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<Result<int, AppError>> GetUnreadCountAsync(Id<User> userId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }

    private sealed class FakeUserRepository : IUserRepository
    {
        private readonly Dictionary<string, User> _users;

        public FakeUserRepository(params User[] users)
            => _users = users.ToDictionary(user => user.Id.ToString(), user => user);

        public Task<User?> FindByIdAsync(Id<User> id, CancellationToken cancellationToken = default)
            => Task.FromResult(_users.TryGetValue(id.ToString(), out var user) ? user : null);

        public Task<User?> FindByIdIncludingDeletedAsync(Id<User> id, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<User?> FindByIdWithRolesAsync(Id<User> id, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<User?> FindByNameAsync(string name, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<User?> FindByEmailAsync(Email email, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<User?> FindByNameOrEmailAsync(string name, string email, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<List<UserRankingEntry>> GetRankingAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task AddAsync(User user, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task UpdateAsync(User user, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<Pagination<UserResult>> GetUsersPaginatedAsync(FilterInput filterInput, bool includeDeleted, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }
}
