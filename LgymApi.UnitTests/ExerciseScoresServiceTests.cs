using FluentAssertions;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.WorkoutProgress.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Features.AdminManagement.Models;
using LgymApi.Application.Features.ExerciseScores;
using LgymApi.Application.Models;
using LgymApi.Application.Pagination;
using LgymApi.Application.Repositories;
using LgymApi.Application.WorkoutProgress.ProgressData;
using LgymApi.Application.WorkoutProgress.ProgressData.Models;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using NSubstitute;
using NUnit.Framework;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class ExerciseScoresServiceTests
{
    [Test]
    public async Task GetExerciseScoresChartDataAsync_WithEmptyUserId_ReturnsInvalidExerciseScoreError()
    {
        var progress = Substitute.For<IWorkoutProgressReadWriteService>();
        progress.GetExerciseScoreChartAsync(Id<User>.Empty, Arg.Any<Id<Exercise>>(), Arg.Any<CancellationToken>()).Returns(Result<List<ExerciseScoreChartPoint>, AppError>.Failure(new InvalidExerciseScoreError("invalid")));
        var service = new ExerciseScoresService(progress);

        var exerciseId = Id<Exercise>.New();
        var result = await service.GetExerciseScoresChartDataAsync(Id<User>.Empty, exerciseId);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<InvalidExerciseScoreError>();
    }

    [Test]
    public async Task GetExerciseScoresChartDataAsync_WithEmptyExerciseId_ReturnsInvalidExerciseScoreError()
    {
        var progress = Substitute.For<IWorkoutProgressReadWriteService>();
        progress.GetExerciseScoreChartAsync(Arg.Any<Id<User>>(), Id<Exercise>.Empty, Arg.Any<CancellationToken>()).Returns(Result<List<ExerciseScoreChartPoint>, AppError>.Failure(new InvalidExerciseScoreError("invalid")));
        var service = new ExerciseScoresService(progress);

        var userId = Id<User>.New();
        var result = await service.GetExerciseScoresChartDataAsync(userId, Id<Exercise>.Empty);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<InvalidExerciseScoreError>();
    }

    [Test]
    public async Task GetExerciseScoresChartDataAsync_WhenScoresShareATraining_ReturnsBestScoresInFirstTrainingOrder()
    {
        var userId = Id<User>.New();
        var exerciseId = Id<Exercise>.New();
        var firstTrainingId = Id<Training>.New();
        var secondTrainingId = Id<Training>.New();
        var firstTraining = new Training
        {
            Id = firstTrainingId,
            CreatedAt = new DateTimeOffset(2026, 1, 2, 8, 0, 0, TimeSpan.Zero)
        };
        var secondTraining = new Training
        {
            Id = secondTrainingId,
            CreatedAt = new DateTimeOffset(2026, 1, 3, 8, 0, 0, TimeSpan.Zero)
        };
        var exercise = new Exercise { Id = exerciseId, Name = "Squat" };
        var scores = new List<ExerciseScore>
        {
            new()
            {
                Id = Id<ExerciseScore>.New(),
                UserId = userId,
                ExerciseId = exerciseId,
                TrainingId = secondTrainingId,
                Reps = 5,
                Weight = 80,
                Exercise = exercise,
                Training = secondTraining,
                CreatedAt = new DateTimeOffset(2026, 1, 3, 8, 0, 0, TimeSpan.Zero)
            },
            new()
            {
                Id = Id<ExerciseScore>.New(),
                UserId = userId,
                ExerciseId = exerciseId,
                TrainingId = firstTrainingId,
                Reps = 5,
                Weight = 100,
                Exercise = exercise,
                Training = firstTraining,
                CreatedAt = new DateTimeOffset(2026, 1, 2, 8, 0, 0, TimeSpan.Zero)
            },
            new()
            {
                Id = Id<ExerciseScore>.New(),
                UserId = userId,
                ExerciseId = exerciseId,
                TrainingId = firstTrainingId,
                Reps = 5,
                Weight = 150,
                Exercise = exercise,
                Training = firstTraining,
                CreatedAt = new DateTimeOffset(2026, 1, 2, 9, 0, 0, TimeSpan.Zero)
            }
        };
        var progress = Substitute.For<IWorkoutProgressReadWriteService>();
        progress.GetExerciseScoreChartAsync(userId, exerciseId, Arg.Any<CancellationToken>()).Returns(Result<List<ExerciseScoreChartPoint>, AppError>.Success([
            new ExerciseScoreChartPoint($"{exerciseId}-{firstTrainingId}", 173, "01/02", "Squat", exerciseId),
            new ExerciseScoreChartPoint($"{exerciseId}-{secondTrainingId}", 92, "01/03", "Squat", exerciseId)]));
        var service = new ExerciseScoresService(progress);

        var result = await service.GetExerciseScoresChartDataAsync(userId, exerciseId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Select(entry => entry.Id).Should().Equal($"{exerciseId}-{firstTrainingId}", $"{exerciseId}-{secondTrainingId}");
        result.Value.Select(entry => entry.Value).Should().Equal(173, 92);
        result.Value.Select(entry => entry.Date).Should().Equal("01/02", "01/03");
        result.Value.Select(entry => entry.ExerciseId).Should().OnlyContain(id => id == exerciseId);
    }

    private sealed class NoOpUserRepository : IUserRepository
    {
        public Task<User?> FindByIdAsync(Id<User> id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<User?> FindByIdIncludingDeletedAsync(Id<User> id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<User?> FindByIdWithRolesAsync(Id<User> id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<User?> FindByNameAsync(string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<User?> FindByEmailAsync(Email email, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<User?> FindByNameOrEmailAsync(string name, string email, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<LgymApi.Application.Models.UserRankingEntry>> GetRankingAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task AddAsync(User user, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UpdateAsync(User user, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Pagination<UserResult>> GetUsersPaginatedAsync(FilterInput filterInput, bool includeDeleted, CancellationToken cancellationToken = default)
            => Task.FromResult(new Pagination<UserResult>());
    }

    private sealed class NoOpExerciseScoreRepository : IExerciseScoreRepository
    {
        public Task AddRangeAsync(IEnumerable<ExerciseScore> scores, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<ExerciseScore>> GetByIdsAsync(List<Id<ExerciseScore>> ids, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<ExerciseScore>> GetByUserAndExerciseAsync(Id<User> userId, Id<Exercise> exerciseId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<ExerciseScore>> GetByUserAndExerciseAndGymAsync(Id<User> userId, Id<Exercise> exerciseId, Id<Gym>? gymId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<ExerciseScore>> GetByUserAndExercisesAsync(Id<User> userId, List<Id<Exercise>> exerciseIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<ExerciseScore>> GetLatestByUserExerciseSeriesAsync(Id<User> userId, Id<Exercise> exerciseId, Id<Gym>? gymId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ExerciseScore?> GetLatestByUserExerciseSeriesAsync(Id<User> userId, Id<Exercise> exerciseId, int series, Id<Gym>? gymId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ExerciseScore?> GetBestScoreAsync(Id<User> userId, Id<Exercise> exerciseId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
