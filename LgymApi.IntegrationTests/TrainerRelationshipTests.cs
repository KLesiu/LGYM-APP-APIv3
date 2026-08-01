using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Globalization;
using System.Text.Json.Serialization;
using FluentAssertions;
using LgymApi.BackgroundWorker.Common.Notifications;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Data;
using LgymApi.Infrastructure.Data.SeedData;
using LgymApi.Resources;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LgymApi.IntegrationTests;

[TestFixture]
public sealed class TrainerRelationshipTests : IntegrationTestBase
{
    private static readonly double[] ExpectedMainRecordWeights = [140d, 150d];
    [SetUp]
    public void ResetEmailCapture()
    {
        Factory.EmailSender.Reset();
    }

    [Test]
    public async Task CreateInvitation_AsTrainer_CreatesPendingInvitation()
    {
        var trainer = await SeedTrainerAsync("trainer-invite", "trainer-invite@example.com");
        var trainee = await SeedUserAsync(name: "trainee-invite", email: "trainee-invite@example.com", password: "password123");
        SetAuthorizationHeader(trainer.Id);

        SetIdempotencyKey("test-invitation-create-pending");
        try
        {
            var response = await Client.PostAsJsonAsync("/api/trainer/invitations", new
            {
                traineeId = trainee.Id.ToString()
            });

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var body = await response.Content.ReadFromJsonAsync<TrainerInvitationResponse>();
            body.Should().NotBeNull();
            body!.Status.Should().Be("Pending");
            body.TraineeId.Should().Be(trainee.Id.ToString());
            body.Code.Should().NotBeNullOrWhiteSpace();
        }
        finally
        {
            ClearIdempotencyKey();
        }
    }

    [Test]
    public async Task CreateInvitation_CreatesPendingEmailNotificationLog()
    {
        var trainer = await SeedTrainerAsync("trainer-email-log", "trainer-email-log@example.com");
        var trainee = await SeedUserAsync(name: "trainee-email-log", email: "trainee-email-log@example.com", password: "password123");
        SetAuthorizationHeader(trainer.Id);

        SetIdempotencyKey("test-invitation-email-log");
        var response = await Client.PostAsJsonAsync("/api/trainer/invitations", new
        {
            traineeId = trainee.Id.ToString()
        });
        ClearIdempotencyKey();
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Process pending commands to trigger handler execution
        await ProcessPendingCommandsAsync();

        var invitation = await response.Content.ReadFromJsonAsync<TrainerInvitationResponse>();
        invitation.Should().NotBeNull();

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        if (!Id<CorrelationScope>.TryParse(invitation!.Id, out var correlationId))
        {
            throw new InvalidOperationException($"Failed to parse invitation ID: {invitation.Id}");
        }

        var log = await db.NotificationMessages.FirstOrDefaultAsync(x => x.CorrelationId == correlationId);
        log.Should().NotBeNull();
        log!.Status.Should().Be(EmailNotificationStatus.Pending);
        log.Recipient.Value.Should().Be("trainee-email-log@example.com");
    }

    [Test]
    public async Task InvitationEmailJob_ProcessesPendingNotification_AndMarksSent()
    {
        var trainer = await SeedTrainerAsync("trainer-email-job", "trainer-email-job@example.com", preferredLanguage: "en");
        var trainee = await SeedUserAsync(name: "trainee-email-job", email: "trainee-email-job@example.com", password: "password123");
        SetAuthorizationHeader(trainer.Id);

        SetIdempotencyKey("test-invitation-email-job");
        var response = await Client.PostAsJsonAsync("/api/trainer/invitations", new
        {
            traineeId = trainee.Id.ToString()
        });
        ClearIdempotencyKey();
        var invitation = await response.Content.ReadFromJsonAsync<TrainerInvitationResponse>();

        // Process pending commands to trigger handler execution
        await ProcessPendingCommandsAsync();
        invitation.Should().NotBeNull();

        Domain.ValueObjects.Id<NotificationMessage> notificationId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            if (!Id<CorrelationScope>.TryParse(invitation!.Id, out var correlationId))
            {
                throw new InvalidOperationException($"Failed to parse invitation ID: {invitation.Id}");
            }

            var log = await db.NotificationMessages.FirstAsync(x => x.CorrelationId == correlationId);
            notificationId = log.Id;
        }

        using (var scope = Factory.Services.CreateScope())
        {
            var handler = scope.ServiceProvider.GetRequiredService<IEmailJobHandler>();
            await handler.ProcessAsync(notificationId.ToString());
        }

        using (var verifyScope = Factory.Services.CreateScope())
        {
            var db = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var log = await db.NotificationMessages.FirstAsync(x => x.Id == notificationId);
            log.Status.Should().Be(EmailNotificationStatus.Sent);
            log.SentAt.Should().NotBeNull();
            log.Attempts.Should().BeGreaterThanOrEqualTo(1);
        }

        Factory.EmailSender.SentMessages.Should().ContainSingle();
        Factory.EmailSender.SentMessages[0].To.Should().Be("trainee-email-job@example.com");
    }

    [Test]
    public async Task InvitationEmailJob_UsesTrainerLanguageTemplate_WhenTrainerIsPolish()
    {
        var trainer = await SeedTrainerAsync("trainer-email-pl", "trainer-email-pl@example.com", preferredLanguage: "pl");
        var trainee = await SeedUserAsync(name: "trainee-email-pl", email: "trainee-email-pl@example.com", password: "password123");
        SetAuthorizationHeader(trainer.Id);

        SetIdempotencyKey("test-invitation-email-pl");
        var response = await Client.PostAsJsonAsync("/api/trainer/invitations", new
        {
            traineeId = trainee.Id.ToString()
        });
        ClearIdempotencyKey();
        var invitation = await response.Content.ReadFromJsonAsync<TrainerInvitationResponse>();

        // Process pending commands to trigger handler execution
        await ProcessPendingCommandsAsync();

        Domain.ValueObjects.Id<NotificationMessage> notificationId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            if (!Id<CorrelationScope>.TryParse(invitation!.Id, out var correlationId))
            {
                throw new InvalidOperationException($"Failed to parse invitation ID: {invitation.Id}");
            }

            var log = await db.NotificationMessages.FirstAsync(x => x.CorrelationId == correlationId);
            notificationId = log.Id;
        }

        using (var scope = Factory.Services.CreateScope())
        {
            var handler = scope.ServiceProvider.GetRequiredService<IEmailJobHandler>();
            await handler.ProcessAsync(notificationId.ToString());
        }

        Factory.EmailSender.SentMessages.Should().ContainSingle();
        Factory.EmailSender.SentMessages[0].Subject.Should().Contain("Zaproszenie");
        Factory.EmailSender.SentMessages[0].Body.Should().Contain("Akceptuj");
    }

    [Test]
    public async Task InvitationEmailJob_DoesNotResend_WhenAlreadySent()
    {
        var trainer = await SeedTrainerAsync("trainer-email-idempotent", "trainer-email-idempotent@example.com");
        var trainee = await SeedUserAsync(name: "trainee-email-idempotent", email: "trainee-email-idempotent@example.com", password: "password123");
        SetAuthorizationHeader(trainer.Id);

        SetIdempotencyKey("test-invitation-email-idempotent");
        var response = await Client.PostAsJsonAsync("/api/trainer/invitations", new
        {
            traineeId = trainee.Id.ToString()
        });
        ClearIdempotencyKey();
        var invitation = await response.Content.ReadFromJsonAsync<TrainerInvitationResponse>();

        // Process pending commands to trigger handler execution
        await ProcessPendingCommandsAsync();

        Domain.ValueObjects.Id<NotificationMessage> notificationId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            if (!Id<CorrelationScope>.TryParse(invitation!.Id, out var correlationId))
            {
                throw new InvalidOperationException($"Failed to parse invitation ID: {invitation.Id}");
            }
            var log = await db.NotificationMessages.FirstAsync(x => x.CorrelationId == correlationId);
            notificationId = log.Id;
        }

        Factory.EmailSender.FailuresRemaining = 1;

        using (var scope = Factory.Services.CreateScope())
        {
            var handler = scope.ServiceProvider.GetRequiredService<IEmailJobHandler>();
            var act = async () => await handler.ProcessAsync(notificationId.ToString());
            await act.Should().ThrowAsync<InvalidOperationException>();
        }

        using (var verifyScope = Factory.Services.CreateScope())
        {
            var db = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var log = await db.NotificationMessages.FirstAsync(x => x.Id == notificationId);
            log.Status.Should().Be(EmailNotificationStatus.Failed);
            log.Attempts.Should().Be(1);
            log.LastError.Should().NotBeNullOrWhiteSpace();
            log.SentAt.Should().BeNull();
        }

        Factory.EmailSender.SentMessages.Should().BeEmpty();
    }

    [Test]
    public async Task InvitationEmailJob_AfterRetry_SetsSentStatusAndKeepsAuditTrail()
    {
        var trainer = await SeedTrainerAsync("trainer-email-retry", "trainer-email-retry@example.com");
        var trainee = await SeedUserAsync(name: "trainee-email-retry", email: "trainee-email-retry@example.com", password: "password123");
        SetAuthorizationHeader(trainer.Id);

        SetIdempotencyKey("test-invitation-email-retry");
        var response = await Client.PostAsJsonAsync("/api/trainer/invitations", new
        {
            traineeId = trainee.Id.ToString()
        });
        ClearIdempotencyKey();
        var invitation = await response.Content.ReadFromJsonAsync<TrainerInvitationResponse>();

        // Process pending commands to trigger handler execution
        await ProcessPendingCommandsAsync();

        Domain.ValueObjects.Id<NotificationMessage> notificationId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            if (!Id<CorrelationScope>.TryParse(invitation!.Id, out var correlationId))
            {
                throw new InvalidOperationException($"Failed to parse invitation ID: {invitation.Id}");
            }
            var log = await db.NotificationMessages.FirstAsync(x => x.CorrelationId == correlationId);
            notificationId = log.Id;
        }

        Factory.EmailSender.FailuresRemaining = 1;

        using (var scope = Factory.Services.CreateScope())
        {
            var handler = scope.ServiceProvider.GetRequiredService<IEmailJobHandler>();
            var act = async () => await handler.ProcessAsync(notificationId.ToString());
            await act.Should().ThrowAsync<InvalidOperationException>();
            await handler.ProcessAsync(notificationId.ToString());
        }

        using (var verifyScope = Factory.Services.CreateScope())
        {
            var db = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var log = await db.NotificationMessages.FirstAsync(x => x.Id == notificationId);
            log.Status.Should().Be(EmailNotificationStatus.Sent);
            log.Attempts.Should().Be(2);
            log.SentAt.Should().NotBeNull();
            log.LastError.Should().BeNull();
            log.LastAttemptAt.Should().NotBeNull();
        }

        Factory.EmailSender.SentMessages.Should().ContainSingle();
    }

    [Test]
    public async Task CreateInvitation_RepeatedRequest_ReturnsExistingPendingInvitation()
    {
        var trainer = await SeedTrainerAsync("trainer-repeat", "trainer-repeat@example.com");
        var trainee = await SeedUserAsync(name: "trainee-repeat", email: "trainee-repeat@example.com", password: "password123");
        SetAuthorizationHeader(trainer.Id);

        SetIdempotencyKey("test-invitation-repeat-same-key");
        var first = await Client.PostAsJsonAsync("/api/trainer/invitations", new { traineeId = trainee.Id.ToString() });
        var firstBody = await first.Content.ReadFromJsonAsync<TrainerInvitationResponse>();

        // Process pending commands to trigger handler execution
        await ProcessPendingCommandsAsync();

        // Same key for replay test - should return cached response
        SetIdempotencyKey("test-invitation-repeat-same-key");
        var second = await Client.PostAsJsonAsync("/api/trainer/invitations", new { traineeId = trainee.Id.ToString() });
        ClearIdempotencyKey();
        var secondBody = await second.Content.ReadFromJsonAsync<TrainerInvitationResponse>();

        // Process pending commands to trigger handler execution
        await ProcessPendingCommandsAsync();

        second.StatusCode.Should().Be(HttpStatusCode.OK);
        firstBody.Should().NotBeNull();
        secondBody.Should().NotBeNull();
        secondBody!.Id.Should().Be(firstBody!.Id);
        secondBody.Code.Should().Be(firstBody.Code);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (!Id<CorrelationScope>.TryParse(firstBody.Id, out var correlationId))
        {
            throw new InvalidOperationException($"Failed to parse invitation ID: {firstBody.Id}");
        }
        var logsCount = await db.NotificationMessages
            .CountAsync(x => x.CorrelationId == correlationId);
        logsCount.Should().Be(1);
    }

    [Test]
    public async Task GetInvitationsPaginated_AsTrainer_ReturnsCreatedInvitations()
    {
        var trainer = await SeedTrainerAsync("trainer-list", "trainer-list@example.com");
        var traineeA = await SeedUserAsync(name: "trainee-list-a", email: "trainee-list-a@example.com", password: "password123");
        var traineeB = await SeedUserAsync(name: "trainee-list-b", email: "trainee-list-b@example.com", password: "password123");
        SetAuthorizationHeader(trainer.Id);

        SetIdempotencyKey("test-invitation-list-a");
        await Client.PostAsJsonAsync("/api/trainer/invitations", new { traineeId = traineeA.Id.ToString() });
        ClearIdempotencyKey();

        SetIdempotencyKey("test-invitation-list-b");
        await Client.PostAsJsonAsync("/api/trainer/invitations", new { traineeId = traineeB.Id.ToString() });
        ClearIdempotencyKey();

        var response = await Client.PostAsJsonAsync("/api/trainer/invitations/paginated", new { page = 1, pageSize = 20 });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PaginatedTrainerInvitationResponse>();
        body.Should().NotBeNull();
        body!.Items.Count.Should().Be(2);
        body.Items.Select(x => x.TraineeId).Should().BeEquivalentTo(new[] { traineeA.Id.ToString(), traineeB.Id.ToString() });
    }

    [Test]
    public async Task GetDashboardTrainees_AppliesOwnershipIsolation()
    {
        var trainerA = await SeedTrainerAsync("trainer-dashboard-a", "trainer-dashboard-a@example.com");
        var trainerB = await SeedTrainerAsync("trainer-dashboard-b", "trainer-dashboard-b@example.com");
        var traineeLinkedToA = await SeedUserAsync(name: "trainee-owned-link", email: "trainee-owned-link@example.com", password: "password123");
        var traineeInvitedByA = await SeedUserAsync(name: "trainee-owned-invite", email: "trainee-owned-invite@example.com", password: "password123");
        var traineeLinkedToB = await SeedUserAsync(name: "trainee-foreign", email: "trainee-foreign@example.com", password: "password123");

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.TrainerTraineeLinks.AddRange(
                new TrainerTraineeLink
                {
                    Id = Id<TrainerTraineeLink>.New(),
                    TrainerId = trainerA.Id,
                    TraineeId = traineeLinkedToA.Id
                },
                new TrainerTraineeLink
                {
                    Id = Id<TrainerTraineeLink>.New(),
                    TrainerId = trainerB.Id,
                    TraineeId = traineeLinkedToB.Id
                });

            db.TrainerInvitations.Add(new TrainerInvitation
            {
                Id = Id<TrainerInvitation>.New(),
                TrainerId = trainerA.Id,
                TraineeId = traineeInvitedByA.Id,
                Code = "OWNERSHIPA001",
                Status = TrainerInvitationStatus.Pending,
                ExpiresAt = DateTimeOffset.UtcNow.AddDays(3)
            });

            await db.SaveChangesAsync();
        }

        SetAuthorizationHeader(trainerA.Id);
        var response = await Client.GetAsync("/api/trainer/trainees?page=1&pageSize=20");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<TrainerDashboardTraineesResponse>();
        body.Should().NotBeNull();
        body!.Total.Should().Be(2);
        body.Items.Select(x => x.Id).Should().BeEquivalentTo(new[]
        {
            traineeLinkedToA.Id.ToString(),
            traineeInvitedByA.Id.ToString()
        });
        body.Items.Should().NotContain(x => x.Id == traineeLinkedToB.Id.ToString());
    }

    [Test]
    public async Task GetDashboardTrainees_AppliesSearchFilterSortAndStatusFlags()
    {
        var trainer = await SeedTrainerAsync("trainer-dashboard-filter", "trainer-dashboard-filter@example.com");
        var linked = await SeedUserAsync(name: "Alpha Linked", email: "alpha-linked@example.com", password: "password123");
        var pending = await SeedUserAsync(name: "Beta Pending", email: "beta-pending@example.com", password: "password123");
        var expired = await SeedUserAsync(name: "Gamma Expired", email: "gamma-expired@example.com", password: "password123");

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            db.TrainerTraineeLinks.Add(new TrainerTraineeLink
            {
                Id = Id<TrainerTraineeLink>.New(),
                TrainerId = trainer.Id,
                TraineeId = linked.Id
            });

            db.TrainerInvitations.AddRange(
                new TrainerInvitation
                {
                    Id = Id<TrainerInvitation>.New(),
                    TrainerId = trainer.Id,
                    TraineeId = pending.Id,
                    Code = "STATUSPENDING1",
                    Status = TrainerInvitationStatus.Pending,
                    ExpiresAt = DateTimeOffset.UtcNow.AddDays(2)
                },
                new TrainerInvitation
                {
                    Id = Id<TrainerInvitation>.New(),
                    TrainerId = trainer.Id,
                    TraineeId = expired.Id,
                    Code = "STATUSEXPIRED1",
                    Status = TrainerInvitationStatus.Pending,
                    ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-10)
                });

            await db.SaveChangesAsync();
        }

        SetAuthorizationHeader(trainer.Id);

        var filtered = await Client.GetAsync("/api/trainer/trainees?search=gamma&status=InvitationExpired&sortBy=name&sortDirection=desc&page=1&pageSize=10");
        filtered.StatusCode.Should().Be(HttpStatusCode.OK);
        var filteredBody = await filtered.Content.ReadFromJsonAsync<TrainerDashboardTraineesResponse>();

        filteredBody.Should().NotBeNull();
        filteredBody!.Total.Should().Be(1);
        filteredBody.Items.Should().ContainSingle();
        filteredBody.Items[0].Id.Should().Be(expired.Id.ToString());
        filteredBody.Items[0].Status.Should().Be("InvitationExpired");
        filteredBody.Items[0].HasExpiredInvitation.Should().BeTrue();
        filteredBody.Items[0].HasPendingInvitation.Should().BeFalse();
        filteredBody.Items[0].IsLinked.Should().BeFalse();

        var full = await Client.GetAsync("/api/trainer/trainees?sortBy=name&sortDirection=asc&page=1&pageSize=10");
        full.StatusCode.Should().Be(HttpStatusCode.OK);
        var fullBody = await full.Content.ReadFromJsonAsync<TrainerDashboardTraineesResponse>();
        fullBody.Should().NotBeNull();
        fullBody!.Items.Select(x => x.Name).Should().Equal("Alpha Linked", "Beta Pending", "Gamma Expired");

        var linkedItem = fullBody.Items.Single(x => x.Id == linked.Id.ToString());
        linkedItem.Status.Should().Be("Linked");
        linkedItem.IsLinked.Should().BeTrue();

        var pendingItem = fullBody.Items.Single(x => x.Id == pending.Id.ToString());
        pendingItem.Status.Should().Be("InvitationPending");
        pendingItem.HasPendingInvitation.Should().BeTrue();
    }

    [Test]
    public async Task GetDashboardTrainees_AppliesPagination_ForLargeResultSet()
    {
        var trainer = await SeedTrainerAsync("trainer-dashboard-pagination", "trainer-dashboard-pagination@example.com");

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            for (var i = 0; i < 35; i++)
            {
                var trainee = await SeedUserAsync(name: $"trainee-{i:00}", email: $"trainee-{i:00}@example.com", password: "password123");
                db.TrainerInvitations.Add(new TrainerInvitation
                {
                    Id = Id<TrainerInvitation>.New(),
                    TrainerId = trainer.Id,
                    TraineeId = trainee.Id,
                    Code = $"PAGE{i:000}CODE",
                    Status = TrainerInvitationStatus.Pending,
                    ExpiresAt = DateTimeOffset.UtcNow.AddDays(1)
                });
            }

            await db.SaveChangesAsync();
        }

        SetAuthorizationHeader(trainer.Id);
        var response = await Client.GetAsync("/api/trainer/trainees?sortBy=name&sortDirection=asc&page=2&pageSize=10");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<TrainerDashboardTraineesResponse>();
        body.Should().NotBeNull();
        body!.Total.Should().Be(35);
        body.Page.Should().Be(2);
        body.PageSize.Should().Be(10);
        body.Items.Count.Should().Be(10);
        body.Items.First().Name.Should().Be("trainee-10");
        body.Items.Last().Name.Should().Be("trainee-19");
    }

    [Test]
    public async Task GetDashboardTrainees_SortByStatus_OrdersByComputedRelationshipStatus()
    {
        var trainer = await SeedTrainerAsync("trainer-dashboard-sort-status", "trainer-dashboard-sort-status@example.com");
        var linked = await SeedUserAsync(name: "Zulu Linked", email: "zulu-linked@example.com", password: "password123");
        var pending = await SeedUserAsync(name: "Alpha Pending", email: "alpha-pending@example.com", password: "password123");
        var expired = await SeedUserAsync(name: "Mike Expired", email: "mike-expired@example.com", password: "password123");

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            db.TrainerTraineeLinks.Add(new TrainerTraineeLink
            {
                Id = Id<TrainerTraineeLink>.New(),
                TrainerId = trainer.Id,
                TraineeId = linked.Id
            });

            db.TrainerInvitations.AddRange(
                new TrainerInvitation
                {
                    Id = Id<TrainerInvitation>.New(),
                    TrainerId = trainer.Id,
                    TraineeId = pending.Id,
                    Code = "STATUSSORTPEND",
                    Status = TrainerInvitationStatus.Pending,
                    ExpiresAt = DateTimeOffset.UtcNow.AddDays(2)
                },
                new TrainerInvitation
                {
                    Id = Id<TrainerInvitation>.New(),
                    TrainerId = trainer.Id,
                    TraineeId = expired.Id,
                    Code = "STATUSSORTEXP",
                    Status = TrainerInvitationStatus.Pending,
                    ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-10)
                });

            await db.SaveChangesAsync();
        }

        SetAuthorizationHeader(trainer.Id);
        var response = await Client.GetAsync("/api/trainer/trainees?sortBy=status&sortDirection=asc&page=1&pageSize=10");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<TrainerDashboardTraineesResponse>();
        body.Should().NotBeNull();
        body!.Items.Select(x => x.Name).Should().Equal("Zulu Linked", "Alpha Pending", "Mike Expired");
    }

    [Test]
    public async Task GetDashboardTrainees_SearchByName_IsCaseInsensitive()
    {
        var trainer = await SeedTrainerAsync("trainer-dashboard-search-case", "trainer-dashboard-search-case@example.com");
        var matching = await SeedUserAsync(name: "Omega Case", email: "person-one@example.com", password: "password123");
        var nonMatching = await SeedUserAsync(name: "Delta Other", email: "person-two@example.com", password: "password123");

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            db.TrainerInvitations.AddRange(
                new TrainerInvitation
                {
                    Id = Id<TrainerInvitation>.New(),
                    TrainerId = trainer.Id,
                    TraineeId = matching.Id,
                    Code = "SEARCHCASE001",
                    Status = TrainerInvitationStatus.Pending,
                    ExpiresAt = DateTimeOffset.UtcNow.AddDays(2)
                },
                new TrainerInvitation
                {
                    Id = Id<TrainerInvitation>.New(),
                    TrainerId = trainer.Id,
                    TraineeId = nonMatching.Id,
                    Code = "SEARCHCASE002",
                    Status = TrainerInvitationStatus.Pending,
                    ExpiresAt = DateTimeOffset.UtcNow.AddDays(2)
                });

            await db.SaveChangesAsync();
        }

        SetAuthorizationHeader(trainer.Id);
        var response = await Client.GetAsync("/api/trainer/trainees?search=omega");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<TrainerDashboardTraineesResponse>();
        body.Should().NotBeNull();
        body!.Total.Should().Be(1);
        body.Items.Should().ContainSingle(x => x.Id == matching.Id.ToString());
    }

    [TestCase("sortBy=unknown", nameof(Messages.DashboardSortByInvalid), TestName = "GetDashboardTrainees_WithInvalidSortBy_ReturnsBadRequestWithResourceMessage")]
    [TestCase("status=NotAStatus", nameof(Messages.DashboardStatusInvalid), TestName = "GetDashboardTrainees_WithInvalidStatus_ReturnsBadRequestWithResourceMessage")]
    [TestCase("page=0", nameof(Messages.DashboardPageRange), TestName = "GetDashboardTrainees_WithInvalidPage_ReturnsBadRequestWithResourceMessage")]
    [TestCase("page=21474838", nameof(Messages.DashboardPageRange), TestName = "GetDashboardTrainees_WithTooLargePage_ReturnsBadRequestWithResourceMessage")]
    [TestCase("sortDirection=invalid", nameof(Messages.DashboardSortDirectionInvalid), TestName = "GetDashboardTrainees_WithInvalidSortDirection_ReturnsBadRequestWithResourceMessage")]
    public async Task GetDashboardTrainees_WithInvalidQueryParam_ReturnsBadRequestWithResourceMessage(string queryString, string expectedMessagePropertyName)
    {
        var uniqueKey = queryString.Replace("=", "-").Replace("&", "-");
        var trainer = await SeedTrainerAsync(
            $"trainer-dashboard-{uniqueKey}",
            $"trainer-dashboard-{Id<TrainerRelationshipTests>.New().ToString().Replace("-", string.Empty, StringComparison.Ordinal)}@example.com");
        SetAuthorizationHeader(trainer.Id);

        var response = await Client.GetAsync($"/api/trainer/trainees?{queryString}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var responseBody = await response.Content.ReadAsStringAsync();
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
            var expectedMessage = typeof(Messages).GetProperty(expectedMessagePropertyName)!.GetValue(null) as string;
            responseBody.Should().Contain(expectedMessage!);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    [TestCase(0)]
    [TestCase(-1)]
    [TestCase(101)]
    public async Task GetDashboardTrainees_WithInvalidPageSize_ReturnsBadRequestWithResourceMessage(int pageSize)
    {
        var trainer = await SeedTrainerAsync(
            $"trainer-dashboard-invalid-pagesize-{pageSize}",
            $"trainer-dashboard-invalid-pagesize-{Id<TrainerRelationshipTests>.New().ToString().Replace("-", string.Empty, StringComparison.Ordinal)}@example.com");
        SetAuthorizationHeader(trainer.Id);

        var response = await Client.GetAsync($"/api/trainer/trainees?pageSize={pageSize}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var responseBody = await response.Content.ReadAsStringAsync();
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
            responseBody.Should().Contain(Messages.DashboardPageSizeRange);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    [Test]
    public async Task GetTraineeTrainingReads_AsLinkedTrainer_ReturnsOnlyOwnedTraineeData()
    {
        var trainer = await SeedTrainerAsync("trainer-read-training", "trainer-read-training@example.com");
        var trainee = await SeedUserAsync(name: "trainee-read-training", email: "trainee-read-training@example.com", password: "password123");
        var foreignTrainee = await SeedUserAsync(name: "trainee-read-training-foreign", email: "trainee-read-training-foreign@example.com", password: "password123");

        var trackedDay = DateTime.UtcNow.Date.AddDays(-1).AddHours(10);
        var otherDay = trackedDay.AddDays(1);

        Id<Exercise> exerciseId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await LinkTrainerAndTraineeAsync(db, trainer.Id, trainee.Id);

            var traineePlan = new Plan { Id = Id<Plan>.New(), UserId = trainee.Id, Name = "Plan A" };
            var traineePlanDay = new PlanDay { Id = Id<PlanDay>.New(), PlanId = traineePlan.Id, Name = "Push Day" };
            var traineeGym = new Gym { Id = Id<Gym>.New(), UserId = trainee.Id, Name = "Gym A" };

            var foreignPlan = new Plan { Id = Id<Plan>.New(), UserId = foreignTrainee.Id, Name = "Plan B" };
            var foreignPlanDay = new PlanDay { Id = Id<PlanDay>.New(), PlanId = foreignPlan.Id, Name = "Pull Day" };
            var foreignGym = new Gym { Id = Id<Gym>.New(), UserId = foreignTrainee.Id, Name = "Gym B" };

            var exercise = new Exercise { Id = Id<Exercise>.New(), Name = "Bench Press", BodyPart = BodyParts.Chest };
            exerciseId = exercise.Id;

            db.Plans.AddRange(traineePlan, foreignPlan);
            db.PlanDays.AddRange(traineePlanDay, foreignPlanDay);
            db.Gyms.AddRange(traineeGym, foreignGym);
            db.Exercises.Add(exercise);

            var traineeTrainingA = new Training
            {
                Id = Id<Training>.New(),
                UserId = trainee.Id,
                TypePlanDayId = traineePlanDay.Id,
                GymId = traineeGym.Id,
                CreatedAt = trackedDay
            };
            var traineeTrainingB = new Training
            {
                Id = Id<Training>.New(),
                UserId = trainee.Id,
                TypePlanDayId = traineePlanDay.Id,
                GymId = traineeGym.Id,
                CreatedAt = otherDay
            };
            var foreignTraining = new Training
            {
                Id = Id<Training>.New(),
                UserId = foreignTrainee.Id,
                TypePlanDayId = foreignPlanDay.Id,
                GymId = foreignGym.Id,
                CreatedAt = otherDay.AddDays(1)
            };

            db.Trainings.AddRange(traineeTrainingA, traineeTrainingB, foreignTraining);

            var traineeScore = new ExerciseScore
            {
                Id = Id<ExerciseScore>.New(),
                ExerciseId = exercise.Id,
                UserId = trainee.Id,
                Reps = 8,
                Series = 1,
                Weight = 80,
                Unit = WeightUnits.Kilograms,
                TrainingId = traineeTrainingA.Id,
                CreatedAt = trackedDay
            };

            db.ExerciseScores.Add(traineeScore);
            db.TrainingExerciseScores.Add(new TrainingExerciseScore
            {
                Id = Id<TrainingExerciseScore>.New(),
                TrainingId = traineeTrainingA.Id,
                ExerciseScoreId = traineeScore.Id
            });

            await db.SaveChangesAsync();
        }

        SetAuthorizationHeader(trainer.Id);

        var datesResponse = await Client.GetAsync($"/api/trainer/trainees/{trainee.Id}/trainings/dates");
        datesResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var dates = await datesResponse.Content.ReadFromJsonAsync<List<DateTime>>();
        dates.Should().NotBeNull();
        dates!.Count.Should().Be(2);

        var byDateResponse = await Client.PostAsJsonAsync($"/api/trainer/trainees/{trainee.Id}/trainings/by-date", new
        {
            createdAt = trackedDay
        });
        byDateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var byDate = await byDateResponse.Content.ReadFromJsonAsync<List<TrainingByDateDetailsResponse>>();
        byDate.Should().NotBeNull();
        byDate!.Count.Should().Be(1);
        byDate[0].Gym.Should().Be("Gym A");
        byDate[0].Exercises.Should().ContainSingle();
        byDate[0].Exercises[0].ExerciseDetails.Name.Should().Be("Bench Press");
        byDate[0].Exercises[0].ExerciseDetails.Id.Should().Be(exerciseId.ToString());
    }

    [Test]
    public async Task GetTraineeProgressReads_AsLinkedTrainer_ReturnsExerciseAndEloCharts()
    {
        var trainer = await SeedTrainerAsync("trainer-read-progress", "trainer-read-progress@example.com");
        var trainee = await SeedUserAsync(name: "trainee-read-progress", email: "trainee-read-progress@example.com", password: "password123");

        Id<Exercise> exerciseId;
        Id<EloRegistry> firstEloRegistryId;
        Id<EloRegistry> secondEloRegistryId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await LinkTrainerAndTraineeAsync(db, trainer.Id, trainee.Id);

            var plan = new Plan { Id = Id<Plan>.New(), UserId = trainee.Id, Name = "Plan Progress" };
            var planDay = new PlanDay { Id = Id<PlanDay>.New(), PlanId = plan.Id, Name = "Progress Day" };
            var gym = new Gym { Id = Id<Gym>.New(), UserId = trainee.Id, Name = "Progress Gym" };
            var exercise = new Exercise { Id = Id<Exercise>.New(), Name = "Squat", BodyPart = BodyParts.Quads };
            exerciseId = exercise.Id;

            db.Plans.Add(plan);
            db.PlanDays.Add(planDay);
            db.Gyms.Add(gym);
            db.Exercises.Add(exercise);

            var dayA = DateTimeOffset.UtcNow.AddDays(-3);
            var dayB = DateTimeOffset.UtcNow.AddDays(-1);
            var trainingA = new Training { Id = Id<Training>.New(), UserId = trainee.Id, TypePlanDayId = planDay.Id, GymId = gym.Id, CreatedAt = dayA };
            var trainingB = new Training { Id = Id<Training>.New(), UserId = trainee.Id, TypePlanDayId = planDay.Id, GymId = gym.Id, CreatedAt = dayB };
            db.Trainings.AddRange(trainingA, trainingB);

            var scoreA = new ExerciseScore
            {
                Id = Id<ExerciseScore>.New(),
                ExerciseId = exercise.Id,
                UserId = trainee.Id,
                Reps = 5,
                Series = 1,
                Weight = 100,
                Unit = WeightUnits.Kilograms,
                TrainingId = trainingA.Id,
                CreatedAt = dayA
            };
            var scoreB = new ExerciseScore
            {
                Id = Id<ExerciseScore>.New(),
                ExerciseId = exercise.Id,
                UserId = trainee.Id,
                Reps = 5,
                Series = 1,
                Weight = 110,
                Unit = WeightUnits.Kilograms,
                TrainingId = trainingB.Id,
                CreatedAt = dayB
            };

            db.ExerciseScores.AddRange(scoreA, scoreB);
            firstEloRegistryId = Id<EloRegistry>.New();
            secondEloRegistryId = Id<EloRegistry>.New();
            db.EloRegistries.AddRange(
                 new EloRegistry { Id = firstEloRegistryId, UserId = trainee.Id, Date = dayA, Elo = 1010 },
                 new EloRegistry { Id = secondEloRegistryId, UserId = trainee.Id, Date = dayB, Elo = 1030 });

            await db.SaveChangesAsync();
        }

        SetAuthorizationHeader(trainer.Id);

        var progressResponse = await Client.PostAsJsonAsync($"/api/trainer/trainees/{trainee.Id}/exercise-scores/chart", new
        {
            exerciseId = exerciseId.ToString()
        });
        progressResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var progress = await progressResponse.Content.ReadFromJsonAsync<List<ExerciseScoresChartDataResponse>>();
        progress.Should().NotBeNull();
        progress!.Count.Should().Be(2);
        progress.Select(x => x.ExerciseId).Should().OnlyContain(x => x == exerciseId.ToString());
        progress.Select(x => x.ExerciseId).Should().OnlyContain(id => IsCanonicalId<Exercise>(id));

        var eloResponse = await Client.GetAsync($"/api/trainer/trainees/{trainee.Id}/elo/chart");
        eloResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var elo = await eloResponse.Content.ReadFromJsonAsync<List<EloRegistryChartResponse>>();
        elo.Should().NotBeNull();
        elo!.Count.Should().BeGreaterThanOrEqualTo(3);
        elo.Select(x => x.Id).Should().Contain(firstEloRegistryId.ToString(), secondEloRegistryId.ToString());
        elo.Select(x => x.Id).Should().OnlyContain(id => IsCanonicalId<EloRegistry>(id));
    }

    private static bool IsCanonicalId<TEntity>(string value)
    {
        return Id<TEntity>.TryParse(value, out var id) && id.ToString() == value;
    }

    [Test]
    public async Task GetTraineeMainRecordsHistory_AsLinkedTrainer_ReturnsHistory()
    {
        var trainer = await SeedTrainerAsync("trainer-read-records", "trainer-read-records@example.com");
        var trainee = await SeedUserAsync(name: "trainee-read-records", email: "trainee-read-records@example.com", password: "password123");

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await LinkTrainerAndTraineeAsync(db, trainer.Id, trainee.Id);

            var exercise = new Exercise { Id = Id<Exercise>.New(), Name = "Deadlift", BodyPart = BodyParts.Back };
            db.Exercises.Add(exercise);

            db.MainRecords.AddRange(
                new MainRecord
                {
                    Id = Id<MainRecord>.New(),
                    UserId = trainee.Id,
                    ExerciseId = exercise.Id,
                    Weight = 140,
                    Unit = WeightUnits.Kilograms,
                    Date = DateTimeOffset.UtcNow.AddDays(-20)
                },
                new MainRecord
                {
                    Id = Id<MainRecord>.New(),
                    UserId = trainee.Id,
                    ExerciseId = exercise.Id,
                    Weight = 150,
                    Unit = WeightUnits.Kilograms,
                    Date = DateTimeOffset.UtcNow.AddDays(-10)
                });

            await db.SaveChangesAsync();
        }

        SetAuthorizationHeader(trainer.Id);

        var response = await Client.GetAsync($"/api/trainer/trainees/{trainee.Id}/main-records/history");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var records = await response.Content.ReadFromJsonAsync<List<MainRecordResponse>>();
        records.Should().NotBeNull();
        records!.Count.Should().Be(2);
        records.Select(x => x.Weight).Should().Contain(ExpectedMainRecordWeights);
    }

    [Test]
    public async Task TraineeReadEndpoints_WhenTraineeBelongsToAnotherTrainer_ReturnsNotFound()
    {
        var trainerA = await SeedTrainerAsync("trainer-read-owner-a", "trainer-read-owner-a@example.com");
        var trainerB = await SeedTrainerAsync("trainer-read-owner-b", "trainer-read-owner-b@example.com");
        var trainee = await SeedUserAsync(name: "trainee-read-owner", email: "trainee-read-owner@example.com", password: "password123");

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await LinkTrainerAndTraineeAsync(db, trainerB.Id, trainee.Id);
            await db.SaveChangesAsync();
        }

        SetAuthorizationHeader(trainerA.Id);
        var responses = await Task.WhenAll(
            Client.GetAsync($"/api/trainer/trainees/{trainee.Id}/trainings/dates"),
            Client.PostAsJsonAsync($"/api/trainer/trainees/{trainee.Id}/trainings/by-date", new { createdAt = DateTime.UtcNow }),
            Client.PostAsJsonAsync($"/api/trainer/trainees/{trainee.Id}/exercise-scores/chart", new { exerciseId = Id<Exercise>.New().ToString() }),
            Client.GetAsync($"/api/trainer/trainees/{trainee.Id}/elo/chart"),
            Client.GetAsync($"/api/trainer/trainees/{trainee.Id}/main-records/history"));

        foreach (var response in responses)
        {
            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
            var body = await response.Content.ReadFromJsonAsync<MessageResponse>();
            body.Should().NotBeNull();
            body!.Message.Should().NotBeNullOrWhiteSpace();
        }
    }

    [Test]
    public async Task TraineeReadEndpoints_WithInvalidIds_ReturnsBadRequestWithResourceMessage()
    {
        var trainer = await SeedTrainerAsync("trainer-read-invalid", "trainer-read-invalid@example.com");
        var trainee = await SeedUserAsync(name: "trainee-read-invalid", email: "trainee-read-invalid@example.com", password: "password123");

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await LinkTrainerAndTraineeAsync(db, trainer.Id, trainee.Id);
            await db.SaveChangesAsync();
        }

        SetAuthorizationHeader(trainer.Id);

        var invalidTraineeResponse = await Client.GetAsync("/api/trainer/trainees/not-a-guid/trainings/dates");
        invalidTraineeResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var invalidTraineeBody = await invalidTraineeResponse.Content.ReadAsStringAsync();
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
            invalidTraineeBody.Should().Contain(Messages.UserIdRequired);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }

        var invalidExerciseResponse = await Client.PostAsJsonAsync($"/api/trainer/trainees/{trainee.Id}/exercise-scores/chart", new
        {
            exerciseId = "not-a-guid"
        });
        invalidExerciseResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var invalidExerciseBody = await invalidExerciseResponse.Content.ReadAsStringAsync();
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
            invalidExerciseBody.Should().Contain(Messages.ExerciseIdRequired);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }

        var emptyExerciseResponse = await Client.PostAsJsonAsync($"/api/trainer/trainees/{trainee.Id}/exercise-scores/chart", new
        {
            exerciseId = string.Empty
        });
        emptyExerciseResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var emptyExerciseBody = await emptyExerciseResponse.Content.ReadAsStringAsync();
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
            emptyExerciseBody.Should().Contain(Messages.ExerciseIdRequired);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    [Test]
    public async Task AcceptInvitation_AsTrainee_CreatesLink()
    {
        var trainer = await SeedTrainerAsync("trainer-accept", "trainer-accept@example.com");
        var trainee = await SeedUserAsync(name: "trainee-accept", email: "trainee-accept@example.com", password: "password123");

        SetAuthorizationHeader(trainer.Id);
        SetIdempotencyKey("test-invitation-accept-link");
        var createResponse = await Client.PostAsJsonAsync("/api/trainer/invitations", new
        {
            traineeId = trainee.Id.ToString()
        });
        ClearIdempotencyKey();
        var invitation = await createResponse.Content.ReadFromJsonAsync<TrainerInvitationResponse>();

        SetAuthorizationHeader(trainee.Id);
        var acceptResponse = await Client.PostAsync($"/api/trainee/invitations/{invitation!.Id}/accept", null);

        acceptResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var link = await db.TrainerTraineeLinks.FirstOrDefaultAsync(x => x.TrainerId == trainer.Id && x.TraineeId == trainee.Id);
        link.Should().NotBeNull();
    }

    [Test]
    public async Task RejectInvitation_AsTrainee_ChangesInvitationStatusToRejected()
    {
        var trainer = await SeedTrainerAsync("trainer-reject", "trainer-reject@example.com");
        var trainee = await SeedUserAsync(name: "trainee-reject", email: "trainee-reject@example.com", password: "password123");

        SetAuthorizationHeader(trainer.Id);
        SetIdempotencyKey("test-invitation-reject");
        var createResponse = await Client.PostAsJsonAsync("/api/trainer/invitations", new
        {
            traineeId = trainee.Id.ToString()
        });
        ClearIdempotencyKey();
        var invitation = await createResponse.Content.ReadFromJsonAsync<TrainerInvitationResponse>();

        SetAuthorizationHeader(trainee.Id);
        var rejectResponse = await Client.PostAsync($"/api/trainee/invitations/{invitation!.Id}/reject", null);
        rejectResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (!Id<TrainerInvitation>.TryParse(invitation.Id, out var invitationId))
        {
            throw new InvalidOperationException($"Failed to parse invitation ID: {invitation.Id}");
        }
        var invitationEntity = await db.TrainerInvitations.FirstAsync(i => i.Id == invitationId);
        invitationEntity.Status.ToString().Should().Be("Rejected");
        invitationEntity.RespondedAt.Should().NotBeNull();
    }

    [Test]
    public async Task AcceptInvitation_WhenExpired_ReturnsBadRequestAndMarksInvitationAsExpired()
    {
        var trainer = await SeedTrainerAsync("trainer-expired", "trainer-expired@example.com");
        var trainee = await SeedUserAsync(name: "trainee-expired", email: "trainee-expired@example.com", password: "password123");

        var invitationId = Id<TrainerInvitation>.New();
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.TrainerInvitations.Add(new TrainerInvitation
            {
                Id = invitationId,
                TrainerId = trainer.Id,
                TraineeId = trainee.Id,
                Code = "EXPIRED123456",
                ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-5)
            });
            await db.SaveChangesAsync();
        }

        SetAuthorizationHeader(trainee.Id);
        var response = await Client.PostAsync($"/api/trainee/invitations/{invitationId}/accept", null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<MessageResponse>();
        body.Should().NotBeNull();
        body!.Message.Should().Be("Invitation has expired.");

        using var verifyScope = Factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var invitation = await verifyDb.TrainerInvitations.FirstAsync(i => i.Id == invitationId);
        invitation.Status.ToString().Should().Be("Expired");
        invitation.RespondedAt.Should().NotBeNull();
    }

    [Test]
    public async Task CreateInvitation_AsRegularUser_ReturnsForbidden()
    {
        var regularUser = await SeedUserAsync(name: "regular-invite", email: "regular-invite@example.com", password: "password123");
        var trainee = await SeedUserAsync(name: "regular-target", email: "regular-target@example.com", password: "password123");
        SetAuthorizationHeader(regularUser.Id);

        SetIdempotencyKey("test-invitation-regular-forbidden");
        var response = await Client.PostAsJsonAsync("/api/trainer/invitations", new
        {
            traineeId = trainee.Id.ToString()
        });
        ClearIdempotencyKey();

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await response.Content.ReadFromJsonAsync<MessageResponse>();
        body.Should().NotBeNull();
        body!.Message.Should().NotBeNullOrWhiteSpace();
    }

    [Test]
    public async Task UnlinkByTrainer_RemovesExistingLink()
    {
        var trainer = await SeedTrainerAsync("trainer-unlink", "trainer-unlink@example.com");
        var trainee = await SeedUserAsync(name: "trainee-unlink", email: "trainee-unlink@example.com", password: "password123");

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.TrainerTraineeLinks.Add(new TrainerTraineeLink
            {
                Id = Id<TrainerTraineeLink>.New(),
                TrainerId = trainer.Id,
                TraineeId = trainee.Id
            });
            await db.SaveChangesAsync();
        }

        SetAuthorizationHeader(trainer.Id);
        var response = await Client.PostAsync($"/api/trainer/trainees/{trainee.Id}/unlink", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var verifyScope = Factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var link = await verifyDb.TrainerTraineeLinks.FirstOrDefaultAsync(x => x.TrainerId == trainer.Id && x.TraineeId == trainee.Id);
        link.Should().BeNull();
    }

    [Test]
    public async Task UnlinkByTrainer_AndDashboardFetch_ExcludesUnlinkedTrainee()
    {
        var trainer = await SeedTrainerAsync("trainer-unlink-dashboard", "trainer-unlink-dashboard@example.com");
        var trainee = await SeedUserAsync(name: "trainee-unlink-dashboard", email: "trainee-unlink-dashboard@example.com", password: "password123");

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.TrainerTraineeLinks.Add(new TrainerTraineeLink
            {
                Id = Id<TrainerTraineeLink>.New(),
                TrainerId = trainer.Id,
                TraineeId = trainee.Id
            });
            db.TrainerInvitations.Add(new TrainerInvitation
            {
                Id = Id<TrainerInvitation>.New(),
                TrainerId = trainer.Id,
                TraineeId = trainee.Id,
                Code = "UNLINKDASHREV",
                Status = TrainerInvitationStatus.Revoked,
                ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1),
                RespondedAt = DateTimeOffset.UtcNow.AddHours(-1)
            });
            await db.SaveChangesAsync();
        }

        SetAuthorizationHeader(trainer.Id);

        var beforeUnlinkResponse = await Client.GetAsync("/api/trainer/trainees?page=1&pageSize=20");
        beforeUnlinkResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var beforeUnlink = await beforeUnlinkResponse.Content.ReadFromJsonAsync<TrainerDashboardTraineesResponse>();
        beforeUnlink.Should().NotBeNull();
        beforeUnlink!.Total.Should().Be(1);
        beforeUnlink.Items.Should().ContainSingle(x => x.Id == trainee.Id.ToString());

        var unlinkResponse = await Client.PostAsync($"/api/trainer/trainees/{trainee.Id}/unlink", null);
        unlinkResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var afterUnlinkResponse = await Client.GetAsync("/api/trainer/trainees?page=1&pageSize=20");
        afterUnlinkResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var afterUnlink = await afterUnlinkResponse.Content.ReadFromJsonAsync<TrainerDashboardTraineesResponse>();
        afterUnlink.Should().NotBeNull();
        afterUnlink!.Total.Should().Be(0);
        afterUnlink.Items.Should().NotContain(x => x.Id == trainee.Id.ToString());
    }

    [Test]
    public async Task DetachByTrainee_RemovesExistingLink()
    {
        var trainer = await SeedTrainerAsync("trainer-detach", "trainer-detach@example.com");
        var trainee = await SeedUserAsync(name: "trainee-detach", email: "trainee-detach@example.com", password: "password123");

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.TrainerTraineeLinks.Add(new TrainerTraineeLink
            {
                Id = Id<TrainerTraineeLink>.New(),
                TrainerId = trainer.Id,
                TraineeId = trainee.Id
            });
            await db.SaveChangesAsync();
        }

        SetAuthorizationHeader(trainee.Id);
        var response = await Client.PostAsync("/api/trainee/trainer/detach", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var verifyScope = Factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var link = await verifyDb.TrainerTraineeLinks.FirstOrDefaultAsync(x => x.TrainerId == trainer.Id && x.TraineeId == trainee.Id);
        link.Should().BeNull();
    }

    [Test]
    public async Task TrainerPlanManagement_AssignAndUnassignPlan_UpdatesTraineeActivePlan()
    {
        var trainer = await SeedTrainerAsync("trainer-plan-assign", "trainer-plan-assign@example.com");
        var trainee = await SeedUserAsync(name: "trainee-plan-assign", email: "trainee-plan-assign@example.com", password: "password123");
        var templateExerciseName = "Trainer Template Exercise";

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await LinkTrainerAndTraineeAsync(db, trainer.Id, trainee.Id);
            await db.SaveChangesAsync();
        }

        SetAuthorizationHeader(trainer.Id);
        var createResponse = await Client.PostAsJsonAsync($"/api/trainer/trainees/{trainee.Id}/plans", new
        {
            name = "Trainer Owned Plan"
        });
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var createdPlan = await createResponse.Content.ReadFromJsonAsync<TrainerManagedPlanResponse>();
        createdPlan.Should().NotBeNull();
        createdPlan!.Name.Should().Be("Trainer Owned Plan");
        createdPlan.IsActive.Should().BeFalse();

        Id<Plan>.TryParse(createdPlan.Id, out var createdPlanId).Should().BeTrue();

        var trainerExerciseId = await CreateExerciseViaEndpointAsync(trainer.Id, templateExerciseName, BodyParts.Chest);
        await CreatePlanDayViaEndpointAsync(
            trainer.Id,
            createdPlanId,
            "Template Day",
            [new PlanDayExerciseInput { ExerciseId = trainerExerciseId.ToString(), Series = 4, Reps = "10" }]);

        var assignResponse = await Client.PostAsync($"/api/trainer/trainees/{trainee.Id}/plans/{createdPlan.Id}/assign", null);
        assignResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        SetAuthorizationHeader(trainer.Id);
        var traineePlansResponse = await Client.GetAsync($"/api/trainer/trainees/{trainee.Id}/plans");
        traineePlansResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var traineePlans = await traineePlansResponse.Content.ReadFromJsonAsync<List<TrainerManagedPlanResponse>>();
        traineePlans.Should().NotBeNull();
        traineePlans!.Should().ContainSingle();
        traineePlans.Single().Id.Should().NotBe(createdPlan.Id);
        traineePlans.Single().Name.Should().Be(createdPlan.Name);
        traineePlans.Single().IsActive.Should().BeTrue();

        SetAuthorizationHeader(trainee.Id);
        var activeResponse = await Client.GetAsync("/api/trainee/plan/active");
        activeResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var activePlan = await activeResponse.Content.ReadFromJsonAsync<TrainerManagedPlanResponse>();
        activePlan.Should().NotBeNull();
        activePlan!.Id.Should().NotBe(createdPlan.Id);
        activePlan.Name.Should().Be(createdPlan.Name);
        activePlan.IsActive.Should().BeTrue();

        using (var verifyScope = Factory.Services.CreateScope())
        {
            var db = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
            Id<Plan>.TryParse(activePlan.Id, out var traineePlanId).Should().BeTrue();

            var copiedPlan = await db.Plans.FirstAsync(p => p.Id == traineePlanId);
            copiedPlan.UserId.Should().Be(trainee.Id);

            var copiedPlanDay = await db.PlanDays
                .Include(pd => pd.Exercises)
                .ThenInclude(pde => pde.Exercise)
                .FirstAsync(pd => pd.PlanId == traineePlanId && !pd.IsDeleted);

            copiedPlanDay.Name.Should().Be("Template Day");
            copiedPlanDay.Exercises.Should().ContainSingle();
            copiedPlanDay.Exercises.Single().Series.Should().Be(4);
            copiedPlanDay.Exercises.Single().Reps.Should().Be("10");
            copiedPlanDay.Exercises.Single().Exercise.Should().NotBeNull();
            copiedPlanDay.Exercises.Single().Exercise!.Name.Should().Be(templateExerciseName);
            copiedPlanDay.Exercises.Single().Exercise!.UserId.Should().Be(trainee.Id);

            var trainerPlan = await db.Plans.FirstAsync(p => p.Id == createdPlanId);
            trainerPlan.UserId.Should().Be(trainer.Id);
        }

        SetAuthorizationHeader(trainer.Id);
        var unassignResponse = await Client.PostAsync($"/api/trainer/trainees/{trainee.Id}/plans/unassign", null);
        unassignResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        SetAuthorizationHeader(trainee.Id);
        var noActiveResponse = await Client.GetAsync("/api/trainee/plan/active");
        noActiveResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task TrainerPlanManagement_WhenTrainerDoesNotOwnTrainee_ReturnsNotFound()
    {
        var trainerA = await SeedTrainerAsync("trainer-plan-owner-a", "trainer-plan-owner-a@example.com");
        var trainerB = await SeedTrainerAsync("trainer-plan-owner-b", "trainer-plan-owner-b@example.com");
        var trainee = await SeedUserAsync(name: "trainee-plan-owner", email: "trainee-plan-owner@example.com", password: "password123");

        Id<Plan> planId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await LinkTrainerAndTraineeAsync(db, trainerB.Id, trainee.Id);

            var plan = new Plan
            {
                Id = Id<Plan>.New(),
                UserId = trainee.Id,
                Name = "Foreign Plan",
                IsActive = false,
                IsDeleted = false
            };

            db.Plans.Add(plan);
            await db.SaveChangesAsync();
            planId = plan.Id;
        }

        SetAuthorizationHeader(trainerA.Id);
        var assignResponse = await Client.PostAsync($"/api/trainer/trainees/{trainee.Id}/plans/{planId}/assign", null);
        assignResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task TrainerPlanManagement_AsNonTrainer_ReturnsForbiddenWithLegacyMessage()
    {
        var user = await SeedUserAsync("managed-plan-non-trainer", "managed-plan-non-trainer@example.com");
        var trainee = await SeedUserAsync("managed-plan-target", "managed-plan-target@example.com");
        SetAuthorizationHeader(user.Id);

        var response = await Client.PostAsJsonAsync($"/api/trainer/trainees/{trainee.Id}/plans", new
        {
            name = "Forbidden plan"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await response.Content.ReadFromJsonAsync<MessageResponse>();
        body.Should().NotBeNull();
        body!.Message.Should().Be(CompatibilityResourceMessage.InCulture("en", () => Messages.Unauthorized));
    }

    private static async Task LinkTrainerAndTraineeAsync(AppDbContext db, Id<User> trainerId, Id<User> traineeId)
    {
        var existing = await db.TrainerTraineeLinks.FirstOrDefaultAsync(x => x.TrainerId == trainerId && x.TraineeId == traineeId);
        if (existing != null)
        {
            return;
        }

        db.TrainerTraineeLinks.Add(new TrainerTraineeLink
        {
            Id = Id<TrainerTraineeLink>.New(),
            TrainerId = trainerId,
            TraineeId = traineeId
        });
    }

    private async Task<User> SeedTrainerAsync(string name, string email, string preferredLanguage = "en-US")
    {
        var trainer = await SeedUserAsync(name: name, email: email, password: "password123");

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var alreadyLinked = await db.UserRoles.AnyAsync(ur => ur.UserId == trainer.Id && ur.RoleId == RoleSeedDataConfiguration.TrainerRoleSeedId);
        if (!alreadyLinked)
        {
            db.UserRoles.Add(new UserRole
            {
                UserId = trainer.Id,
                RoleId = RoleSeedDataConfiguration.TrainerRoleSeedId
            });
        }

        var trainerToUpdate = await db.Users.FirstAsync(u => u.Id == trainer.Id);
        trainerToUpdate.PreferredLanguage = preferredLanguage;
        await db.SaveChangesAsync();

        return trainer;
    }

    private sealed class TrainerInvitationResponse
    {
        [JsonPropertyName("_id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("traineeId")]
        public string TraineeId { get; set; } = string.Empty;

        [JsonPropertyName("code")]
        public string Code { get; set; } = string.Empty;

        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;

    }

    private sealed class PaginatedTrainerInvitationResponse
    {
        [JsonPropertyName("items")]
        public List<TrainerInvitationResponse> Items { get; set; } = [];

        [JsonPropertyName("page")]
        public int Page { get; set; }

        [JsonPropertyName("pageSize")]
        public int PageSize { get; set; }

        [JsonPropertyName("totalCount")]
        public int TotalCount { get; set; }

        [JsonPropertyName("totalPages")]
        public int TotalPages { get; set; }

        [JsonPropertyName("hasNextPage")]
        public bool HasNextPage { get; set; }

        [JsonPropertyName("hasPreviousPage")]
        public bool HasPreviousPage { get; set; }
    }

    private sealed class TrainerDashboardTraineesResponse
    {
        [JsonPropertyName("page")]
        public int Page { get; set; }

        [JsonPropertyName("pageSize")]
        public int PageSize { get; set; }

        [JsonPropertyName("total")]
        public int Total { get; set; }

        [JsonPropertyName("items")]
        public List<TrainerDashboardTraineeResponse> Items { get; set; } = [];
    }

    private sealed class TrainerDashboardTraineeResponse
    {
        [JsonPropertyName("_id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;

        [JsonPropertyName("isLinked")]
        public bool IsLinked { get; set; }

        [JsonPropertyName("hasPendingInvitation")]
        public bool HasPendingInvitation { get; set; }

        [JsonPropertyName("hasExpiredInvitation")]
        public bool HasExpiredInvitation { get; set; }
    }

    private sealed class TrainingByDateDetailsResponse
    {
        [JsonPropertyName("gym")]
        public string? Gym { get; set; }

        [JsonPropertyName("exercises")]
        public List<TrainingExerciseGroupResponse> Exercises { get; set; } = [];
    }

    private sealed class TrainingExerciseGroupResponse
    {
        [JsonPropertyName("exerciseDetails")]
        public ExerciseDetailsResponse ExerciseDetails { get; set; } = new();
    }

    private sealed class ExerciseDetailsResponse
    {
        [JsonPropertyName("_id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;
    }

    private sealed class ExerciseScoresChartDataResponse
    {
        [JsonPropertyName("exerciseId")]
        public string ExerciseId { get; set; } = string.Empty;
    }

    private sealed class EloRegistryChartResponse
    {
        [JsonPropertyName("_id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("value")]
        public int Value { get; set; }
    }

    private sealed class MainRecordResponse
    {
        [JsonPropertyName("weight")]
        public double Weight { get; set; }
    }

    private sealed class TrainerManagedPlanResponse
    {
        [JsonPropertyName("_id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("isActive")]
        public bool IsActive { get; set; }
    }

    private sealed class MessageResponse
    {
        [JsonPropertyName("msg")]
        public string Message { get; set; } = string.Empty;
    }
}
