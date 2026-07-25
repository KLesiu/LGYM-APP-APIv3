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
using LgymApi.Resources;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using System.Globalization;

namespace LgymApi.UnitTests.InAppNotifications;

[TestFixture]
public sealed class ReportFeedbackAddedInAppNotificationCommandHandlerTests
{
    [Test]
    public async Task ExecuteAsync_WhenTrainerAndTemplateMissing_UsesFallbackNamesAndPreferredCulture()
    {
        var previousCulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("pl-PL");

        try
        {
            var service = new FakeNotificationService(Result<InAppNotificationResult, AppError>.Success(CreateResult()));
            var traineeId = Id<User>.New();
            var submissionId = Id<ReportSubmission>.New();
            var trainee = new User { Id = traineeId, Name = "Trainee", Email = "trainee@example.com", PreferredLanguage = "pl-PL" };
            var handler = new ReportFeedbackAddedInAppNotificationCommandHandler(
                service,
                new FakeUserRepository(trainee),
                new AppDefaultsOptions { PreferredLanguage = "en" },
                NullLogger<ReportFeedbackAddedInAppNotificationCommandHandler>.Instance);

            await handler.ExecuteAsync(new ReportFeedbackAddedInAppNotificationCommand
            {
                SubmissionId = submissionId,
                TraineeId = traineeId,
                TrainerId = Id<User>.New(),
                TemplateName = "   ",
                TriggeredAt = new DateTimeOffset(2026, 6, 21, 10, 30, 0, TimeSpan.Zero)
            });

            service.Calls.Should().Be(1);
            service.LastInput.Should().NotBeNull();
            service.LastInput!.DeliveryKey.Should().Be($"report-feedback:{submissionId}:2026-06-21T10:30:00.0000000+00:00");
            service.LastInput.Type.Should().Be(InAppNotificationTypes.ReportFeedbackReceived);
            service.LastInput.Message.Should().Contain(Messages.GenericTrainerDisplayName);
            service.LastInput.Message.Should().Contain(Messages.GenericReportDisplayName);
            service.LastInput.RedirectUrl.Should().Be($"/trainer/report-submissions/{submissionId}");
        }
        finally
        {
            CultureInfo.CurrentUICulture = previousCulture;
        }
    }

    [Test]
    public async Task ExecuteAsync_WhenCultureIsInvalid_FallsBackToAppDefaultCulture()
    {
        var service = new FakeNotificationService(Result<InAppNotificationResult, AppError>.Success(CreateResult()));
        var trainerId = Id<User>.New();
        var traineeId = Id<User>.New();
        var previousCulture = System.Globalization.CultureInfo.CurrentUICulture;
        var handler = new ReportFeedbackAddedInAppNotificationCommandHandler(
            service,
            new FakeUserRepository(
                new User { Id = trainerId, Name = "Trainer Name", Email = "trainer@example.com" },
                new User { Id = traineeId, Name = "Trainee", Email = "trainee@example.com", PreferredLanguage = "xx-invalid" }),
            new AppDefaultsOptions { PreferredLanguage = "en" },
            NullLogger<ReportFeedbackAddedInAppNotificationCommandHandler>.Instance);

        await handler.ExecuteAsync(new ReportFeedbackAddedInAppNotificationCommand
        {
            SubmissionId = Id<ReportSubmission>.New(),
            TraineeId = traineeId,
            TrainerId = trainerId,
            TemplateName = "Weekly check-in",
            TriggeredAt = DateTimeOffset.UtcNow
        });

        System.Globalization.CultureInfo.CurrentUICulture.Should().Be(previousCulture);
        service.LastInput!.Message.Should().Contain("Trainer Name");
        service.LastInput.Message.Should().Contain("Weekly check-in");
    }

    private static InAppNotificationResult CreateResult()
        => new(Id<InAppNotification>.New(), Id<User>.New(), "message", null, false, InAppNotificationTypes.ReportFeedbackReceived, false, null, DateTimeOffset.UtcNow);

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
        private readonly Dictionary<Id<User>, User> _users;

        public FakeUserRepository(params User[] users)
        {
            _users = users.ToDictionary(user => user.Id);
        }

        public Task<User?> FindByIdAsync(Id<User> id, CancellationToken cancellationToken = default)
            => Task.FromResult(_users.TryGetValue(id, out var user) ? user : null);

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
