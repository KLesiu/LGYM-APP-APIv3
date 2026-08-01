using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Data;
using LgymApi.Infrastructure.Data.SeedData;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace LgymApi.IntegrationTests.Pagination;

/// <summary>
/// Integration tests for trainer dashboard Gridify pagination.
/// Validates the contract after the Gridify-backed pagination pipeline is wired into the trainer dashboard flow.
/// </summary>
[TestFixture]
public sealed class TrainerDashboardGridifyPaginationTests : IntegrationTestBase
{
    [Test]
    public async Task TrainerDashboardGridifyPagination_DefaultSortPagesTheEnrichedRowSet()
    {
        // Arrange
        var trainer = await SeedTrainerAsync("trainer-grid-baseline", "trainer-grid-baseline@example.com");
        var linkedAt = new DateTimeOffset(2026, 7, 22, 8, 0, 0, TimeSpan.Zero);
        await SeedDashboardTraineeAsync(trainer.Id, "Alpha baseline", "alpha-baseline@example.com", linked: true, linkedAt: linkedAt);
        await SeedDashboardTraineeAsync(trainer.Id, "Bravo baseline", "bravo-baseline@example.com", invited: true, invitationStatus: TrainerInvitationStatus.Pending, invitationExpiresAt: linkedAt.AddDays(2));
        var charlie = await SeedDashboardTraineeAsync(trainer.Id, "Charlie baseline", "charlie-baseline@example.com", linked: true, linkedAt: linkedAt.AddDays(1));
        SetAuthorizationHeader(trainer.Id);

        // Act
        var response = await Client.GetAsync("/api/trainer/trainees?page=2&pageSize=2");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await ParseDashboardResponseAsync(response);
        body.GetProperty("page").GetInt32().Should().Be(2);
        body.GetProperty("pageSize").GetInt32().Should().Be(2);
        body.GetProperty("total").GetInt32().Should().Be(3);
        body.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("_id").GetString()).Should().Equal(charlie.Id.ToString());
    }

    [Test]
    public async Task TrainerDashboardGridifyPagination_ReturnsCorrectPageAndTotal()
    {
        // Arrange
        var trainer = await SeedTrainerAsync("trainer-grid-page", "trainer-grid-page@example.com");
        var alpha = await SeedDashboardTraineeAsync(trainer.Id, "alpha-page", "alpha-page@example.com", linked: true, linkedAt: DateTimeOffset.UtcNow.AddDays(-3));
        var beta = await SeedDashboardTraineeAsync(trainer.Id, "beta-page", "beta-page@example.com", invited: true, invitationStatus: TrainerInvitationStatus.Pending, invitationExpiresAt: DateTimeOffset.UtcNow.AddDays(2));
        var gamma = await SeedDashboardTraineeAsync(trainer.Id, "gamma-page", "gamma-page@example.com", linked: true, linkedAt: DateTimeOffset.UtcNow.AddDays(-1));
        SetAuthorizationHeader(trainer.Id);

        // Act
        var response = await Client.GetAsync("/api/trainer/trainees?page=1&pageSize=2");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await ParseDashboardResponseAsync(response);
        body.GetProperty("page").GetInt32().Should().Be(1);
        body.GetProperty("pageSize").GetInt32().Should().Be(2);
        body.GetProperty("total").GetInt32().Should().Be(3);

        var items = body.GetProperty("items").EnumerateArray().ToArray();
        items.Should().HaveCount(2);
        items.Select(x => x.GetProperty("name").GetString()).Should().Equal("alpha-page", "beta-page");
    }

    [Test]
    public async Task TrainerDashboardGridifyPagination_AppliesMultiSortDeterministically()
    {
        // Arrange
        var trainer = await SeedTrainerAsync("trainer-grid-sort", "trainer-grid-sort@example.com");
        var first = await SeedDashboardTraineeAsync(trainer.Id, "Alpha Name", "alpha-name@example.com", linked: true, linkedAt: DateTimeOffset.UtcNow.AddDays(-5));
        var second = await SeedDashboardTraineeAsync(trainer.Id, "Beta Name", "beta-name@example.com", linked: true, linkedAt: DateTimeOffset.UtcNow.AddDays(-1));
        var third = await SeedDashboardTraineeAsync(trainer.Id, "Zeta Name", "zeta-name@example.com", linked: true, linkedAt: DateTimeOffset.UtcNow.AddDays(-3));
        SetAuthorizationHeader(trainer.Id);

        // Act
        var response = await Client.GetAsync("/api/trainer/trainees?sortBy=name&sortDirection=asc&page=1&pageSize=50");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await ParseDashboardResponseAsync(response);
        var items = body.GetProperty("items").EnumerateArray().ToArray();
        items.Select(x => x.GetProperty("name").GetString()).Should().Equal("Alpha Name", "Beta Name", "Zeta Name");
    }

    [Test]
    public async Task TrainerDashboardGridifyPagination_AppliesSearchFilter()
    {
        // Arrange
        var trainer = await SeedTrainerAsync("trainer-grid-filter", "trainer-grid-filter@example.com");
        var matching = await SeedDashboardTraineeAsync(trainer.Id, "Search Match", "search-match@example.com", invited: true, invitationStatus: TrainerInvitationStatus.Pending, invitationExpiresAt: DateTimeOffset.UtcNow.AddDays(1));
        await SeedDashboardTraineeAsync(trainer.Id, "Other Person", "other-person@example.com", linked: true, linkedAt: DateTimeOffset.UtcNow.AddDays(-1));
        SetAuthorizationHeader(trainer.Id);

        // Act
        var response = await Client.GetAsync("/api/trainer/trainees?search=match&page=1&pageSize=10");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await ParseDashboardResponseAsync(response);
        body.GetProperty("total").GetInt32().Should().Be(1);
        body.GetProperty("items").EnumerateArray().Should().ContainSingle(x => x.GetProperty("_id").GetString() == matching.Id.ToString());
    }

    [Test]
    public async Task TrainerDashboardGridifyPagination_DoesNotListRevokedInvitationRowsByDefault()
    {
        // Arrange
        var trainer = await SeedTrainerAsync("trainer-grid-revoked-default", "trainer-grid-revoked-default@example.com");
        var linked = await SeedDashboardTraineeAsync(trainer.Id, "linked-visible-default", "linked-visible-default@example.com", linked: true, linkedAt: DateTimeOffset.UtcNow.AddDays(-2));
        var pending = await SeedDashboardTraineeAsync(trainer.Id, "pending-visible-default", "pending-visible-default@example.com", invited: true, invitationStatus: TrainerInvitationStatus.Pending, invitationExpiresAt: DateTimeOffset.UtcNow.AddDays(2));
        var revoked = await SeedDashboardTraineeAsync(trainer.Id, "revoked-hidden-default", "revoked-hidden-default@example.com", invited: true, invitationStatus: TrainerInvitationStatus.Revoked, invitationExpiresAt: DateTimeOffset.UtcNow.AddDays(-1));
        SetAuthorizationHeader(trainer.Id);

        // Act
        var response = await Client.GetAsync("/api/trainer/trainees?page=1&pageSize=10");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await ParseDashboardResponseAsync(response);
        body.GetProperty("total").GetInt32().Should().Be(2);

        var items = body.GetProperty("items").EnumerateArray().ToArray();
        items.Select(x => x.GetProperty("_id").GetString()).Should().Contain(new[] { linked.Id.ToString(), pending.Id.ToString() });
        items.Select(x => x.GetProperty("_id").GetString()).Should().NotContain(revoked.Id.ToString());
    }

    [Test]
    public async Task TrainerDashboardGridifyPagination_DoesNotCountRevokedInvitationRowsForPaging()
    {
        // Arrange
        var trainer = await SeedTrainerAsync("trainer-grid-revoked-page", "trainer-grid-revoked-page@example.com");
        await SeedDashboardTraineeAsync(trainer.Id, "linked-visible-page", "linked-visible-page@example.com", linked: true, linkedAt: DateTimeOffset.UtcNow.AddDays(-2));
        await SeedDashboardTraineeAsync(trainer.Id, "pending-visible-page", "pending-visible-page@example.com", invited: true, invitationStatus: TrainerInvitationStatus.Pending, invitationExpiresAt: DateTimeOffset.UtcNow.AddDays(2));
        var revoked = await SeedDashboardTraineeAsync(trainer.Id, "revoked-hidden-page", "revoked-hidden-page@example.com", invited: true, invitationStatus: TrainerInvitationStatus.Revoked, invitationExpiresAt: DateTimeOffset.UtcNow.AddDays(-1));
        SetAuthorizationHeader(trainer.Id);

        // Act
        var response = await Client.GetAsync("/api/trainer/trainees?page=2&pageSize=2");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await ParseDashboardResponseAsync(response);
        body.GetProperty("page").GetInt32().Should().Be(2);
        body.GetProperty("pageSize").GetInt32().Should().Be(2);
        body.GetProperty("total").GetInt32().Should().Be(2);
        body.GetProperty("items").EnumerateArray().Should().NotContain(x => x.GetProperty("_id").GetString() == revoked.Id.ToString());
        body.GetProperty("items").EnumerateArray().Should().BeEmpty();
    }

    [Test]
    public async Task TrainerDashboardGridifyPagination_SearchDoesNotListRevokedInvitationRows()
    {
        // Arrange
        var trainer = await SeedTrainerAsync("trainer-grid-revoked-search", "trainer-grid-revoked-search@example.com");
        var linked = await SeedDashboardTraineeAsync(trainer.Id, "match linked visible", "match-linked-visible@example.com", linked: true, linkedAt: DateTimeOffset.UtcNow.AddDays(-2));
        var pending = await SeedDashboardTraineeAsync(trainer.Id, "match pending visible", "match-pending-visible@example.com", invited: true, invitationStatus: TrainerInvitationStatus.Pending, invitationExpiresAt: DateTimeOffset.UtcNow.AddDays(2));
        var revoked = await SeedDashboardTraineeAsync(trainer.Id, "match revoked hidden", "match-revoked-hidden@example.com", invited: true, invitationStatus: TrainerInvitationStatus.Revoked, invitationExpiresAt: DateTimeOffset.UtcNow.AddDays(-1));
        SetAuthorizationHeader(trainer.Id);

        // Act
        var response = await Client.GetAsync("/api/trainer/trainees?search=match&page=1&pageSize=10");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await ParseDashboardResponseAsync(response);
        body.GetProperty("total").GetInt32().Should().Be(2);

        var itemIds = body.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("_id").GetString()).ToArray();
        itemIds.Should().Contain(new[] { linked.Id.ToString(), pending.Id.ToString() });
        itemIds.Should().NotContain(revoked.Id.ToString());
    }

    [Test]
    public async Task TrainerDashboardGridifyPagination_NoRelationshipStatusReturnsEmptyPage()
    {
        // Arrange
        var trainer = await SeedTrainerAsync("trainer-grid-no-relationship", "trainer-grid-no-relationship@example.com");
        await SeedDashboardTraineeAsync(trainer.Id, "linked-visible-no-relationship", "linked-visible-no-relationship@example.com", linked: true, linkedAt: DateTimeOffset.UtcNow.AddDays(-2));
        await SeedDashboardTraineeAsync(trainer.Id, "pending-visible-no-relationship", "pending-visible-no-relationship@example.com", invited: true, invitationStatus: TrainerInvitationStatus.Pending, invitationExpiresAt: DateTimeOffset.UtcNow.AddDays(2));
        await SeedDashboardTraineeAsync(trainer.Id, "revoked-hidden-no-relationship", "revoked-hidden-no-relationship@example.com", invited: true, invitationStatus: TrainerInvitationStatus.Revoked, invitationExpiresAt: DateTimeOffset.UtcNow.AddDays(-1));
        SetAuthorizationHeader(trainer.Id);

        // Act
        var response = await Client.GetAsync("/api/trainer/trainees?status=NoRelationship&page=1&pageSize=10");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await ParseDashboardResponseAsync(response);
        body.GetProperty("total").GetInt32().Should().Be(0);
        body.GetProperty("items").EnumerateArray().Should().BeEmpty();
    }

    [Test]
    public async Task TrainerDashboardGridifyPagination_ReturnsEmptyPageBeyondTotal()
    {
        // Arrange
        var trainer = await SeedTrainerAsync("trainer-grid-beyond", "trainer-grid-beyond@example.com");
        SetAuthorizationHeader(trainer.Id);

        // Act: request a page far beyond any reasonable total
        var response = await Client.GetAsync("/api/trainer/trainees?page=99999&pageSize=10");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await ParseDashboardResponseAsync(response);
        body.GetProperty("items").EnumerateArray().Should().BeEmpty();
    }

    [Test]
    public async Task TrainerDashboardGridifyPagination_UsesLatestInvitationTieBreakForAConsistentExpiredStatus()
    {
        // Arrange
        var trainer = await SeedTrainerAsync("trainer-grid-latest", "trainer-grid-latest@example.com");
        var trainee = await SeedUserAsync(name: "Latest invitation trainee", email: "latest-invitation@example.com", password: "password123");
        var createdAt = new DateTimeOffset(2025, 1, 1, 12, 0, 0, TimeSpan.Zero);

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.TrainerInvitations.AddRange(
                new TrainerInvitation
                {
                    Id = new Id<TrainerInvitation>(new Guid("10000000-0000-0000-0000-000000000001")),
                    TrainerId = trainer.Id,
                    TraineeId = trainee.Id,
                    InviteeEmail = trainee.Email,
                    Code = "LATEST-PENDING",
                    Status = TrainerInvitationStatus.Pending,
                    ExpiresAt = new DateTimeOffset(2030, 1, 1, 12, 0, 0, TimeSpan.Zero),
                    CreatedAt = createdAt,
                    UpdatedAt = createdAt
                },
                new TrainerInvitation
                {
                    Id = new Id<TrainerInvitation>(new Guid("f0000000-0000-0000-0000-000000000001")),
                    TrainerId = trainer.Id,
                    TraineeId = trainee.Id,
                    InviteeEmail = trainee.Email,
                    Code = "LATEST-EXPIRED",
                    Status = TrainerInvitationStatus.Pending,
                    ExpiresAt = new DateTimeOffset(2020, 1, 1, 12, 0, 0, TimeSpan.Zero),
                    CreatedAt = createdAt,
                    UpdatedAt = createdAt
                });
            await db.SaveChangesAsync();
        }

        SetAuthorizationHeader(trainer.Id);

        // Act
        var response = await Client.GetAsync("/api/trainer/trainees?status=InvitationExpired&page=1&pageSize=10");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await ParseDashboardResponseAsync(response);
        body.GetProperty("total").GetInt32().Should().Be(1);
        var item = body.GetProperty("items").EnumerateArray().Should().ContainSingle().Subject;
        item.GetProperty("_id").GetString().Should().Be(trainee.Id.ToString());
        item.GetProperty("status").GetString().Should().Be("InvitationExpired");
        item.GetProperty("hasPendingInvitation").GetBoolean().Should().BeFalse();
        item.GetProperty("hasExpiredInvitation").GetBoolean().Should().BeTrue();
    }

    [Test]
    public async Task TrainerDashboardGridifyPagination_ExcludesDeletedAndMissingProfilesBeforeMixedCaseSearchAndPaging()
    {
        // Arrange
        var trainer = await SeedTrainerAsync("trainer-grid-enriched", "trainer-grid-enriched@example.com");
        var matchingByName = await SeedUserAsync(name: "Alpha MIXED profile", email: "alpha@example.com", password: "password123");
        var matchingByEmail = await SeedUserAsync(name: "Zulu profile", email: "zulu.MIXED@example.com", password: "password123");
        var deleted = await SeedUserAsync(name: "Aardvark MIXED deleted", email: "deleted@example.com", password: "password123");
        var linkedAt = new DateTimeOffset(2025, 2, 2, 8, 0, 0, TimeSpan.Zero);

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var deletedProfile = await db.Users.SingleAsync(user => user.Id == deleted.Id);
            deletedProfile.IsDeleted = true;
            db.TrainerTraineeLinks.AddRange(
                new TrainerTraineeLink
                {
                    Id = new Id<TrainerTraineeLink>(new Guid("20000000-0000-0000-0000-000000000001")),
                    TrainerId = trainer.Id,
                    TraineeId = matchingByName.Id,
                    CreatedAt = linkedAt,
                    UpdatedAt = linkedAt
                },
                new TrainerTraineeLink
                {
                    Id = new Id<TrainerTraineeLink>(new Guid("20000000-0000-0000-0000-000000000002")),
                    TrainerId = trainer.Id,
                    TraineeId = matchingByEmail.Id,
                    CreatedAt = linkedAt,
                    UpdatedAt = linkedAt
                },
                new TrainerTraineeLink
                {
                    Id = new Id<TrainerTraineeLink>(new Guid("20000000-0000-0000-0000-000000000003")),
                    TrainerId = trainer.Id,
                    TraineeId = deleted.Id,
                    CreatedAt = linkedAt,
                    UpdatedAt = linkedAt
                },
                new TrainerTraineeLink
                {
                    Id = new Id<TrainerTraineeLink>(new Guid("20000000-0000-0000-0000-000000000004")),
                    TrainerId = trainer.Id,
                    TraineeId = new Id<User>(new Guid("30000000-0000-0000-0000-000000000001")),
                    CreatedAt = linkedAt,
                    UpdatedAt = linkedAt
                });
            await db.SaveChangesAsync();
        }

        SetAuthorizationHeader(trainer.Id);

        // Act
        var response = await Client.GetAsync("/api/trainer/trainees?search=mIxEd&sortBy=name&sortDirection=asc&page=2&pageSize=1");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await ParseDashboardResponseAsync(response);
        body.GetProperty("total").GetInt32().Should().Be(2);
        body.GetProperty("items").EnumerateArray().Should().ContainSingle(item => item.GetProperty("_id").GetString() == matchingByEmail.Id.ToString());
    }

    [Test]
    public async Task TrainerDashboardGridifyPagination_UsesStatusAliasDefaultNameAndIdTieBreaks()
    {
        // Arrange
        var trainer = await SeedTrainerAsync("trainer-grid-alias", "trainer-grid-alias@example.com");
        var firstDuplicate = await SeedUserAsync(name: "Duplicate profile", email: "duplicate-one@example.com", password: "password123");
        var secondDuplicate = await SeedUserAsync(name: "Duplicate profile", email: "duplicate-two@example.com", password: "password123");
        var pending = await SeedUserAsync(name: "Pending profile", email: "pending-profile@example.com", password: "password123");
        var expired = await SeedUserAsync(name: "Expired profile", email: "expired-profile@example.com", password: "password123");
        var createdAt = new DateTimeOffset(2025, 3, 3, 8, 0, 0, TimeSpan.Zero);

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.TrainerTraineeLinks.AddRange(
                new TrainerTraineeLink
                {
                    Id = new Id<TrainerTraineeLink>(new Guid("40000000-0000-0000-0000-000000000001")),
                    TrainerId = trainer.Id,
                    TraineeId = firstDuplicate.Id,
                    CreatedAt = createdAt,
                    UpdatedAt = createdAt
                },
                new TrainerTraineeLink
                {
                    Id = new Id<TrainerTraineeLink>(new Guid("40000000-0000-0000-0000-000000000002")),
                    TrainerId = trainer.Id,
                    TraineeId = secondDuplicate.Id,
                    CreatedAt = createdAt,
                    UpdatedAt = createdAt
                });
            db.TrainerInvitations.AddRange(
                new TrainerInvitation
                {
                    Id = new Id<TrainerInvitation>(new Guid("50000000-0000-0000-0000-000000000001")),
                    TrainerId = trainer.Id,
                    TraineeId = pending.Id,
                    InviteeEmail = pending.Email,
                    Code = "STATUS-PENDING",
                    Status = TrainerInvitationStatus.Pending,
                    ExpiresAt = new DateTimeOffset(2030, 3, 3, 8, 0, 0, TimeSpan.Zero),
                    CreatedAt = createdAt,
                    UpdatedAt = createdAt
                },
                new TrainerInvitation
                {
                    Id = new Id<TrainerInvitation>(new Guid("50000000-0000-0000-0000-000000000002")),
                    TrainerId = trainer.Id,
                    TraineeId = expired.Id,
                    InviteeEmail = expired.Email,
                    Code = "STATUS-EXPIRED",
                    Status = TrainerInvitationStatus.Pending,
                    ExpiresAt = new DateTimeOffset(2020, 3, 3, 8, 0, 0, TimeSpan.Zero),
                    CreatedAt = createdAt,
                    UpdatedAt = createdAt
                });
            await db.SaveChangesAsync();
        }

        SetAuthorizationHeader(trainer.Id);

        // Act
        var defaultResponse = await Client.GetAsync("/api/trainer/trainees?page=1&pageSize=2");
        var statusResponse = await Client.GetAsync("/api/trainer/trainees?sortBy=status&sortDirection=asc&page=1&pageSize=10");

        // Assert
        defaultResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        statusResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var defaultBody = await ParseDashboardResponseAsync(defaultResponse);
        var duplicateIds = new[] { firstDuplicate, secondDuplicate }
            .OrderBy(user => user.Id)
            .Select(user => user.Id.ToString());
        defaultBody.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("_id").GetString()).Should().Equal(duplicateIds);

        var statusBody = await ParseDashboardResponseAsync(statusResponse);
        statusBody.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("status").GetString())
            .Should().Equal("Linked", "Linked", "InvitationPending", "InvitationExpired");
    }

    private async Task<User> SeedTrainerAsync(string name, string email)
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

        await db.SaveChangesAsync();

        return trainer;
    }

    private async Task<User> SeedDashboardTraineeAsync(
        Id<User> trainerId,
        string name,
        string email,
        bool linked = false,
        DateTimeOffset? linkedAt = null,
        bool invited = false,
        TrainerInvitationStatus invitationStatus = TrainerInvitationStatus.Pending,
        DateTimeOffset? invitationExpiresAt = null)
    {
        var trainee = await SeedUserAsync(name: name, email: email, password: "password123");

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        if (linked)
        {
            db.TrainerTraineeLinks.Add(new TrainerTraineeLink
            {
                Id = Id<TrainerTraineeLink>.New(),
                TrainerId = trainerId,
                TraineeId = trainee.Id,
                CreatedAt = linkedAt ?? DateTimeOffset.UtcNow,
                UpdatedAt = linkedAt ?? DateTimeOffset.UtcNow
            });
        }

        if (invited)
        {
            db.TrainerInvitations.Add(new TrainerInvitation
            {
                Id = Id<TrainerInvitation>.New(),
                TrainerId = trainerId,
                TraineeId = trainee.Id,
                Code = $"INV-{Guid.NewGuid():N}",
                Status = invitationStatus,
                ExpiresAt = invitationExpiresAt ?? DateTimeOffset.UtcNow.AddDays(1),
                CreatedAt = invitationExpiresAt ?? DateTimeOffset.UtcNow,
                UpdatedAt = invitationExpiresAt ?? DateTimeOffset.UtcNow
            });
        }

        await db.SaveChangesAsync();

        return trainee;
    }

    private static async Task<JsonElement> ParseDashboardResponseAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
