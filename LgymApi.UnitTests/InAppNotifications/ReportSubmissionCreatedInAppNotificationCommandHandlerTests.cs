using FluentAssertions;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Features.AdminManagement.Models;
using LgymApi.Application.Models;
using LgymApi.Application.Notifications;
using LgymApi.Application.Notifications.Models;
using LgymApi.Application.Options;
using LgymApi.Application.Pagination;
using LgymApi.Application.Repositories;
using LgymApi.BackgroundWorker.Actions;
using LgymApi.Application.Reporting.Contracts.BackgroundCommands;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Notifications;
using LgymApi.Domain.ValueObjects;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using System.Globalization;

namespace LgymApi.UnitTests.InAppNotifications;

[TestFixture]
public sealed class ReportSubmissionCreatedInAppNotificationCommandHandlerTests
{
    [Test]
    public async Task ExecuteAsync_Success_CreatesExpectedNotification()
    {
        var service = new FakeNotificationService(Result<InAppNotificationResult, AppError>.Success(CreateResult()));
        var trainerId = Id<User>.New();
        var traineeId = Id<User>.New();
        var submissionId = Id<ReportSubmission>.New();
        var previousUiCulture = CultureInfo.CurrentUICulture;
        var handler = new ReportSubmissionCreatedInAppNotificationCommandHandler(
            service,
            new FakeUserRepository(
                new User { Id = trainerId, Name = "Coach", PreferredLanguage = "en-US" },
                new User { Id = traineeId, Name = "Adam" }),
            new AppDefaultsOptions { PreferredLanguage = "pl-PL" },
            NullLogger<ReportSubmissionCreatedInAppNotificationCommandHandler>.Instance);

        try
        {
            await handler.ExecuteAsync(new ReportSubmissionCreatedInAppNotificationCommand
            {
                SubmissionId = submissionId,
                TrainerId = trainerId,
                TraineeId = traineeId,
                TemplateName = "Weekly check-in"
            });
        }
        finally
        {
            CultureInfo.CurrentUICulture = previousUiCulture;
        }

        service.Calls.Should().Be(1);
        service.LastInput.Should().NotBeNull();
        service.LastInput!.RecipientId.Should().Be(trainerId);
        service.LastInput.SenderUserId.Should().Be(traineeId);
        service.LastInput.DeliveryKey.Should().Be($"report-submission:{submissionId}");
        service.LastInput.Message.Should().Be("Adam submitted a report: Weekly check-in.");
        service.LastInput.RedirectUrl.Should().Be($"/trainer/members/{traineeId}?tab=reports&submissionId={submissionId}");
        service.LastInput.Type.Should().Be(InAppNotificationTypes.ReportSubmissionReceived);
    }

    [Test]
    public async Task ExecuteAsync_WhenNamesMissing_UsesLocalizedFallbacks()
    {
        var service = new FakeNotificationService(Result<InAppNotificationResult, AppError>.Success(CreateResult()));
        var trainerId = Id<User>.New();
        var traineeId = Id<User>.New();
        var previousUiCulture = CultureInfo.CurrentUICulture;
        var handler = new ReportSubmissionCreatedInAppNotificationCommandHandler(
            service,
            new FakeUserRepository(
                new User { Id = trainerId, Name = string.Empty, PreferredLanguage = "pl-PL" },
                new User { Id = traineeId, Name = string.Empty }),
            new AppDefaultsOptions { PreferredLanguage = "en-US" },
            NullLogger<ReportSubmissionCreatedInAppNotificationCommandHandler>.Instance);

        try
        {
            await handler.ExecuteAsync(new ReportSubmissionCreatedInAppNotificationCommand
            {
                SubmissionId = Id<ReportSubmission>.New(),
                TrainerId = trainerId,
                TraineeId = traineeId,
                TemplateName = string.Empty
            });
        }
        finally
        {
            CultureInfo.CurrentUICulture = previousUiCulture;
        }

        service.LastInput!.Message.Should().Be("Podopieczny wysłał raport: raport.");
    }

    private static InAppNotificationResult CreateResult()
        => new(Id<InAppNotification>.New(), Id<User>.New(), "message", null, false, InAppNotificationTypes.ReportSubmissionReceived, false, null, DateTimeOffset.UtcNow);

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
