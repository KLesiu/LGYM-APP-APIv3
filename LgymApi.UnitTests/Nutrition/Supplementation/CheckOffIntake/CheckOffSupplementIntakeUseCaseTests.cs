using FluentAssertions;
using LgymApi.Application.Common.Errors;
using LgymApi.Application.Nutrition.Persistence;
using LgymApi.Application.Nutrition.Supplementation.CheckOffIntake;
using LgymApi.Application.Nutrition.Supplementation.CheckOffIntake.Contracts;
using LgymApi.Application.Nutrition.Supplementation.CheckOffIntake.Models;
using LgymApi.Application.Repositories;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using NSubstitute;
using UserEntity = LgymApi.Domain.Entities.User;

namespace LgymApi.UnitTests.Nutrition.Supplementation.CheckOffIntake;

[TestFixture]
public sealed class CheckOffSupplementIntakeUseCaseTests
{
    private static readonly DateOnly Monday = new(2026, 7, 27);

    [Test]
    public async Task ExecuteAsync_WhenNewLogHasNoTakenAt_UsesUtcNowAndForwardsCancellation()
    {
        var dependencies = new Dependencies();
        var item = dependencies.GrantActiveScheduledPlan();
        using var cancellationSource = new CancellationTokenSource();
        var before = DateTimeOffset.UtcNow;

        var result = await dependencies.CreateUseCase().ExecuteAsync(
            dependencies.Command(item.Id),
            cancellationSource.Token);
        var after = DateTimeOffset.UtcNow;

        result.IsSuccess.Should().BeTrue();
        result.Value.Taken.Should().BeTrue();
        result.Value.TakenAt.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
        await dependencies.Plans.Received(1).GetActivePlanForTraineeAsync(dependencies.TraineeId, cancellationSource.Token);
        await dependencies.Plans.Received(1).FindTrackedIntakeLogAsync(dependencies.TraineeId, item.Id, Monday, cancellationSource.Token);
        await dependencies.Plans.Received(1).AddIntakeLogAsync(Arg.Is<SupplementIntakeLog>(log =>
            log.TraineeId == dependencies.TraineeId && log.PlanItemId == item.Id && log.IntakeDate == Monday), cancellationSource.Token);
        await dependencies.UnitOfWork.Received(1).SaveChangesAsync(cancellationSource.Token);
    }

    [Test]
    public async Task ExecuteAsync_WhenNewLogHasExplicitTakenAt_PersistsAndReturnsIt()
    {
        var dependencies = new Dependencies();
        var item = dependencies.GrantActiveScheduledPlan();
        var takenAt = new DateTimeOffset(2026, 7, 27, 8, 30, 0, TimeSpan.Zero);

        var result = await dependencies.CreateUseCase().ExecuteAsync(dependencies.Command(item.Id, takenAt));

        result.IsSuccess.Should().BeTrue();
        result.Value.TakenAt.Should().Be(takenAt);
        await dependencies.Plans.Received(1).AddIntakeLogAsync(Arg.Is<SupplementIntakeLog>(log => log.TakenAt == takenAt), Arg.Any<CancellationToken>());
        await dependencies.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ExecuteAsync_WhenExistingLogHasNoTakenAt_RetainsItsTimestamp()
    {
        var dependencies = new Dependencies();
        var item = dependencies.GrantActiveScheduledPlan();
        var existing = dependencies.ExistingLog(item.Id, new DateTimeOffset(2026, 7, 27, 8, 0, 0, TimeSpan.Zero));
        dependencies.Plans.FindTrackedIntakeLogAsync(dependencies.TraineeId, item.Id, Monday, Arg.Any<CancellationToken>()).Returns(existing);

        var result = await dependencies.CreateUseCase().ExecuteAsync(dependencies.Command(item.Id));

        result.IsSuccess.Should().BeTrue();
        result.Value.TakenAt.Should().Be(existing.TakenAt);
        await dependencies.Plans.DidNotReceive().AddIntakeLogAsync(Arg.Any<SupplementIntakeLog>(), Arg.Any<CancellationToken>());
        await dependencies.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ExecuteAsync_WhenExistingLogHasExplicitTakenAt_ReplacesItsTimestamp()
    {
        var dependencies = new Dependencies();
        var item = dependencies.GrantActiveScheduledPlan();
        var existing = dependencies.ExistingLog(item.Id, new DateTimeOffset(2026, 7, 27, 8, 0, 0, TimeSpan.Zero));
        var replacement = new DateTimeOffset(2026, 7, 27, 9, 0, 0, TimeSpan.Zero);
        dependencies.Plans.FindTrackedIntakeLogAsync(dependencies.TraineeId, item.Id, Monday, Arg.Any<CancellationToken>()).Returns(existing);

        var result = await dependencies.CreateUseCase().ExecuteAsync(dependencies.Command(item.Id, replacement));

        result.IsSuccess.Should().BeTrue();
        existing.TakenAt.Should().Be(replacement);
        result.Value.TakenAt.Should().Be(replacement);
        await dependencies.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ExecuteAsync_WhenPlanItemIdIsEmpty_ReturnsInvalidWithoutPersistence()
    {
        var dependencies = new Dependencies();

        var result = await dependencies.CreateUseCase().ExecuteAsync(dependencies.Command(Id<SupplementPlanItem>.Empty));

        result.Error.Should().BeOfType<InvalidSupplementationError>();
        await AssertNoPersistenceAsync(dependencies);
    }

    [Test]
    public async Task ExecuteAsync_WhenIntakeDateIsDefault_ReturnsInvalidWithoutPersistence()
    {
        var dependencies = new Dependencies();

        var result = await dependencies.CreateUseCase().ExecuteAsync(
            new CheckOffSupplementIntakeCommand(dependencies.TraineeId, Id<SupplementPlanItem>.New(), default, null));

        result.Error.Should().BeOfType<InvalidSupplementationError>();
        await AssertNoPersistenceAsync(dependencies);
    }

    [Test]
    public async Task ExecuteAsync_WhenThereIsNoActivePlan_ReturnsNotFoundWithoutMutation()
    {
        var dependencies = new Dependencies();
        var itemId = Id<SupplementPlanItem>.New();
        dependencies.Plans.GetActivePlanForTraineeAsync(dependencies.TraineeId, Arg.Any<CancellationToken>())
            .Returns((SupplementPlan?)null);

        var result = await dependencies.CreateUseCase().ExecuteAsync(dependencies.Command(itemId));

        result.Error.Should().BeOfType<SupplementationNotFoundError>();
        await dependencies.Plans.Received(1).GetActivePlanForTraineeAsync(dependencies.TraineeId, Arg.Any<CancellationToken>());
        await AssertNoSaveAsync(dependencies);
    }

    [Test]
    public async Task ExecuteAsync_WhenPlanItemIsMissingOrUnscheduled_ReturnsNotFoundWithoutMutation()
    {
        var missing = new Dependencies();
        missing.GrantActiveScheduledPlan();

        var missingResult = await missing.CreateUseCase().ExecuteAsync(missing.Command(Id<SupplementPlanItem>.New()));

        missingResult.Error.Should().BeOfType<SupplementationNotFoundError>();
        await AssertNoTrackedMutationAsync(missing);

        var unscheduled = new Dependencies();
        var unscheduledItem = unscheduled.GrantActiveScheduledPlan(daysOfWeekMask: 2);

        var unscheduledResult = await unscheduled.CreateUseCase().ExecuteAsync(unscheduled.Command(unscheduledItem.Id));

        unscheduledResult.Error.Should().BeOfType<SupplementationNotFoundError>();
        await AssertNoTrackedMutationAsync(unscheduled);
    }

    private static async Task AssertNoPersistenceAsync(Dependencies dependencies)
    {
        dependencies.Plans.ReceivedCalls().Should().BeEmpty();
        await AssertNoSaveAsync(dependencies);
    }

    private static async Task AssertNoTrackedMutationAsync(Dependencies dependencies)
    {
        await dependencies.Plans.DidNotReceive().FindTrackedIntakeLogAsync(
            Arg.Any<Id<UserEntity>>(),
            Arg.Any<Id<SupplementPlanItem>>(),
            Arg.Any<DateOnly>(),
            Arg.Any<CancellationToken>());
        await dependencies.Plans.DidNotReceive().AddIntakeLogAsync(Arg.Any<SupplementIntakeLog>(), Arg.Any<CancellationToken>());
        await AssertNoSaveAsync(dependencies);
    }

    private static async Task AssertNoSaveAsync(Dependencies dependencies)
    {
        await dependencies.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await dependencies.UnitOfWork.DidNotReceive().BeginTransactionAsync(Arg.Any<CancellationToken>());
    }

    private sealed class Dependencies
    {
        public Id<UserEntity> TraineeId { get; } = Id<UserEntity>.New();
        public ISupplementationPersistence Plans { get; } = Substitute.For<ISupplementationPersistence>();
        public IUnitOfWork UnitOfWork { get; } = Substitute.For<IUnitOfWork>();

        public SupplementPlanItem GrantActiveScheduledPlan(int daysOfWeekMask = 1)
        {
            var plan = new SupplementPlan
            {
                Id = Id<SupplementPlan>.New(),
                TraineeId = TraineeId,
                IsActive = true
            };
            var item = new SupplementPlanItem
            {
                Id = Id<SupplementPlanItem>.New(),
                PlanId = plan.Id,
                SupplementName = "Magnesium",
                Dosage = "1 tablet",
                Order = 1,
                DaysOfWeekMask = (DaysOfWeekSet)daysOfWeekMask,
                TimeOfDay = new TimeSpan(8, 0, 0)
            };
            plan.Items.Add(item);
            Plans.GetActivePlanForTraineeAsync(TraineeId, Arg.Any<CancellationToken>()).Returns(plan);
            return item;
        }

        public SupplementIntakeLog ExistingLog(Id<SupplementPlanItem> planItemId, DateTimeOffset takenAt)
            => new()
            {
                Id = Id<SupplementIntakeLog>.New(),
                TraineeId = TraineeId,
                PlanItemId = planItemId,
                IntakeDate = Monday,
                TakenAt = takenAt
            };

        public CheckOffSupplementIntakeCommand Command(Id<SupplementPlanItem> planItemId, DateTimeOffset? takenAt = null)
            => new(TraineeId, planItemId, Monday, takenAt);

        public ICheckOffSupplementIntakeUseCase CreateUseCase()
            => new CheckOffSupplementIntakeUseCase(Plans, UnitOfWork);
    }
}
