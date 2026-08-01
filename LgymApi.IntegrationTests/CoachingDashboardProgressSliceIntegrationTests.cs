using FluentAssertions;
using LgymApi.Application.Coaching.Progress.EloChart;
using LgymApi.Application.Coaching.Progress.ExerciseScoresChart;
using LgymApi.Application.Coaching.Progress.MainRecordsHistory;
using LgymApi.Application.Coaching.Progress.TrainingByDate;
using LgymApi.Application.Coaching.Progress.TrainingDates;
using LgymApi.Application.Coaching.Relationships.TrainerDashboard;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.Coaching.Errors;
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
public sealed class CoachingDashboardProgressSliceIntegrationTests : IntegrationTestBase
{
    [Test]
    public async Task TrainerDashboard_EnrichesBeforeStatusSortCountAndPage()
    {
        var trainer = await SeedUserAsync("slice-dashboard-trainer", "slice-dashboard-trainer@example.test");
        var linked = await SeedUserAsync("slice dashboard linked", "slice-dashboard-linked@example.test");
        var pending = await SeedUserAsync("slice dashboard pending", "slice-dashboard-pending@example.test");
        var expired = await SeedUserAsync("slice dashboard expired", "slice-dashboard-expired@example.test");
        var deleted = await SeedUserAsync("slice dashboard deleted", "slice-dashboard-deleted@example.test", isDeleted: true);
        var timestamp = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

        using (var seedScope = Factory.Services.CreateScope())
        {
            var database = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();
            database.UserRoles.Add(new UserRole { UserId = trainer.Id, RoleId = RoleSeedDataConfiguration.TrainerRoleSeedId });
            database.TrainerTraineeLinks.Add(new TrainerTraineeLink
            {
                Id = Id<TrainerTraineeLink>.New(),
                TrainerId = trainer.Id,
                TraineeId = linked.Id,
                CreatedAt = timestamp,
                UpdatedAt = timestamp
            });
            database.TrainerInvitations.AddRange(
                Invitation("10000000-0000-0000-0000-000000000001", trainer.Id, pending.Id, pending.Email.Value, timestamp, timestamp.AddYears(10)),
                Invitation("20000000-0000-0000-0000-000000000001", trainer.Id, expired.Id, expired.Email.Value, timestamp, timestamp.AddYears(10)),
                Invitation("f0000000-0000-0000-0000-000000000001", trainer.Id, expired.Id, expired.Email.Value, timestamp, timestamp.AddYears(-10)),
                Invitation("30000000-0000-0000-0000-000000000001", trainer.Id, deleted.Id, deleted.Email.Value, timestamp, timestamp.AddYears(10)));
            await database.SaveChangesAsync();
        }

        using var actionScope = Factory.Services.CreateScope();
        var useCase = actionScope.ServiceProvider.GetRequiredService<IGetTrainerDashboardUseCase>();
        var result = await useCase.ExecuteAsync(new GetTrainerDashboardQuery(
            trainer.Id,
            Search: "SLICE DASHBOARD",
            SortBy: "status",
            SortDirection: "asc",
            Page: 2,
            PageSize: 1));

        result.IsSuccess.Should().BeTrue();
        result.Value.TotalCount.Should().Be(3);
        result.Value.Items.Should().ContainSingle(item =>
            item.Id == pending.Id
            && item.Status == TrainerDashboardTraineeStatus.InvitationPending
            && item.HasPendingInvitation);
    }

    [Test]
    public async Task ProgressSlices_ReturnEveryWorkoutProgressReadForOwnedTrainee()
    {
        var trainer = await SeedUserAsync("slice-progress-trainer", "slice-progress-trainer@example.test");
        var trainee = await SeedUserAsync("slice-progress-trainee", "slice-progress-trainee@example.test");
        var createdAt = new DateTime(2026, 2, 2, 10, 0, 0, DateTimeKind.Utc);
        var exerciseId = Id<Exercise>.New();
        const string gymName = "Slice gym";

        using (var seedScope = Factory.Services.CreateScope())
        {
            var database = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();
            database.UserRoles.Add(new UserRole { UserId = trainer.Id, RoleId = RoleSeedDataConfiguration.TrainerRoleSeedId });
            database.TrainerTraineeLinks.Add(new TrainerTraineeLink
            {
                Id = Id<TrainerTraineeLink>.New(),
                TrainerId = trainer.Id,
                TraineeId = trainee.Id
            });
            var plan = new Plan { Id = Id<Plan>.New(), UserId = trainee.Id, Name = "Slice plan" };
            var planDay = new PlanDay { Id = Id<PlanDay>.New(), PlanId = plan.Id, Name = "Slice day" };
            var gym = new Gym { Id = Id<Gym>.New(), UserId = trainee.Id, Name = gymName };
            var exercise = new Exercise { Id = exerciseId, Name = "Slice squat", BodyPart = BodyParts.Quads };
            var training = new Training
            {
                Id = Id<Training>.New(),
                UserId = trainee.Id,
                TypePlanDayId = planDay.Id,
                GymId = gym.Id,
                CreatedAt = createdAt
            };
            var score = new ExerciseScore
            {
                Id = Id<ExerciseScore>.New(),
                ExerciseId = exercise.Id,
                UserId = trainee.Id,
                Reps = 5,
                Series = 1,
                Weight = 100,
                Unit = WeightUnits.Kilograms,
                TrainingId = training.Id,
                CreatedAt = createdAt
            };
            database.Plans.Add(plan);
            database.PlanDays.Add(planDay);
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
        byDate.Value.Should().ContainSingle(item => item.Gym == gymName);
        scores.IsSuccess.Should().BeTrue();
        scores.Value.Should().ContainSingle(item => item.ExerciseId == exerciseId);
        elo.IsSuccess.Should().BeTrue();
        elo.Value.Should().Contain(item => item.Value == 1010);
        records.IsSuccess.Should().BeTrue();
        records.Value.Should().ContainSingle(item => item.Weight == 100);

    }

    [Test]
    public async Task ProgressSlices_RejectForeignAndEmptyTraineeIdsBeforeWorkoutReads()
    {
        var trainer = await SeedUserAsync("slice-progress-foreign-trainer", "slice-progress-foreign-trainer@example.test");
        var trainee = await SeedUserAsync("slice-progress-foreign-trainee", "slice-progress-foreign-trainee@example.test");
        using (var seedScope = Factory.Services.CreateScope())
        {
            var database = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();
            database.UserRoles.Add(new UserRole { UserId = trainer.Id, RoleId = RoleSeedDataConfiguration.TrainerRoleSeedId });
            await database.SaveChangesAsync();
        }

        using var actionScope = Factory.Services.CreateScope();
        var foreign = await actionScope.ServiceProvider.GetRequiredService<IGetTrainingDatesUseCase>()
            .ExecuteAsync(new GetTrainingDatesQuery(trainer.Id.Rebind<AccountReference>(), trainee.Id.Rebind<AccountReference>()));
        var empty = await actionScope.ServiceProvider.GetRequiredService<IGetEloChartUseCase>()
            .ExecuteAsync(new GetEloChartQuery(trainer.Id.Rebind<AccountReference>(), Id<AccountReference>.Empty));

        foreign.Error.Should().BeOfType<TrainerRelationshipNotFoundError>();
        empty.Error.Should().BeOfType<InvalidTrainerRelationshipError>();
    }

    private static TrainerInvitation Invitation(
        string id,
        Id<User> trainerId,
        Id<User> traineeId,
        string email,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt) => new()
        {
            Id = ParseInvitationId(id),
            TrainerId = trainerId,
            TraineeId = traineeId,
            InviteeEmail = email,
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
