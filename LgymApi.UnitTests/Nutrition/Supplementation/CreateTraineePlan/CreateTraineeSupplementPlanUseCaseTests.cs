using FluentAssertions;
using LgymApi.Application.Coaching.Contracts.Access;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.Nutrition.Errors;
using LgymApi.Application.Mapping;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.Nutrition.Persistence;
using LgymApi.Application.Nutrition.Supplementation.CreateTraineePlan;
using LgymApi.Application.Nutrition.Supplementation.Models;
using LgymApi.Application.Repositories;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Resources;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;

namespace LgymApi.UnitTests.Nutrition.Supplementation.CreateTraineePlan;

[TestFixture]
public sealed class CreateTraineeSupplementPlanUseCaseTests
{
    [Test]
    public async Task ValidCreate_NormalizesStagesInactivePlanSavesAndMaps()
    {
        var trainerId = Id<User>.New();
        var traineeId = Id<User>.New();
        var cancellationToken = new CancellationTokenSource().Token;
        var dependencies = new Dependencies();
        SupplementPlan? stagedPlan = null;
        var operations = new List<string>();
        dependencies.GrantAccess(trainerId, traineeId, operations);
        dependencies.Plans.AddPlanAsync(Arg.Any<SupplementPlan>(), cancellationToken)
            .Returns(Task.CompletedTask)
            .AndDoes(call =>
            {
                stagedPlan = call.Arg<SupplementPlan>();
                operations.Add("stage");
            });
        dependencies.UnitOfWork.SaveChangesAsync(cancellationToken)
            .Returns(Task.FromResult(1))
            .AndDoes(_ => operations.Add("save"));

        var result = await dependencies.CreateUseCase().ExecuteAsync(
            new CreateTraineeSupplementPlanCommand(trainerId, traineeId, ValidData()),
            cancellationToken);

        result.IsSuccess.Should().BeTrue();
        stagedPlan.Should().NotBeNull();
        stagedPlan!.Id.IsEmpty.Should().BeFalse();
        stagedPlan.TrainerId.Should().Be(trainerId);
        stagedPlan.TraineeId.Should().Be(traineeId);
        stagedPlan.Name.Should().Be("Plan");
        stagedPlan.Notes.Should().Be("note");
        stagedPlan.IsActive.Should().BeFalse();
        stagedPlan.IsDeleted.Should().BeFalse();
        stagedPlan.Items.Select(item => item.SupplementName).Should().Equal("Morning", "Evening");
        stagedPlan.Items.Should().OnlyContain(item => !item.Id.IsEmpty && item.PlanId == stagedPlan.Id);
        result.Value.IsActive.Should().BeFalse();
        result.Value.Items.Select(item => item.SupplementName).Should().Equal("Morning", "Evening");
        operations.Should().Equal("access", "stage", "save");
        await dependencies.UnitOfWork.Received(1).SaveChangesAsync(cancellationToken);
    }

    [Test]
    public async Task InvalidBody_ReturnsSupplementationErrorAfterAccessWithoutWrites()
    {
        var trainerId = Id<User>.New();
        var traineeId = Id<User>.New();
        var dependencies = new Dependencies();
        dependencies.GrantAccess(trainerId, traineeId);

        var result = await dependencies.CreateUseCase().ExecuteAsync(
            new CreateTraineeSupplementPlanCommand(trainerId, traineeId, new SupplementPlanUpsertData(" ", null, [])));

        result.Error.Should().BeOfType<InvalidSupplementationError>();
        result.Error.Message.Should().Be(Messages.FieldRequired);
        await dependencies.Access.Received(1).GetAccessDecisionAsync(trainerId, traineeId, Arg.Any<CancellationToken>());
        await AssertNoWritesAsync(dependencies);
    }

    [Test]
    public async Task EmptyTrainee_ReturnsUserRequiredAfterCoachingWithoutWrites()
    {
        var trainerId = Id<User>.New();
        var dependencies = new Dependencies();
        dependencies.Access.GetAccessDecisionAsync(trainerId, Id<User>.Empty, Arg.Any<CancellationToken>())
            .Returns(new CoachingRelationshipAccessDecision(true, true));

        var result = await dependencies.CreateUseCase().ExecuteAsync(
            new CreateTraineeSupplementPlanCommand(trainerId, Id<User>.Empty, ValidData()));

        result.Error.Should().BeOfType<InvalidSupplementationError>();
        result.Error.Message.Should().Be(Messages.UserIdRequired);
        await dependencies.Access.Received(1).GetAccessDecisionAsync(trainerId, Id<User>.Empty, Arg.Any<CancellationToken>());
        await AssertNoWritesAsync(dependencies);
    }

    [Test]
    public async Task NonTrainer_ReturnsForbiddenWithoutWrites()
    {
        var trainerId = Id<User>.New();
        var traineeId = Id<User>.New();
        var dependencies = new Dependencies();
        dependencies.Access.GetAccessDecisionAsync(trainerId, traineeId, Arg.Any<CancellationToken>())
            .Returns(new CoachingRelationshipAccessDecision(false, true));

        var result = await dependencies.CreateUseCase().ExecuteAsync(
            new CreateTraineeSupplementPlanCommand(trainerId, traineeId, ValidData()));

        result.Error.Should().BeOfType<SupplementationForbiddenError>();
        result.Error.Message.Should().Be(Messages.TrainerRoleRequired);
        await AssertNoWritesAsync(dependencies);
    }

    [Test]
    public async Task MissingRelationship_ReturnsNotFoundWithoutWrites()
    {
        var trainerId = Id<User>.New();
        var traineeId = Id<User>.New();
        var dependencies = new Dependencies();
        dependencies.Access.GetAccessDecisionAsync(trainerId, traineeId, Arg.Any<CancellationToken>())
            .Returns(new CoachingRelationshipAccessDecision(true, false));

        var result = await dependencies.CreateUseCase().ExecuteAsync(
            new CreateTraineeSupplementPlanCommand(trainerId, traineeId, ValidData()));

        result.Error.Should().BeOfType<SupplementationNotFoundError>();
        result.Error.Message.Should().Be(Messages.DidntFind);
        await AssertNoWritesAsync(dependencies);
    }

    [Test]
    public async Task InvalidItemValidation_ReturnsErrorWithoutWrites()
    {
        var trainerId = Id<User>.New();
        var traineeId = Id<User>.New();
        var dependencies = new Dependencies();
        dependencies.GrantAccess(trainerId, traineeId);
        var invalidItem = new SupplementPlanItemInput("Vitamin", "dose", "25:00", 1, 1);

        var result = await dependencies.CreateUseCase().ExecuteAsync(
            new CreateTraineeSupplementPlanCommand(
                trainerId,
                traineeId,
                new SupplementPlanUpsertData("Plan", null, [invalidItem])));

        result.Error.Should().BeOfType<InvalidSupplementationError>();
        result.Error.Message.Should().Be(Messages.FieldRequired);
        await AssertNoWritesAsync(dependencies);
    }

    [Test]
    public async Task SaveFailure_PropagatesAfterSingleStageAndDoesNotMapFurther()
    {
        var trainerId = Id<User>.New();
        var traineeId = Id<User>.New();
        var cancellationToken = new CancellationTokenSource().Token;
        var dependencies = new Dependencies();
        dependencies.GrantAccess(trainerId, traineeId);
        dependencies.Plans.AddPlanAsync(Arg.Any<SupplementPlan>(), cancellationToken).Returns(Task.CompletedTask);
        dependencies.UnitOfWork.SaveChangesAsync(cancellationToken)
            .Returns(Task.FromException<int>(new InvalidOperationException("save failed")));

        Func<Task> act = () => dependencies.CreateUseCase().ExecuteAsync(
            new CreateTraineeSupplementPlanCommand(trainerId, traineeId, ValidData()),
            cancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("save failed");
        await dependencies.Plans.Received(1).AddPlanAsync(Arg.Any<SupplementPlan>(), cancellationToken);
        await dependencies.UnitOfWork.Received(1).SaveChangesAsync(cancellationToken);
        await dependencies.UnitOfWork.DidNotReceive().BeginTransactionAsync(Arg.Any<CancellationToken>());
    }

    private static SupplementPlanUpsertData ValidData()
        => new(
            " Plan ",
            " note ",
            [
                new SupplementPlanItemInput(" Evening ", " 2 pills ", "20:00", 127, 2),
                new SupplementPlanItemInput(" Morning ", " 1 pill ", "08:00", 127, 1)
            ]);

    private static async Task AssertNoWritesAsync(Dependencies dependencies)
    {
        await dependencies.Plans.DidNotReceive().AddPlanAsync(Arg.Any<SupplementPlan>(), Arg.Any<CancellationToken>());
        await dependencies.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await dependencies.UnitOfWork.DidNotReceive().BeginTransactionAsync(Arg.Any<CancellationToken>());
    }

    private sealed class Dependencies
    {
        public ICoachingRelationshipAccessService Access { get; } = Substitute.For<ICoachingRelationshipAccessService>();
        public ISupplementationPersistence Plans { get; } = Substitute.For<ISupplementationPersistence>();
        public IUnitOfWork UnitOfWork { get; } = Substitute.For<IUnitOfWork>();
        private IMapper Mapper { get; } = CreateMapper();

        public void GrantAccess(Id<User> trainerId, Id<User> traineeId, List<string>? operations = null)
            => Access.GetAccessDecisionAsync(trainerId, traineeId, Arg.Any<CancellationToken>())
                .Returns(new CoachingRelationshipAccessDecision(true, true))
                .AndDoes(_ => operations?.Add("access"));

        public ICreateTraineeSupplementPlanUseCase CreateUseCase()
            => new CreateTraineeSupplementPlanUseCase(Access, Plans, UnitOfWork, Mapper);

        private static IMapper CreateMapper()
        {
            var services = new ServiceCollection();
            services.AddApplicationMapping(typeof(IMappingProfile).Assembly);
            using var provider = services.BuildServiceProvider();
            return provider.GetRequiredService<IMapper>();
        }
    }
}
