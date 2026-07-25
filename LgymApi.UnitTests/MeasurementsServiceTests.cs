using FluentAssertions;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.WorkoutProgress.Errors;
using LgymApi.Application.Features.Measurements;
using LgymApi.Application.Features.Measurements.Models;
using LgymApi.Application.Repositories;
using LgymApi.Application.Platform.ReferenceData.Units;
using LgymApi.Application.Identity.Contracts.Access;
using LgymApi.Application.WorkoutProgress.Contracts.Measurements;
using LgymApi.Application.WorkoutProgress.ProgressData;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using NUnit.Framework;
using NSubstitute;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class MeasurementsServiceTests
{
    [Test]
    public async Task GetMeasurementDetailAsync_WhenMeasurementBelongsToDifferentUser_ReturnsForbidden()
    {
        var currentUser = new User { Id = Id<User>.New(), Name = "current", Email = "current-measure@example.com", ProfileRank = "Rookie" };
        var foreignUserId = Id<User>.New();
        var measurementId = Id<Measurement>.New();
        var measurement = new Measurement
        {
            Id = measurementId,
            UserId = foreignUserId,
            BodyPart = BodyParts.Chest,
            Unit = MeasurementUnits.Centimeters.ToString(),
            Value = 100
        };

        var service = CreateService(findById: (_, _) => Task.FromResult<Measurement?>(measurement));

        var result = await service.GetMeasurementDetailAsync(currentUser, measurementId);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<MeasurementForbiddenError>();
    }

    [Test]
    public async Task GetMeasurementsHistoryAsync_WhenTrainerOwnsTrainee_ReturnsTraineeMeasurements()
    {
        var trainerId = Id<User>.New();
        var traineeId = Id<User>.New();
        var trainer = CreateUser(trainerId, "trainer@example.com");
        var measurements = new List<Measurement>
        {
            CreateMeasurement(traineeId, BodyParts.BodyWeight, MeasurementUnits.Kilograms, 82, 0),
            CreateMeasurement(traineeId, BodyParts.BodyWeight, MeasurementUnits.Kilograms, 81.5, 3)
        };

        var service = CreateService(
            getByUser: (userId, _, _) => Task.FromResult(userId == traineeId ? measurements : new List<Measurement>()),
            isTrainer: (_, _) => Task.FromResult(true),
            hasActiveRelationship: (_, _, _) => Task.FromResult(true));

        var result = await service.GetMeasurementsHistoryAsync(trainer, traineeId, BodyParts.BodyWeight, MeasurementUnits.Kilograms);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
        result.Value.Should().OnlyContain(item => item.UserId == traineeId);
    }

    [Test]
    public async Task GetMeasurementsHistoryAsync_WhenTrainerRequestsLinkedTrainee_ForwardsAuthorizationToConsumerOwnedPort()
    {
        var trainerId = Id<User>.New();
        var traineeId = Id<User>.New();
        var cancellationToken = new CancellationTokenSource().Token;
        var forwardedIdentityId = Id<User>.Empty;
        var forwardedRelationshipTrainerId = Id<User>.Empty;
        var forwardedRelationshipTraineeId = Id<User>.Empty;
        var forwardedCancellationToken = CancellationToken.None;
        var service = CreateService(
            getByUser: (userId, _, _) => Task.FromResult(new List<Measurement>
            {
                CreateMeasurement(userId, BodyParts.BodyWeight, MeasurementUnits.Kilograms, 80, 0)
            }),
            isTrainer: (userId, token) =>
            {
                forwardedIdentityId = userId;
                forwardedCancellationToken = token;
                return Task.FromResult(true);
            },
            hasActiveRelationship: (trainerIdFromPort, traineeIdFromPort, token) =>
            {
                forwardedRelationshipTrainerId = trainerIdFromPort;
                forwardedRelationshipTraineeId = traineeIdFromPort;
                forwardedCancellationToken = token;
                return Task.FromResult(true);
            });

        var result = await service.GetMeasurementsHistoryAsync(
            CreateUser(trainerId, "linked-trainer@example.com"),
            traineeId,
            BodyParts.BodyWeight,
            MeasurementUnits.Kilograms,
            cancellationToken);

        result.IsSuccess.Should().BeTrue();
        forwardedIdentityId.Should().Be(trainerId);
        forwardedRelationshipTrainerId.Should().Be(trainerId);
        forwardedRelationshipTraineeId.Should().Be(traineeId);
        forwardedCancellationToken.Should().Be(cancellationToken);
    }

    [Test]
    public async Task GetMeasurementsHistoryAsync_WhenTraineeReadsOwnHistory_SkipsAuthorizationPorts()
    {
        var traineeId = Id<User>.New();
        var service = CreateService(
            getByUser: (userId, _, _) => Task.FromResult(new List<Measurement>
            {
                CreateMeasurement(userId, BodyParts.BodyWeight, MeasurementUnits.Kilograms, 80, 0)
            }),
            isTrainer: (_, _) => throw new AssertionException("Self access must not read the trainer role."),
            hasActiveRelationship: (_, _, _) => throw new AssertionException("Self access must not check a relationship."));

        var result = await service.GetMeasurementsHistoryAsync(
            CreateUser(traineeId, "trainee-self@example.com"),
            traineeId,
            BodyParts.BodyWeight,
            MeasurementUnits.Kilograms);

        result.IsSuccess.Should().BeTrue();
    }

    [Test]
    public async Task GetMeasurementsHistoryAsync_WhenCallerIsNotTrainer_ReturnsForbiddenWithoutRelationshipCheck()
    {
        var service = CreateService(
            isTrainer: (_, _) => Task.FromResult(false),
            hasActiveRelationship: (_, _, _) => throw new AssertionException("Non-trainers must not check relationships."));

        var result = await service.GetMeasurementsHistoryAsync(
            CreateUser(Id<User>.New(), "non-trainer@example.com"),
            Id<User>.New(),
            null,
            null);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<MeasurementForbiddenError>();
    }

    [Test]
    public async Task GetMeasurementsHistoryAsync_WhenTrainerIsNotLinkedToRequestedUser_ReturnsForbidden()
    {
        var service = CreateService(
            isTrainer: (_, _) => Task.FromResult(true),
            hasActiveRelationship: (_, _, _) => Task.FromResult(false));

        var result = await service.GetMeasurementsHistoryAsync(
            CreateUser(Id<User>.New(), "foreign-trainer@example.com"),
            Id<User>.New(),
            null,
            null);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<MeasurementForbiddenError>();
    }

    [Test]
    public async Task GetMeasurementsHistoryAsync_WhenRouteUserIdIsEmpty_ReturnsInvalidMeasurementErrorWithoutAuthorizationReads()
    {
        var service = CreateService(
            isTrainer: (_, _) => throw new AssertionException("An empty route ID must not read the trainer role."),
            hasActiveRelationship: (_, _, _) => throw new AssertionException("An empty route ID must not check a relationship."));

        var result = await service.GetMeasurementsHistoryAsync(
            CreateUser(Id<User>.New(), "empty-id@example.com"),
            Id<User>.Empty,
            null,
            null);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<InvalidMeasurementError>();
    }

    [Test]
    public async Task AddMeasurementAsync_WhenUnitDoesNotMatchBodyPart_ReturnsInvalidMeasurementError()
    {
        var currentUser = new User { Id = Id<User>.New(), Name = "user", Email = "weight-unit@example.com", ProfileRank = "Rookie" };
        var service = CreateService();

        var result = await service.AddMeasurementAsync(currentUser, BodyParts.BodyWeight, MeasurementUnits.Centimeters, 80);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<InvalidMeasurementError>();
    }

    [Test]
    public async Task AddMeasurementAsync_WhenValueIsNotPositive_ReturnsInvalidMeasurementError()
    {
        var currentUser = new User { Id = Id<User>.New(), Name = "user", Email = "weight-value@example.com", ProfileRank = "Rookie" };
        var service = CreateService();

        var result = await service.AddMeasurementAsync(currentUser, BodyParts.BodyWeight, MeasurementUnits.Kilograms, 0);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<InvalidMeasurementError>();
    }

    [Test]
    public async Task GetMeasurementsTrendAsync_WhenValueGrows_ReturnsUpDirectionAndDifference()
    {
        var userId = Id<User>.New();
        var currentUser = CreateUser(userId, "trend-up@example.com");
        var measurements = new List<Measurement>
        {
            CreateMeasurement(userId, BodyParts.BodyWeight, MeasurementUnits.Kilograms, 80, 0),
            CreateMeasurement(userId, BodyParts.BodyWeight, MeasurementUnits.Kilograms, 94.1, 5)
        };

        var service = CreateService(getByUser: (_, _, _) => Task.FromResult(measurements));

        var result = await service.GetMeasurementsTrendAsync(currentUser, userId, BodyParts.BodyWeight, MeasurementUnits.Kilograms);

        result.IsSuccess.Should().BeTrue();
        result.Value.Direction.Should().Be("up");
        result.Value.Difference.Should().Be(14.1);
        result.Value.FirstMeasurementValue.Should().Be(80);
        result.Value.LastMeasurementValue.Should().Be(94.1);
    }

    [Test]
    public async Task GetMeasurementsTrendAsync_WhenValueDrops_ReturnsDownDirectionAndAbsoluteDifference()
    {
        var userId = Id<User>.New();
        var currentUser = CreateUser(userId, "trend-down@example.com");
        var measurements = new List<Measurement>
        {
            CreateMeasurement(userId, BodyParts.Waist, MeasurementUnits.Centimeters, 90, 0),
            CreateMeasurement(userId, BodyParts.Waist, MeasurementUnits.Centimeters, 86.6, 7)
        };

        var service = CreateService(getByUser: (_, _, _) => Task.FromResult(measurements));

        var result = await service.GetMeasurementsTrendAsync(currentUser, userId, BodyParts.Waist, MeasurementUnits.Centimeters);

        result.IsSuccess.Should().BeTrue();
        result.Value.Direction.Should().Be("down");
        result.Value.Difference.Should().Be(3.4);
        result.Value.Change.Should().Be(-3.4);
    }

    [Test]
    public async Task GetMeasurementsTrendAsync_WhenValueStaysTheSame_ReturnsSameDirection()
    {
        var userId = Id<User>.New();
        var currentUser = CreateUser(userId, "trend-same@example.com");
        var measurements = new List<Measurement>
        {
            CreateMeasurement(userId, BodyParts.BodyFat, MeasurementUnits.Percent, 15, 0),
            CreateMeasurement(userId, BodyParts.BodyFat, MeasurementUnits.Percent, 15, 2)
        };

        var service = CreateService(getByUser: (_, _, _) => Task.FromResult(measurements));

        var result = await service.GetMeasurementsTrendAsync(currentUser, userId, BodyParts.BodyFat, MeasurementUnits.Percent);

        result.IsSuccess.Should().BeTrue();
        result.Value.Direction.Should().Be("same");
        result.Value.Difference.Should().Be(0);
    }

    [Test]
    public async Task GetMeasurementsTrendAsync_WhenOnlyOneMeasurementExists_ReturnsInsufficientData()
    {
        var userId = Id<User>.New();
        var currentUser = CreateUser(userId, "trend-one@example.com");
        var measurements = new List<Measurement>
        {
            CreateMeasurement(userId, BodyParts.Neck, MeasurementUnits.Centimeters, 42, 0)
        };

        var service = CreateService(getByUser: (_, _, _) => Task.FromResult(measurements));

        var result = await service.GetMeasurementsTrendAsync(currentUser, userId, BodyParts.Neck, MeasurementUnits.Centimeters);

        result.IsSuccess.Should().BeTrue();
        result.Value.Direction.Should().Be("insufficient_data");
        result.Value.Points.Should().Be(1);
    }

    [Test]
    public async Task GetMeasurementsTrendAsync_WhenNoMeasurementsExist_ReturnsInsufficientData()
    {
        var userId = Id<User>.New();
        var currentUser = CreateUser(userId, "trend-none@example.com");
        var service = CreateService(getByUser: (_, _, _) => Task.FromResult(new List<Measurement>()));

        var result = await service.GetMeasurementsTrendAsync(currentUser, userId, BodyParts.Bmi, MeasurementUnits.Bmi);

        result.IsSuccess.Should().BeTrue();
        result.Value.Direction.Should().Be("insufficient_data");
        result.Value.Points.Should().Be(0);
    }

    [Test]
    public async Task GetMeasurementsTrendsAsync_WhenMultipleMeasurementTypesExist_ReturnsTrendPerType()
    {
        var userId = Id<User>.New();
        var currentUser = CreateUser(userId, "trend-many@example.com");
        var measurements = new List<Measurement>
        {
            CreateMeasurement(userId, BodyParts.BodyWeight, MeasurementUnits.Kilograms, 80, 0),
            CreateMeasurement(userId, BodyParts.BodyWeight, MeasurementUnits.Kilograms, 82, 4),
            CreateMeasurement(userId, BodyParts.Waist, MeasurementUnits.Centimeters, 90, 1),
            CreateMeasurement(userId, BodyParts.Waist, MeasurementUnits.Centimeters, 88, 6),
            CreateMeasurement(userId, BodyParts.BodyFat, MeasurementUnits.Percent, 16, 3)
        };

        var service = CreateService(getByUser: (_, _, _) => Task.FromResult(measurements));

        var result = await service.GetMeasurementsTrendsAsync(currentUser, userId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(3);
        result.Value.Should().ContainSingle(x => x.BodyPart == BodyParts.BodyWeight && x.Direction == "up");
        result.Value.Should().ContainSingle(x => x.BodyPart == BodyParts.Waist && x.Direction == "down");
        result.Value.Should().ContainSingle(x => x.BodyPart == BodyParts.BodyFat && x.Direction == "insufficient_data");
    }

    [Test]
    public async Task AddMeasurementsAsync_WhenOwnerWritesValidBulkPayload_SavesAllMeasurements()
    {
        var userId = Id<User>.New();
        var currentUser = CreateUser(userId, "bulk-valid@example.com");
        var repository = new CapturingMeasurementRepository();
        var service = CreateService(repository: repository);

        var result = await service.AddMeasurementsAsync(currentUser,
        [
            new MeasurementCreateInput { BodyPart = BodyParts.BodyWeight, Unit = MeasurementUnits.Kilograms, Value = 80 },
            new MeasurementCreateInput { BodyPart = BodyParts.Waist, Unit = MeasurementUnits.Centimeters, Value = 90 }
        ]);

        result.IsSuccess.Should().BeTrue();
        repository.AddedMeasurements.Should().HaveCount(2);
    }

    private static User CreateUser(Id<User> userId, string email)
        => new() { Id = userId, Name = email.Split('@')[0], Email = email, ProfileRank = "Rookie" };

    private static Measurement CreateMeasurement(Id<User> userId, BodyParts bodyPart, MeasurementUnits unit, double value, int dayOffset)
        => new()
        {
            Id = Id<Measurement>.New(),
            UserId = userId,
            BodyPart = bodyPart,
            Unit = unit.ToString(),
            Value = value,
            CreatedAt = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero).AddDays(dayOffset)
        };

    private static MeasurementsService CreateService(
        Func<Id<Measurement>, CancellationToken, Task<Measurement?>>? findById = null,
        Func<Id<User>, BodyParts?, CancellationToken, Task<List<Measurement>>>? getByUser = null,
        Func<Id<User>, CancellationToken, Task<bool>>? isTrainer = null,
        Func<Id<User>, Id<User>, CancellationToken, Task<bool>>? hasActiveRelationship = null,
        CapturingMeasurementRepository? repository = null)
    {
        var measurementRepository = repository ?? new CapturingMeasurementRepository();
        measurementRepository.FindByIdHandler = findById ?? ((_, _) => Task.FromResult<Measurement?>(null));
        measurementRepository.GetByUserHandler = getByUser ?? ((_, _, _) => Task.FromResult(new List<Measurement>()));
        var heightConverter = new StubHeightUnitConverter();
        var weightConverter = new StubWeightUnitConverter();
        var unitOfWork = new StubUnitOfWork();
        var userAccess = Substitute.For<IUserAccessReadService>();
        userAccess.IsTrainerAsync(Arg.Any<Id<User>>(), Arg.Any<CancellationToken>())
            .Returns(call => (isTrainer ?? ((_, _) => Task.FromResult(false)))(call.Arg<Id<User>>(), call.Arg<CancellationToken>()));
        var relationshipAccess = Substitute.For<IMeasurementsRelationshipAccessPort>();
        relationshipAccess.HasActiveRelationshipAsync(Arg.Any<Id<User>>(), Arg.Any<Id<User>>(), Arg.Any<CancellationToken>())
            .Returns(call => (hasActiveRelationship ?? ((_, _, _) => Task.FromResult(false)))(
                call.ArgAt<Id<User>>(0),
                call.ArgAt<Id<User>>(1),
                call.ArgAt<CancellationToken>(2)));
        var progress = new WorkoutProgressReadWriteService(new WorkoutProgressReadWriteServiceDependencies(
            Substitute.For<IExerciseRepository>(),
            Substitute.For<IExerciseScoreRepository>(),
            measurementRepository,
            Substitute.For<IMainRecordRepository>(),
            Substitute.For<IEloRegistryRepository>(),
            userAccess,
            heightConverter,
            weightConverter,
            unitOfWork));
        return new MeasurementsService(progress, userAccess, relationshipAccess);
    }

    private sealed class CapturingMeasurementRepository : IMeasurementRepository
    {
        public List<Measurement> AddedMeasurements { get; } = new();
        public Func<Id<Measurement>, CancellationToken, Task<Measurement?>> FindByIdHandler { get; set; } = (_, _) => Task.FromResult<Measurement?>(null);
        public Func<Id<User>, BodyParts?, CancellationToken, Task<List<Measurement>>> GetByUserHandler { get; set; } = (_, _, _) => Task.FromResult(new List<Measurement>());

        public Task AddAsync(Measurement measurement, CancellationToken cancellationToken = default)
        {
            AddedMeasurements.Add(measurement);
            return Task.CompletedTask;
        }

        public Task<Measurement?> FindByIdAsync(Id<Measurement> id, CancellationToken cancellationToken = default)
            => FindByIdHandler(id, cancellationToken);

        public Task<List<Measurement>> GetByUserAsync(Id<User> userId, BodyParts? bodyPart, CancellationToken cancellationToken = default)
            => GetFilteredAsync(userId, bodyPart, cancellationToken);

        public async Task<HashSet<BodyParts>> GetExistingBodyPartsByUserAndCreatedAtRangeAsync(
            Id<User> userId,
            IReadOnlyCollection<BodyParts> bodyParts,
            DateTimeOffset createdAtFromUtc,
            DateTimeOffset createdAtToUtc,
            CancellationToken cancellationToken = default)
        {
            var items = await GetByUserHandler(userId, null, cancellationToken);

            return items
                .Where(item => bodyParts.Contains(item.BodyPart)
                               && item.CreatedAt >= createdAtFromUtc
                               && item.CreatedAt < createdAtToUtc)
                .Select(item => item.BodyPart)
                .ToHashSet();
        }

        private async Task<List<Measurement>> GetFilteredAsync(Id<User> userId, BodyParts? bodyPart, CancellationToken cancellationToken)
        {
            var items = await GetByUserHandler(userId, bodyPart, cancellationToken);
            return bodyPart.HasValue ? items.Where(item => item.BodyPart == bodyPart.Value).ToList() : items;
        }
    }

    private sealed class StubHeightUnitConverter : IUnitConverter<HeightUnits>
    {
        public double Convert(double value, HeightUnits fromUnit, HeightUnits toUnit)
        {
            if (fromUnit == toUnit)
            {
                return value;
            }

            return (fromUnit, toUnit) switch
            {
                (HeightUnits.Meters, HeightUnits.Centimeters) => value * 100d,
                (HeightUnits.Centimeters, HeightUnits.Meters) => value / 100d,
                (HeightUnits.Centimeters, HeightUnits.Millimeters) => value * 10d,
                (HeightUnits.Millimeters, HeightUnits.Centimeters) => value / 10d,
                _ => value
            };
        }
    }

    private sealed class StubWeightUnitConverter : IUnitConverter<WeightUnits>
    {
        public double Convert(double value, WeightUnits fromUnit, WeightUnits toUnit)
        {
            if (fromUnit == toUnit)
            {
                return value;
            }

            return (fromUnit, toUnit) switch
            {
                (WeightUnits.Kilograms, WeightUnits.Pounds) => value * 2.20462d,
                (WeightUnits.Pounds, WeightUnits.Kilograms) => value / 2.20462d,
                _ => value
            };
        }
    }

    private sealed class StubUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(1);

        public Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public void DetachEntity<TEntity>(TEntity entity) where TEntity : class => throw new NotSupportedException();
    }
}
