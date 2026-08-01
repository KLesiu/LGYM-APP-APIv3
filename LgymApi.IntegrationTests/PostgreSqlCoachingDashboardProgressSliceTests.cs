using FluentAssertions;
using LgymApi.Application.Coaching.Progress.EloChart;
using LgymApi.Application.Coaching.Progress.ExerciseScoresChart;
using LgymApi.Application.Coaching.Progress.MainRecordsHistory;
using LgymApi.Application.Coaching.Progress.TrainingByDate;
using LgymApi.Application.Coaching.Progress.TrainingDates;
using LgymApi.Application.Coaching.Relationships.TrainerDashboard;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using LgymApi.Infrastructure.Data;
using LgymApi.Infrastructure.Data.SeedData;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LgymApi.IntegrationTests;

[TestFixture]
[NonParallelizable]
[Category("PostgreSql")]
internal sealed class PostgreSqlCoachingDashboardProgressSliceTests : PostgreSqlIntegrationTestBase
{
    [Test]
    public async Task DashboardSlice_EnrichesBeforeStatusSortCountAndPageOnPostgreSql()
    {
        var trainer = await SeedTrainerAsync("pg-slice-dashboard-trainer", "pg-slice-dashboard-trainer@example.test");
        var linked = await SeedUserAsync("PG slice linked", "pg-slice-linked@example.test");
        var pending = await SeedUserAsync("PG slice pending", "pg-slice-pending@example.test");
        var expired = await SeedUserAsync("PG slice expired", "pg-slice-expired@example.test");
        var deleted = await SeedUserAsync("PG slice deleted", "pg-slice-deleted@example.test");
        var timestamp = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

        using (var seedScope = Factory.Services.CreateScope())
        {
            var database = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await database.Users.SingleAsync(user => user.Id == deleted.Id)).IsDeleted = true;
            database.TrainerTraineeLinks.Add(Link(trainer.Id, linked.Id, timestamp));
            database.TrainerInvitations.AddRange(
                Invitation("10000000-0000-0000-0000-000000000015", trainer.Id, pending, timestamp, timestamp.AddYears(10)),
                Invitation("20000000-0000-0000-0000-000000000015", trainer.Id, expired, timestamp, timestamp.AddYears(10)),
                Invitation("f0000000-0000-0000-0000-000000000015", trainer.Id, expired, timestamp, timestamp.AddYears(-10)),
                Invitation("30000000-0000-0000-0000-000000000015", trainer.Id, deleted, timestamp, timestamp.AddYears(10)));
            await database.SaveChangesAsync();
        }

        using var actionScope = Factory.Services.CreateScope();
        var result = await actionScope.ServiceProvider.GetRequiredService<IGetTrainerDashboardUseCase>()
            .ExecuteAsync(new GetTrainerDashboardQuery(
                trainer.Id,
                Search: "pg SLICE",
                SortBy: "status",
                SortDirection: "asc",
                Page: 2,
                PageSize: 1));

        result.IsSuccess.Should().BeTrue();
        result.Value.TotalCount.Should().Be(3);
        result.Value.Items.Should().ContainSingle(item =>
            item.Id == pending.Id
            && item.Status == TrainerDashboardTraineeStatus.InvitationPending);
    }

    [Test]
    public async Task ProgressSlices_ReturnAllFiveReadsOnPostgreSql()
    {
        var trainer = await SeedTrainerAsync("pg-slice-progress-trainer", "pg-slice-progress-trainer@example.test");
        var trainee = await SeedUserAsync("pg-slice-progress-trainee", "pg-slice-progress-trainee@example.test");
        var createdAt = new DateTime(2026, 2, 2, 10, 0, 0, DateTimeKind.Utc);
        var exerciseId = Id<Exercise>.New();

        using (var seedScope = Factory.Services.CreateScope())
        {
            var database = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();
            database.TrainerTraineeLinks.Add(Link(trainer.Id, trainee.Id, createdAt));
            var plan = new Plan { Id = Id<Plan>.New(), UserId = trainee.Id, Name = "PG slice plan" };
            var day = new PlanDay { Id = Id<PlanDay>.New(), PlanId = plan.Id, Name = "PG slice day" };
            var gym = new Gym { Id = Id<Gym>.New(), UserId = trainee.Id, Name = "PG slice gym" };
            var exercise = new Exercise { Id = exerciseId, Name = "PG slice squat", BodyPart = BodyParts.Quads };
            var training = new Training
            {
                Id = Id<Training>.New(),
                UserId = trainee.Id,
                TypePlanDayId = day.Id,
                GymId = gym.Id,
                CreatedAt = createdAt
            };
            var score = new ExerciseScore
            {
                Id = Id<ExerciseScore>.New(),
                ExerciseId = exerciseId,
                UserId = trainee.Id,
                Reps = 5,
                Series = 1,
                Weight = 100,
                Unit = WeightUnits.Kilograms,
                TrainingId = training.Id,
                CreatedAt = createdAt
            };
            database.Plans.Add(plan);
            database.PlanDays.Add(day);
            database.Gyms.Add(gym);
            database.Exercises.Add(exercise);
            database.Trainings.Add(training);
            database.ExerciseScores.Add(score);
            database.TrainingExerciseScores.Add(new TrainingExerciseScore
            {
                Id = Id<TrainingExerciseScore>.New(),
                TrainingId = training.Id,
                ExerciseScoreId = score.Id
            });
            database.EloRegistries.Add(new EloRegistry
            {
                Id = Id<EloRegistry>.New(),
                UserId = trainee.Id,
                Date = createdAt,
                Elo = 1010
            });
            database.MainRecords.Add(new MainRecord
            {
                Id = Id<MainRecord>.New(),
                UserId = trainee.Id,
                ExerciseId = exerciseId,
                Weight = 100,
                Unit = WeightUnits.Kilograms,
                Date = createdAt
            });
            await database.SaveChangesAsync();
        }

        using var actionScope = Factory.Services.CreateScope();
        var dates = await actionScope.ServiceProvider.GetRequiredService<IGetTrainingDatesUseCase>()
            .ExecuteAsync(new GetTrainingDatesQuery(trainer.Id.Rebind<AccountReference>(), trainee.Id.Rebind<AccountReference>()));
        var byDate = await actionScope.ServiceProvider.GetRequiredService<IGetTrainingByDateUseCase>()
            .ExecuteAsync(new GetTrainingByDateQuery(trainer.Id.Rebind<AccountReference>(), trainee.Id.Rebind<AccountReference>(), createdAt));
        var scores = await actionScope.ServiceProvider.GetRequiredService<IGetExerciseScoresChartUseCase>()
            .ExecuteAsync(new GetExerciseScoresChartQuery(trainer.Id.Rebind<AccountReference>(), trainee.Id.Rebind<AccountReference>(), exerciseId));
        var elo = await actionScope.ServiceProvider.GetRequiredService<IGetEloChartUseCase>()
            .ExecuteAsync(new GetEloChartQuery(trainer.Id.Rebind<AccountReference>(), trainee.Id.Rebind<AccountReference>()));
        var records = await actionScope.ServiceProvider.GetRequiredService<IGetMainRecordsHistoryUseCase>()
            .ExecuteAsync(new GetMainRecordsHistoryQuery(trainer.Id.Rebind<AccountReference>(), trainee.Id.Rebind<AccountReference>()));

        dates.IsSuccess.Should().BeTrue();
        dates.Value.Should().ContainSingle();
        byDate.IsSuccess.Should().BeTrue();
        byDate.Value.Should().ContainSingle();
        scores.IsSuccess.Should().BeTrue();
        scores.Value.Should().ContainSingle();
        elo.IsSuccess.Should().BeTrue();
        elo.Value.Should().Contain(item => item.Value == 1010);
        records.IsSuccess.Should().BeTrue();
        records.Value.Should().ContainSingle(item => item.Weight == 100);
    }

    private async Task<User> SeedTrainerAsync(string name, string email)
    {
        var trainer = await SeedUserAsync(name, email);
        using var scope = Factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        database.UserRoles.Add(new UserRole { UserId = trainer.Id, RoleId = RoleSeedDataConfiguration.TrainerRoleSeedId });
        await database.SaveChangesAsync();
        return trainer;
    }

    private static TrainerTraineeLink Link(Id<User> trainerId, Id<User> traineeId, DateTimeOffset timestamp) => new()
    {
        Id = Id<TrainerTraineeLink>.New(),
        TrainerId = trainerId,
        TraineeId = traineeId,
        CreatedAt = timestamp,
        UpdatedAt = timestamp
    };

    private static TrainerInvitation Invitation(
        string id,
        Id<User> trainerId,
        User trainee,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt) => new()
        {
            Id = ParseInvitationId(id),
            TrainerId = trainerId,
            TraineeId = trainee.Id,
            InviteeEmail = trainee.Email,
            Code = id[..12],
            Status = TrainerInvitationStatus.Pending,
            CreatedAt = createdAt,
            UpdatedAt = createdAt,
            ExpiresAt = expiresAt
        };

    private static Id<TrainerInvitation> ParseInvitationId(string value) =>
        Id<TrainerInvitation>.TryParse(value, out var invitationId)
            ? invitationId
            : throw new ArgumentException("Invitation ID must be valid.", nameof(value));
}
