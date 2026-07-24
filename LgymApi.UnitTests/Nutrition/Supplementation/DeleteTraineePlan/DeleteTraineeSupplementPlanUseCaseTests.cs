using FluentAssertions;
using LgymApi.Application.Coaching.Contracts.Access;
using LgymApi.Application.Common.Errors;
using LgymApi.Application.Common.Results;
using LgymApi.Application.Nutrition.Supplementation.DeleteTraineePlan;
using LgymApi.Application.Nutrition.Supplementation.DeleteTraineePlan.Models;
using LgymApi.Application.Nutrition.Persistence;
using LgymApi.Application.Repositories;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Data;
using LgymApi.Infrastructure.Repositories.Nutrition;
using LgymApi.Resources;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using NUnit.Framework;

namespace LgymApi.UnitTests.Nutrition.Supplementation.DeleteTraineePlan;

[TestFixture]
public sealed class DeleteTraineeSupplementPlanUseCaseTests
{
    [Test]
    public async Task ExecuteAsync_WhenPlanIsOwned_SoftDeletesOnceAndPreservesItemsAndFilterBehavior()
    {
        var databaseName = $"supplement-plan-delete-{Id<DeleteTraineeSupplementPlanUseCaseTests>.New():N}";
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(databaseName).Options;
        var trainerId = Id<User>.New();
        var traineeId = Id<User>.New();
        var plan = CreatePlan(trainerId, traineeId, isActive: true);
        var item = CreateItem(plan);
        plan.Items.Add(item);
        var unrelatedPlan = CreatePlan(trainerId, traineeId, isActive: true);

        await using (var seedContext = new AppDbContext(options))
        {
            seedContext.SupplementPlans.AddRange(plan, unrelatedPlan);
            await seedContext.SaveChangesAsync();
        }

        var access = Substitute.For<ICoachingRelationshipAccessService>();
        access.GetAccessDecisionAsync(trainerId, traineeId, Arg.Any<CancellationToken>())
            .Returns(new CoachingRelationshipAccessDecision(true, true));
        CountingUnitOfWork unitOfWork;
        await using (var deleteContext = new AppDbContext(options))
        {
            unitOfWork = new CountingUnitOfWork(deleteContext);
            var useCase = new DeleteTraineeSupplementPlanUseCase(
                access,
                new SupplementationPersistenceRepository(deleteContext),
                unitOfWork);

            var result = await useCase.ExecuteAsync(new(trainerId, traineeId, plan.Id));

            result.IsSuccess.Should().BeTrue();
            result.Value.Should().Be(Unit.Value);
        }
        unitOfWork.SaveCount.Should().Be(1);

        await using var readContext = new AppDbContext(options);
        var repository = new SupplementationPersistenceRepository(readContext);
        (await repository.GetActivePlanForTraineeAsync(traineeId))!.Id.Should().Be(unrelatedPlan.Id);
        var allRows = await readContext.SupplementPlans
            .IgnoreQueryFilters()
            .Include(row => row.Items)
            .AsNoTracking()
            .ToListAsync();
        var deletedRow = allRows.Single(row => row.Id == plan.Id);
        deletedRow.IsDeleted.Should().BeTrue();
        deletedRow.IsActive.Should().BeFalse();
        deletedRow.Items.Should().ContainSingle();
        deletedRow.Items.Single().SupplementName.Should().Be("Vitamin D");
        allRows.Single(row => row.Id == unrelatedPlan.Id).IsDeleted.Should().BeFalse();
        await access.Received(1).GetAccessDecisionAsync(trainerId, traineeId, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ExecuteAsync_WhenAccessOrTraineeIsInvalid_PreservesSupplementErrorOrderWithoutWrites()
    {
        var forbidden = new Dependencies();
        forbidden.Access.GetAccessDecisionAsync(forbidden.TrainerId, Id<User>.Empty, Arg.Any<CancellationToken>())
            .Returns(new CoachingRelationshipAccessDecision(false, true));
        var forbiddenResult = await forbidden.CreateUseCase().ExecuteAsync(
            new(forbidden.TrainerId, Id<User>.Empty, Id<SupplementPlan>.New()));

        forbiddenResult.Error.Should().BeOfType<SupplementationForbiddenError>().Which.Message.Should().Be(Messages.TrainerRoleRequired);
        await forbidden.Access.Received(1).GetAccessDecisionAsync(forbidden.TrainerId, Id<User>.Empty, Arg.Any<CancellationToken>());
        await AssertNoWritesAsync(forbidden);

        var emptyTrainee = new Dependencies();
        emptyTrainee.Access.GetAccessDecisionAsync(emptyTrainee.TrainerId, Id<User>.Empty, Arg.Any<CancellationToken>())
            .Returns(new CoachingRelationshipAccessDecision(true, true));
        var emptyResult = await emptyTrainee.CreateUseCase().ExecuteAsync(
            new(emptyTrainee.TrainerId, Id<User>.Empty, Id<SupplementPlan>.New()));

        emptyResult.Error.Should().BeOfType<InvalidSupplementationError>().Which.Message.Should().Be(Messages.UserIdRequired);
        await emptyTrainee.Access.Received(1).GetAccessDecisionAsync(emptyTrainee.TrainerId, Id<User>.Empty, Arg.Any<CancellationToken>());
        await AssertNoWritesAsync(emptyTrainee);
    }

    [Test]
    public async Task ExecuteAsync_WhenTrainerHasNoActiveLink_ReturnsNotFoundBeforePlanRead()
    {
        var dependencies = new Dependencies();
        dependencies.Access.GetAccessDecisionAsync(dependencies.TrainerId, dependencies.TraineeId, Arg.Any<CancellationToken>())
            .Returns(new CoachingRelationshipAccessDecision(true, false));

        var result = await dependencies.CreateUseCase().ExecuteAsync(dependencies.Command(Id<SupplementPlan>.New()));

        result.Error.Should().BeOfType<SupplementationNotFoundError>().Which.Message.Should().Be(Messages.DidntFind);
        await dependencies.Plans.DidNotReceive().FindTrackedPlanByIdAsync(Arg.Any<Id<SupplementPlan>>(), Arg.Any<CancellationToken>());
        await AssertNoWritesAsync(dependencies);
    }

    [Test]
    public async Task ExecuteAsync_WhenPlanIdentifierIsEmpty_ReturnsBadRequestAfterAccess()
    {
        var dependencies = new Dependencies();
        dependencies.GrantAccess();

        var result = await dependencies.CreateUseCase().ExecuteAsync(dependencies.Command(Id<SupplementPlan>.Empty));

        result.Error.Should().BeOfType<InvalidSupplementationError>().Which.Message.Should().Be(Messages.FieldRequired);
        await dependencies.Access.Received(1).GetAccessDecisionAsync(dependencies.TrainerId, dependencies.TraineeId, Arg.Any<CancellationToken>());
        await dependencies.Plans.DidNotReceive().FindTrackedPlanByIdAsync(Arg.Any<Id<SupplementPlan>>(), Arg.Any<CancellationToken>());
        await AssertNoWritesAsync(dependencies);
    }

    [Test]
    public async Task ExecuteAsync_WhenPlanIsMissingForeignOrDeleted_ReturnsNotFoundWithoutWrites()
    {
        var missing = new Dependencies();
        missing.GrantAccess();
        missing.Plans.FindTrackedPlanByIdAsync(Arg.Any<Id<SupplementPlan>>(), Arg.Any<CancellationToken>()).Returns((SupplementPlan?)null);
        var missingResult = await missing.CreateUseCase().ExecuteAsync(missing.Command(Id<SupplementPlan>.New()));
        missingResult.Error.Should().BeOfType<SupplementationNotFoundError>();
        await AssertNoWritesAsync(missing);

        var foreign = new Dependencies();
        foreign.GrantAccess();
        foreign.Plans.FindTrackedPlanByIdAsync(Arg.Any<Id<SupplementPlan>>(), Arg.Any<CancellationToken>())
            .Returns(CreatePlan(Id<User>.New(), foreign.TraineeId));
        var foreignResult = await foreign.CreateUseCase().ExecuteAsync(foreign.Command(Id<SupplementPlan>.New()));
        foreignResult.Error.Should().BeOfType<SupplementationNotFoundError>();
        await AssertNoWritesAsync(foreign);

        var deleted = new Dependencies();
        deleted.GrantAccess();
        deleted.Plans.FindTrackedPlanByIdAsync(Arg.Any<Id<SupplementPlan>>(), Arg.Any<CancellationToken>())
            .Returns(CreatePlan(deleted.TrainerId, deleted.TraineeId, isDeleted: true));
        var deletedResult = await deleted.CreateUseCase().ExecuteAsync(deleted.Command(Id<SupplementPlan>.New()));
        deletedResult.Error.Should().BeOfType<SupplementationNotFoundError>();
        await AssertNoWritesAsync(deleted);
    }

    private static SupplementPlan CreatePlan(Id<User> trainerId, Id<User> traineeId, bool isActive = false, bool isDeleted = false)
        => new()
        {
            Id = Id<SupplementPlan>.New(),
            TrainerId = trainerId,
            TraineeId = traineeId,
            Name = "Supplement plan",
            IsActive = isActive,
            IsDeleted = isDeleted
        };

    private static SupplementPlanItem CreateItem(SupplementPlan plan)
        => new()
        {
            Id = Id<SupplementPlanItem>.New(),
            PlanId = plan.Id,
            SupplementName = "Vitamin D",
            Dosage = "1 capsule",
            TimeOfDay = new TimeSpan(8, 0, 0),
            DaysOfWeekMask = DaysOfWeekSet.Monday,
            Order = 1
        };

    private static async Task AssertNoWritesAsync(Dependencies dependencies)
    {
        await dependencies.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    private sealed class Dependencies
    {
        public Id<User> TrainerId { get; } = Id<User>.New();
        public Id<User> TraineeId { get; } = Id<User>.New();
        public ICoachingRelationshipAccessService Access { get; } = Substitute.For<ICoachingRelationshipAccessService>();
        public ISupplementationPersistence Plans { get; } = Substitute.For<ISupplementationPersistence>();
        public IUnitOfWork UnitOfWork { get; } = Substitute.For<IUnitOfWork>();

        public void GrantAccess()
            => Access.GetAccessDecisionAsync(TrainerId, TraineeId, Arg.Any<CancellationToken>())
                .Returns(new CoachingRelationshipAccessDecision(true, true));

        public DeleteTraineeSupplementPlanCommand Command(Id<SupplementPlan> planId)
            => new(TrainerId, TraineeId, planId);

        public DeleteTraineeSupplementPlanUseCase CreateUseCase()
            => new(Access, Plans, UnitOfWork);
    }

    private sealed class CountingUnitOfWork : IUnitOfWork
    {
        private readonly AppDbContext _dbContext;

        public CountingUnitOfWork(AppDbContext deleteContext)
        {
            _dbContext = deleteContext;
        }

        public int SaveCount { get; private set; }

        public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveCount++;
            return await _dbContext.SaveChangesAsync(cancellationToken);
        }

        public Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
