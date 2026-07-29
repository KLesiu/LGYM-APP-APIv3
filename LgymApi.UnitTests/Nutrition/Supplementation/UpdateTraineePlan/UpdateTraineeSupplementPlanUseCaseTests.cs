using FluentAssertions;
using LgymApi.Application.Coaching.Contracts.Access;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.Nutrition.Errors;
using LgymApi.Application.Mapping;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.Nutrition.Supplementation.Models;
using LgymApi.Application.Nutrition.Supplementation.UpdateTraineePlan;
using LgymApi.Application.Nutrition.Persistence;
using LgymApi.Application.Repositories;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Data;
using LgymApi.Infrastructure.Repositories.Nutrition;
using LgymApi.Resources;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;

namespace LgymApi.UnitTests.Nutrition.Supplementation.UpdateTraineePlan;

[TestFixture]
public sealed class UpdateTraineeSupplementPlanUseCaseTests
{
    [Test]
    public async Task ExecuteAsync_WhenActive_ReplacesTrackedPlanAndSavesOnceInOrder()
    {
        var dependencies = new Dependencies();
        var oldPlan = ExistingPlan(dependencies.TrainerId, dependencies.TraineeId, isActive: true);
        var oldItemId = oldPlan.Items.Single().Id;
        dependencies.GrantAccess();
        dependencies.Plans.FindTrackedPlanByIdAsync(oldPlan.Id, Arg.Any<CancellationToken>())
            .Returns(oldPlan);
        var operations = new List<string>();
        dependencies.Access.GetAccessDecisionAsync(dependencies.TrainerId, dependencies.TraineeId, Arg.Any<CancellationToken>())
            .Returns(new CoachingRelationshipAccessDecision(true, true))
            .AndDoes(_ => operations.Add("access"));
        dependencies.Plans.FindTrackedPlanByIdAsync(oldPlan.Id, Arg.Any<CancellationToken>())
            .Returns(oldPlan)
            .AndDoes(_ => operations.Add("tracked"));
        dependencies.Plans.AddPlanAsync(Arg.Any<SupplementPlan>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask)
            .AndDoes(_ => operations.Add("add"));
        dependencies.UnitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(1)
            .AndDoes(_ => operations.Add("save"));

        var result = await dependencies.CreateUseCase().ExecuteAsync(dependencies.Command(oldPlan.Id));

        result.IsSuccess.Should().BeTrue();
        oldPlan.IsDeleted.Should().BeTrue();
        oldPlan.IsActive.Should().BeFalse();
        var replacement = result.Value;
        replacement.Name.Should().Be("Updated plan");
        replacement.IsActive.Should().BeTrue();
        replacement.Id.Should().NotBe(oldPlan.Id);
        replacement.Items.Should().ContainSingle();
        replacement.Items.Single().Id.Should().NotBe(oldItemId);
        operations.Should().Equal("access", "tracked", "add", "save");
        await dependencies.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ExecuteAsync_WhenInactive_PreservesInactiveStateAndNormalizesReplacementItems()
    {
        var dependencies = new Dependencies();
        var oldPlan = ExistingPlan(dependencies.TrainerId, dependencies.TraineeId, isActive: false);
        dependencies.GrantAccess();
        dependencies.Plans.FindTrackedPlanByIdAsync(oldPlan.Id, Arg.Any<CancellationToken>()).Returns(oldPlan);

        var result = await dependencies.CreateUseCase().ExecuteAsync(
            dependencies.Command(oldPlan.Id, data: new SupplementPlanUpsertData("Updated plan", null, [
                new SupplementPlanItemInput(" Zinc ", " 20 mg ", "09:30", 127, 2),
                new SupplementPlanItemInput(" Magnesium ", "400 mg", "08:00", 127, 1)])));

        result.Value.IsActive.Should().BeFalse();
        result.Value.Items.Select(item => item.SupplementName).Should().Equal("Magnesium", "Zinc");
        result.Value.Items.Select(item => item.Order).Should().Equal(1, 2);
        oldPlan.IsDeleted.Should().BeTrue();
        await dependencies.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ExecuteAsync_WhenAccessIsForbiddenOrTraineeIsEmpty_PreservesSupplementErrorOrder()
    {
        var forbidden = new Dependencies();
        forbidden.Access.GetAccessDecisionAsync(forbidden.TrainerId, Id<User>.Empty, Arg.Any<CancellationToken>())
            .Returns(new CoachingRelationshipAccessDecision(false, true));
        var forbiddenResult = await forbidden.CreateUseCase().ExecuteAsync(
            forbidden.Command(Id<SupplementPlan>.New(), Id<User>.Empty));
        forbiddenResult.Error.Should().BeOfType<SupplementationForbiddenError>();
        await AssertNoWritesAsync(forbidden);

        var empty = new Dependencies();
        empty.Access.GetAccessDecisionAsync(empty.TrainerId, Id<User>.Empty, Arg.Any<CancellationToken>())
            .Returns(new CoachingRelationshipAccessDecision(true, true));
        var emptyResult = await empty.CreateUseCase().ExecuteAsync(
            empty.Command(Id<SupplementPlan>.New(), Id<User>.Empty));
        emptyResult.Error.Should().BeOfType<InvalidSupplementationError>();
        emptyResult.Error.Message.Should().Be(Messages.UserIdRequired);
        await AssertNoWritesAsync(empty);
    }

    [Test]
    public async Task ExecuteAsync_WhenBodyIsInvalid_ReturnsInvalidAfterAccessWithoutPlanRead()
    {
        var dependencies = new Dependencies();
        dependencies.GrantAccess();

        var result = await dependencies.CreateUseCase().ExecuteAsync(
            dependencies.Command(Id<SupplementPlan>.New(), data: new SupplementPlanUpsertData(" ", null, [])));

        result.Error.Should().BeOfType<InvalidSupplementationError>();
        await dependencies.Access.Received(1).GetAccessDecisionAsync(
            dependencies.TrainerId, dependencies.TraineeId, Arg.Any<CancellationToken>());
        await dependencies.Plans.DidNotReceive().FindTrackedPlanByIdAsync(
            Arg.Any<Id<SupplementPlan>>(), Arg.Any<CancellationToken>());
        await AssertNoWritesAsync(dependencies);
    }

    [Test]
    public async Task ExecuteAsync_WhenPlanIsMissingForeignOrDeleted_ReturnsNotFoundWithoutWrites()
    {
        var missing = new Dependencies();
        missing.GrantAccess();
        missing.Plans.FindTrackedPlanByIdAsync(Arg.Any<Id<SupplementPlan>>(), Arg.Any<CancellationToken>())
            .Returns((SupplementPlan?)null);
        (await missing.CreateUseCase().ExecuteAsync(missing.Command(Id<SupplementPlan>.New())))
            .Error.Should().BeOfType<SupplementationNotFoundError>();
        await AssertNoWritesAsync(missing);

        var foreign = new Dependencies();
        foreign.GrantAccess();
        foreign.Plans.FindTrackedPlanByIdAsync(Arg.Any<Id<SupplementPlan>>(), Arg.Any<CancellationToken>())
            .Returns(ExistingPlan(Id<User>.New(), foreign.TraineeId));
        (await foreign.CreateUseCase().ExecuteAsync(foreign.Command(Id<SupplementPlan>.New())))
            .Error.Should().BeOfType<SupplementationNotFoundError>();
        await AssertNoWritesAsync(foreign);

        var deleted = new Dependencies();
        deleted.GrantAccess();
        deleted.Plans.FindTrackedPlanByIdAsync(Arg.Any<Id<SupplementPlan>>(), Arg.Any<CancellationToken>())
            .Returns(ExistingPlan(deleted.TrainerId, deleted.TraineeId, isDeleted: true));
        (await deleted.CreateUseCase().ExecuteAsync(deleted.Command(Id<SupplementPlan>.New())))
            .Error.Should().BeOfType<SupplementationNotFoundError>();
        await AssertNoWritesAsync(deleted);
    }

    [Test]
    public async Task ExecuteAsync_WhenPlanIdIsEmpty_ReturnsInvalidAfterAccessWithoutWrites()
    {
        var dependencies = new Dependencies();
        dependencies.GrantAccess();

        var result = await dependencies.CreateUseCase().ExecuteAsync(
            dependencies.Command(Id<SupplementPlan>.Empty));

        result.Error.Should().BeOfType<InvalidSupplementationError>();
        result.Error.Message.Should().Be(Messages.FieldRequired);
        await dependencies.Access.Received(1).GetAccessDecisionAsync(
            dependencies.TrainerId, dependencies.TraineeId, Arg.Any<CancellationToken>());
        await dependencies.Plans.DidNotReceive().FindTrackedPlanByIdAsync(
            Arg.Any<Id<SupplementPlan>>(), Arg.Any<CancellationToken>());
        await AssertNoWritesAsync(dependencies);
    }

    [Test]
    public async Task ExecuteAsync_WhenSaveFails_LeavesFreshContextWithoutOldOrReplacementRows()
    {
        var databaseName = $"supplement-update-{Id<UpdateTraineeSupplementPlanUseCaseTests>.New():N}";
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(databaseName).Options;
        var trainerId = Id<User>.New();
        var traineeId = Id<User>.New();
        var oldPlan = ExistingPlan(trainerId, traineeId, isActive: true);
        await using (var seedContext = new AppDbContext(options))
        {
            seedContext.SupplementPlans.Add(oldPlan);
            await seedContext.SaveChangesAsync();
        }

        var access = Substitute.For<ICoachingRelationshipAccessService>();
        access.GetAccessDecisionAsync(trainerId, traineeId, Arg.Any<CancellationToken>())
            .Returns(new CoachingRelationshipAccessDecision(true, true));
        await using (var writeContext = new AppDbContext(options))
        {
            var useCase = new UpdateTraineeSupplementPlanUseCase(
                access,
                new SupplementationPersistenceRepository(writeContext),
                new ThrowingUnitOfWork(),
                CreateMapper());

            await FluentActions.Awaiting(() => useCase.ExecuteAsync(new(
                trainerId,
                traineeId,
                oldPlan.Id,
                ValidData())))
                .Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("save failed");
        }

        await using var readContext = new AppDbContext(options);
        var rows = await readContext.SupplementPlans.IgnoreQueryFilters().AsNoTracking().ToListAsync();
        rows.Should().ContainSingle(row => row.Id == oldPlan.Id && !row.IsDeleted && row.IsActive);
    }

    private static SupplementPlanUpsertData ValidData(
        IReadOnlyList<SupplementPlanItemInput>? items = null)
        => new(" Updated plan ", " notes ", items ?? [new SupplementPlanItemInput(" Vitamin D ", " 1 capsule ", "08:00", 127, 1)]);

    private static SupplementPlan ExistingPlan(
        Id<User> trainerId,
        Id<User> traineeId,
        bool isActive = false,
        bool isDeleted = false)
    {
        var plan = new SupplementPlan
        {
            Id = Id<SupplementPlan>.New(),
            TrainerId = trainerId,
            TraineeId = traineeId,
            Name = "Old plan",
            IsActive = isActive,
            IsDeleted = isDeleted
        };
        plan.Items.Add(new SupplementPlanItem
        {
            Id = Id<SupplementPlanItem>.New(),
            PlanId = plan.Id,
            SupplementName = "Old item",
            Dosage = "1",
            TimeOfDay = new TimeSpan(8, 0, 0),
            DaysOfWeekMask = DaysOfWeekSet.EveryDay,
            Order = 1
        });
        return plan;
    }

    private static async Task AssertNoWritesAsync(Dependencies dependencies)
    {
        await dependencies.Plans.DidNotReceive().AddPlanAsync(Arg.Any<SupplementPlan>(), Arg.Any<CancellationToken>());
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

        public UpdateTraineeSupplementPlanCommand Command(
            Id<SupplementPlan> planId,
            Id<User>? traineeId = null,
            SupplementPlanUpsertData? data = null)
            => new(TrainerId, traineeId ?? TraineeId, planId, data ?? ValidData());

        public UpdateTraineeSupplementPlanUseCase CreateUseCase()
            => new(Access, Plans, UnitOfWork, CreateMapper());
    }

    private sealed class ThrowingUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
            => Task.FromException<int>(new InvalidOperationException("save failed"));

        public Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private static IMapper CreateMapper()
    {
        var services = new ServiceCollection();
        services.AddApplicationMapping(LgymApi.Api.Mapping.MappingAssemblyMarkers.All);
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IMapper>();
    }
}
