using FluentAssertions;
using LgymApi.Application.Nutrition.Supplementation.CheckOffIntake;
using LgymApi.Application.Nutrition.Supplementation.CheckOffIntake.Models;
using LgymApi.Application.Repositories;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Data;
using LgymApi.Infrastructure.Repositories.Nutrition;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using UserEntity = LgymApi.Domain.Entities.User;

namespace LgymApi.UnitTests.Nutrition.Supplementation.CheckOffIntake;

[TestFixture]
public sealed class CheckOffSupplementIntakeTrackerRecoveryTests
{
    private const string IntakeLogUniqueIndexName = "IX_SupplementIntakeLogs_TraineeId_PlanItemId_IntakeDate";
    private static readonly DateOnly Monday = new(2026, 7, 27);

    [Test]
    public async Task ExecuteAsync_WhenWinnerIsReloaded_DetachesLoserBeforeALaterSave()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"supplement-intake-race-{Id<CheckOffSupplementIntakeTrackerRecoveryTests>.New():N}")
            .Options;
        var traineeId = Id<UserEntity>.New();
        var plan = CreatePlan(traineeId);
        var item = CreateItem(plan.Id);

        await using (var seedContext = new AppDbContext(options))
        {
            seedContext.SupplementPlans.Add(plan);
            seedContext.SupplementPlanItems.Add(item);
            await seedContext.SaveChangesAsync();
        }

        await using var losingContext = new AppDbContext(options);
        var winner = CreateLog(traineeId, item.Id);
        var unitOfWork = new WinnerThenSaveUnitOfWork(losingContext, options, winner);
        var useCase = new CheckOffSupplementIntakeUseCase(
            new SupplementationPersistenceRepository(losingContext),
            unitOfWork);

        var result = await useCase.ExecuteAsync(new CheckOffSupplementIntakeCommand(traineeId, item.Id, Monday, null));

        result.IsSuccess.Should().BeTrue();
        losingContext.ChangeTracker.Entries<SupplementIntakeLog>().Should().BeEmpty();
        await unitOfWork.SaveChangesAsync();

        await using var verificationContext = new AppDbContext(options);
        (await verificationContext.SupplementIntakeLogs.AsNoTracking().ToListAsync()).Should().ContainSingle();
        unitOfWork.SaveCalls.Should().Be(2);
    }

    private static SupplementPlan CreatePlan(Id<UserEntity> traineeId)
        => new()
        {
            Id = Id<SupplementPlan>.New(),
            TrainerId = traineeId,
            TraineeId = traineeId,
            Name = "Plan",
            IsActive = true
        };

    private static SupplementPlanItem CreateItem(Id<SupplementPlan> planId)
        => new()
        {
            Id = Id<SupplementPlanItem>.New(),
            PlanId = planId,
            SupplementName = "Magnesium",
            Dosage = "1 tablet",
            Order = 1,
            DaysOfWeekMask = DaysOfWeekSet.Monday,
            TimeOfDay = new TimeSpan(8, 0, 0)
        };

    private static SupplementIntakeLog CreateLog(Id<UserEntity> traineeId, Id<SupplementPlanItem> itemId)
        => new()
        {
            Id = Id<SupplementIntakeLog>.New(),
            TraineeId = traineeId,
            PlanItemId = itemId,
            IntakeDate = Monday,
            TakenAt = new DateTimeOffset(2026, 7, 27, 8, 0, 0, TimeSpan.Zero)
        };

    private sealed class WinnerThenSaveUnitOfWork : IUnitOfWork
    {
        private readonly AppDbContext _context;
        private readonly DbContextOptions<AppDbContext> _options;
        private readonly SupplementIntakeLog _winner;

        public WinnerThenSaveUnitOfWork(
            AppDbContext context,
            DbContextOptions<AppDbContext> options,
            SupplementIntakeLog winner)
        {
            _context = context;
            _options = options;
            _winner = winner;
        }

        public int SaveCalls { get; private set; }

        public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveCalls++;
            if (SaveCalls == 1)
            {
                await using var winnerContext = new AppDbContext(_options);
                winnerContext.SupplementIntakeLogs.Add(_winner);
                await winnerContext.SaveChangesAsync(cancellationToken);
                throw CreateUniqueViolation();
            }

            return await _context.SaveChangesAsync(cancellationToken);
        }

        public Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        private static DbUpdateException CreateUniqueViolation()
        {
            var postgresException = new PostgresException(
                "duplicate key value violates unique constraint",
                "ERROR",
                "ERROR",
                PostgresErrorCodes.UniqueViolation,
                constraintName: IntakeLogUniqueIndexName);
            return new DbUpdateException("duplicate intake log", postgresException);
        }
    }
}
