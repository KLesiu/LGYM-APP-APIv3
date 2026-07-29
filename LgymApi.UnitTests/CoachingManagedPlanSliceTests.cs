using FluentAssertions;
using LgymApi.Application.Coaching;
using LgymApi.Application.Coaching.Contracts.Access;
using LgymApi.Application.Coaching.ManagedPlans.Assign;
using LgymApi.Application.Coaching.ManagedPlans.Create;
using LgymApi.Application.Coaching.ManagedPlans.Delete;
using LgymApi.Application.Coaching.ManagedPlans.GetActive;
using LgymApi.Application.Coaching.ManagedPlans.List;
using LgymApi.Application.Coaching.ManagedPlans.Unassign;
using LgymApi.Application.Coaching.ManagedPlans.Update;
using LgymApi.Application.Coaching.Persistence;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.Coaching.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Mapping;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.TrainingPlanning.Contracts.ManagedPlans;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using LgymApi.TrainingPlanning.Contracts;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using OwnerAssignUseCase = LgymApi.Application.TrainingPlanning.Contracts.ManagedPlans.IAssignManagedPlanUseCase;
using OwnerCreateUseCase = LgymApi.Application.TrainingPlanning.Contracts.ManagedPlans.ICreateManagedPlanUseCase;
using OwnerDeleteUseCase = LgymApi.Application.TrainingPlanning.Contracts.ManagedPlans.IDeleteManagedPlanUseCase;
using OwnerGetActiveUseCase = LgymApi.Application.TrainingPlanning.Contracts.ManagedPlans.IGetActiveAssignedPlanUseCase;
using OwnerListUseCase = LgymApi.Application.TrainingPlanning.Contracts.ManagedPlans.IGetManagedPlansUseCase;
using OwnerUnassignUseCase = LgymApi.Application.TrainingPlanning.Contracts.ManagedPlans.IUnassignManagedPlanUseCase;
using OwnerUpdateUseCase = LgymApi.Application.TrainingPlanning.Contracts.ManagedPlans.IUpdateManagedPlanUseCase;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class CoachingManagedPlanSliceTests
{
    [Test]
    public async Task ManagedPlanSlices_AuthorizeThenDelegateEveryOperationWithExactInputsAndResults()
    {
        var trainerId = Id<AccountReference>.New();
        var traineeId = Id<AccountReference>.New();
        var planId = Id<PlanReference>.New();
        var cancellationToken = new CancellationTokenSource().Token;
        var readModel = new ManagedPlanReadModel(planId, "Managed", true, DateTimeOffset.UtcNow);
        IReadOnlyList<ManagedPlanReadModel> plans = [readModel];
        var dependencies = new Dependencies();
        dependencies.Access.GetAccessDecisionAsync(trainerId, traineeId, cancellationToken)
            .Returns(new CoachingRelationshipAccessDecision(true, true));
        dependencies.ActiveLinks.HasForTraineeAsync(traineeId, cancellationToken).Returns(true);
        dependencies.List.ExecuteAsync(Arg.Any<GetManagedPlansQuery>(), cancellationToken)
            .Returns(Result<IReadOnlyList<ManagedPlanReadModel>, AppError>.Success(plans));
        dependencies.Create.ExecuteAsync(Arg.Any<LgymApi.Application.TrainingPlanning.Contracts.ManagedPlans.CreateManagedPlanCommand>(), cancellationToken)
            .Returns(Result<ManagedPlanReadModel, AppError>.Success(readModel));
        dependencies.Update.ExecuteAsync(Arg.Any<LgymApi.Application.TrainingPlanning.Contracts.ManagedPlans.UpdateManagedPlanCommand>(), cancellationToken)
            .Returns(Result<ManagedPlanReadModel, AppError>.Success(readModel));
        dependencies.Delete.ExecuteAsync(Arg.Any<LgymApi.Application.TrainingPlanning.Contracts.ManagedPlans.DeleteManagedPlanCommand>(), cancellationToken)
            .Returns(Result<Unit, AppError>.Success(Unit.Value));
        dependencies.Assign.ExecuteAsync(Arg.Any<LgymApi.Application.TrainingPlanning.Contracts.ManagedPlans.AssignManagedPlanCommand>(), cancellationToken)
            .Returns(Result<Unit, AppError>.Success(Unit.Value));
        dependencies.Unassign.ExecuteAsync(Arg.Any<LgymApi.Application.TrainingPlanning.Contracts.ManagedPlans.UnassignManagedPlanCommand>(), cancellationToken)
            .Returns(Result<Unit, AppError>.Success(Unit.Value));
        dependencies.GetActive.ExecuteAsync(Arg.Any<GetActiveAssignedPlanQuery>(), cancellationToken)
            .Returns(Result<ManagedPlanReadModel, AppError>.Success(readModel));
        var services = dependencies.CreateServices();

        var listed = await Resolve<IListManagedPlansUseCase>(services)
            .ExecuteAsync(new ListManagedPlansQuery(trainerId, traineeId), cancellationToken);
        var created = await Resolve<LgymApi.Application.Coaching.ManagedPlans.Create.ICreateTraineeManagedPlanUseCase>(services)
            .ExecuteAsync(new LgymApi.Application.Coaching.ManagedPlans.Create.CreateTraineeManagedPlanCommand(trainerId, traineeId, "Managed"), cancellationToken);
        var updated = await Resolve<LgymApi.Application.Coaching.ManagedPlans.Update.IUpdateTraineeManagedPlanUseCase>(services)
            .ExecuteAsync(new LgymApi.Application.Coaching.ManagedPlans.Update.UpdateTraineeManagedPlanCommand(trainerId, traineeId, planId, "Updated"), cancellationToken);
        var deleted = await Resolve<LgymApi.Application.Coaching.ManagedPlans.Delete.IDeleteTraineeManagedPlanUseCase>(services)
            .ExecuteAsync(new LgymApi.Application.Coaching.ManagedPlans.Delete.DeleteTraineeManagedPlanCommand(trainerId, traineeId, planId), cancellationToken);
        var assigned = await Resolve<LgymApi.Application.Coaching.ManagedPlans.Assign.IAssignTraineeManagedPlanUseCase>(services)
            .ExecuteAsync(new LgymApi.Application.Coaching.ManagedPlans.Assign.AssignTraineeManagedPlanCommand(trainerId, traineeId, planId), cancellationToken);
        var unassigned = await Resolve<LgymApi.Application.Coaching.ManagedPlans.Unassign.IUnassignTraineeManagedPlanUseCase>(services)
            .ExecuteAsync(new LgymApi.Application.Coaching.ManagedPlans.Unassign.UnassignTraineeManagedPlanCommand(trainerId, traineeId), cancellationToken);
        var active = await Resolve<IGetActiveManagedPlanUseCase>(services)
            .ExecuteAsync(new GetActiveManagedPlanQuery(traineeId), cancellationToken);

        listed.Value.Should().BeSameAs(plans);
        created.Value.Should().BeSameAs(readModel);
        updated.Value.Should().BeSameAs(readModel);
        deleted.Value.Should().Be(Unit.Value);
        assigned.Value.Should().Be(Unit.Value);
        unassigned.Value.Should().Be(Unit.Value);
        active.Value.Should().BeSameAs(readModel);
        await dependencies.Access.Received(6).GetAccessDecisionAsync(trainerId, traineeId, cancellationToken);
        await dependencies.ActiveLinks.Received(1).HasForTraineeAsync(traineeId, cancellationToken);
        await dependencies.List.Received(1).ExecuteAsync(
            Arg.Is<GetManagedPlansQuery>(query => query.TraineeId == traineeId), cancellationToken);
        await dependencies.Create.Received(1).ExecuteAsync(
            Arg.Is<LgymApi.Application.TrainingPlanning.Contracts.ManagedPlans.CreateManagedPlanCommand>(command =>
                command.TrainerId == trainerId && command.TraineeId == traineeId && command.Name == "Managed"),
            cancellationToken);
        await dependencies.Update.Received(1).ExecuteAsync(
            Arg.Is<LgymApi.Application.TrainingPlanning.Contracts.ManagedPlans.UpdateManagedPlanCommand>(command =>
                command.TrainerId == trainerId && command.TraineeId == traineeId && command.PlanId == planId && command.Name == "Updated"),
            cancellationToken);
        await dependencies.Delete.Received(1).ExecuteAsync(
            Arg.Is<LgymApi.Application.TrainingPlanning.Contracts.ManagedPlans.DeleteManagedPlanCommand>(command =>
                command.TrainerId == trainerId && command.TraineeId == traineeId && command.PlanId == planId),
            cancellationToken);
        await dependencies.Assign.Received(1).ExecuteAsync(
            Arg.Is<LgymApi.Application.TrainingPlanning.Contracts.ManagedPlans.AssignManagedPlanCommand>(command =>
                command.TrainerId == trainerId && command.TraineeId == traineeId && command.PlanId == planId),
            cancellationToken);
        await dependencies.Unassign.Received(1).ExecuteAsync(
            Arg.Is<LgymApi.Application.TrainingPlanning.Contracts.ManagedPlans.UnassignManagedPlanCommand>(command => command.TraineeId == traineeId),
            cancellationToken);
        await dependencies.GetActive.Received(1).ExecuteAsync(
            Arg.Is<GetActiveAssignedPlanQuery>(query => query.TraineeId == traineeId), cancellationToken);
    }

    [Test]
    public async Task TrainerManagedPlanSlices_RejectForeignRelationshipBeforeEveryOwnerCall()
    {
        var trainerId = Id<AccountReference>.New();
        var traineeId = Id<AccountReference>.New();
        var planId = Id<PlanReference>.New();
        var dependencies = new Dependencies();
        dependencies.Access.GetAccessDecisionAsync(trainerId, traineeId, Arg.Any<CancellationToken>())
            .Returns(new CoachingRelationshipAccessDecision(true, false));
        var services = dependencies.CreateServices();

        var errors = new AppError[]
        {
            (await Resolve<IListManagedPlansUseCase>(services).ExecuteAsync(new ListManagedPlansQuery(trainerId, traineeId))).Error,
            (await Resolve<LgymApi.Application.Coaching.ManagedPlans.Create.ICreateTraineeManagedPlanUseCase>(services).ExecuteAsync(new LgymApi.Application.Coaching.ManagedPlans.Create.CreateTraineeManagedPlanCommand(trainerId, traineeId, "Name"))).Error,
            (await Resolve<LgymApi.Application.Coaching.ManagedPlans.Update.IUpdateTraineeManagedPlanUseCase>(services).ExecuteAsync(new LgymApi.Application.Coaching.ManagedPlans.Update.UpdateTraineeManagedPlanCommand(trainerId, traineeId, planId, "Name"))).Error,
            (await Resolve<LgymApi.Application.Coaching.ManagedPlans.Delete.IDeleteTraineeManagedPlanUseCase>(services).ExecuteAsync(new LgymApi.Application.Coaching.ManagedPlans.Delete.DeleteTraineeManagedPlanCommand(trainerId, traineeId, planId))).Error,
            (await Resolve<LgymApi.Application.Coaching.ManagedPlans.Assign.IAssignTraineeManagedPlanUseCase>(services).ExecuteAsync(new LgymApi.Application.Coaching.ManagedPlans.Assign.AssignTraineeManagedPlanCommand(trainerId, traineeId, planId))).Error,
            (await Resolve<LgymApi.Application.Coaching.ManagedPlans.Unassign.IUnassignTraineeManagedPlanUseCase>(services).ExecuteAsync(new LgymApi.Application.Coaching.ManagedPlans.Unassign.UnassignTraineeManagedPlanCommand(trainerId, traineeId))).Error
        };

        errors.Should().OnlyContain(error => error is TrainerRelationshipNotFoundError);
        await dependencies.List.DidNotReceiveWithAnyArgs().ExecuteAsync(default!, default);
        await dependencies.Create.DidNotReceiveWithAnyArgs().ExecuteAsync(default!, default);
        await dependencies.Update.DidNotReceiveWithAnyArgs().ExecuteAsync(default!, default);
        await dependencies.Delete.DidNotReceiveWithAnyArgs().ExecuteAsync(default!, default);
        await dependencies.Assign.DidNotReceiveWithAnyArgs().ExecuteAsync(default!, default);
        await dependencies.Unassign.DidNotReceiveWithAnyArgs().ExecuteAsync(default!, default);
    }

    [Test]
    public async Task ManagedPlanAccess_PreservesForbiddenInvalidAndMissingLinkErrorsBeforeDelegation()
    {
        var trainerId = Id<AccountReference>.New();
        var traineeId = Id<AccountReference>.New();
        var dependencies = new Dependencies();
        var services = dependencies.CreateServices();
        dependencies.Access.GetAccessDecisionAsync(trainerId, traineeId, Arg.Any<CancellationToken>())
            .Returns(new CoachingRelationshipAccessDecision(false, false));

        var forbidden = await Resolve<IListManagedPlansUseCase>(services)
            .ExecuteAsync(new ListManagedPlansQuery(trainerId, traineeId));
        dependencies.Access.GetAccessDecisionAsync(trainerId, Id<AccountReference>.Empty, Arg.Any<CancellationToken>())
            .Returns(new CoachingRelationshipAccessDecision(true, false));
        var invalid = await Resolve<LgymApi.Application.Coaching.ManagedPlans.Create.ICreateTraineeManagedPlanUseCase>(services)
            .ExecuteAsync(new LgymApi.Application.Coaching.ManagedPlans.Create.CreateTraineeManagedPlanCommand(trainerId, Id<AccountReference>.Empty, "Name"));
        var missingActiveLink = await Resolve<IGetActiveManagedPlanUseCase>(services)
            .ExecuteAsync(new GetActiveManagedPlanQuery(traineeId));

        forbidden.Error.Should().BeOfType<TrainerRelationshipForbiddenError>();
        invalid.Error.Should().BeOfType<InvalidTrainerRelationshipError>();
        missingActiveLink.Error.Should().BeOfType<TrainerRelationshipNotFoundError>();
        await dependencies.List.DidNotReceiveWithAnyArgs().ExecuteAsync(default!, default);
        await dependencies.Create.DidNotReceiveWithAnyArgs().ExecuteAsync(default!, default);
        await dependencies.GetActive.DidNotReceiveWithAnyArgs().ExecuteAsync(default!, default);
    }

    [Test]
    public async Task ManagedPlanSlices_PassThroughOwnerValidationOwnershipAndTransactionFailures()
    {
        var trainerId = Id<AccountReference>.New();
        var traineeId = Id<AccountReference>.New();
        var planId = Id<PlanReference>.New();
        var invalidName = new InvalidTrainerRelationshipError("invalid name");
        var foreignPlan = new TrainerRelationshipNotFoundError("foreign plan");
        var missingOwner = new TrainerRelationshipNotFoundError("missing owner");
        var dependencies = new Dependencies();
        dependencies.Access.GetAccessDecisionAsync(trainerId, traineeId, Arg.Any<CancellationToken>())
            .Returns(new CoachingRelationshipAccessDecision(true, true));
        dependencies.Create.ExecuteAsync(Arg.Any<LgymApi.Application.TrainingPlanning.Contracts.ManagedPlans.CreateManagedPlanCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result<ManagedPlanReadModel, AppError>.Failure(invalidName));
        dependencies.Update.ExecuteAsync(Arg.Any<LgymApi.Application.TrainingPlanning.Contracts.ManagedPlans.UpdateManagedPlanCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result<ManagedPlanReadModel, AppError>.Failure(foreignPlan));
        dependencies.Delete.ExecuteAsync(Arg.Any<LgymApi.Application.TrainingPlanning.Contracts.ManagedPlans.DeleteManagedPlanCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result<Unit, AppError>.Failure(missingOwner));
        dependencies.Assign.ExecuteAsync(Arg.Any<LgymApi.Application.TrainingPlanning.Contracts.ManagedPlans.AssignManagedPlanCommand>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<Result<Unit, AppError>>(new InvalidOperationException("owner transaction failed")));
        var services = dependencies.CreateServices();

        var create = await Resolve<LgymApi.Application.Coaching.ManagedPlans.Create.ICreateTraineeManagedPlanUseCase>(services)
            .ExecuteAsync(new LgymApi.Application.Coaching.ManagedPlans.Create.CreateTraineeManagedPlanCommand(trainerId, traineeId, " "));
        var update = await Resolve<LgymApi.Application.Coaching.ManagedPlans.Update.IUpdateTraineeManagedPlanUseCase>(services)
            .ExecuteAsync(new LgymApi.Application.Coaching.ManagedPlans.Update.UpdateTraineeManagedPlanCommand(trainerId, traineeId, Id<PlanReference>.Empty, "Name"));
        var delete = await Resolve<LgymApi.Application.Coaching.ManagedPlans.Delete.IDeleteTraineeManagedPlanUseCase>(services)
            .ExecuteAsync(new LgymApi.Application.Coaching.ManagedPlans.Delete.DeleteTraineeManagedPlanCommand(trainerId, traineeId, planId));
        Func<Task> assign = () => Resolve<LgymApi.Application.Coaching.ManagedPlans.Assign.IAssignTraineeManagedPlanUseCase>(services)
            .ExecuteAsync(new LgymApi.Application.Coaching.ManagedPlans.Assign.AssignTraineeManagedPlanCommand(trainerId, traineeId, planId));

        create.Error.Should().BeSameAs(invalidName);
        update.Error.Should().BeSameAs(foreignPlan);
        delete.Error.Should().BeSameAs(missingOwner);
        await assign.Should().ThrowAsync<InvalidOperationException>().WithMessage("owner transaction failed");
    }

    private static TContract Resolve<TContract>(ServiceCollection services) where TContract : notnull
    {
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        return scope.ServiceProvider.GetRequiredService<TContract>();
    }

    private sealed class Dependencies
    {
        public IMarkerCoachingRelationshipAccessService Access { get; } = Substitute.For<IMarkerCoachingRelationshipAccessService>();
        public ICoachingActiveLinkPersistence ActiveLinks { get; } = Substitute.For<ICoachingActiveLinkPersistence>();
        public OwnerListUseCase List { get; } = Substitute.For<OwnerListUseCase>();
        public OwnerCreateUseCase Create { get; } = Substitute.For<OwnerCreateUseCase>();
        public OwnerUpdateUseCase Update { get; } = Substitute.For<OwnerUpdateUseCase>();
        public OwnerDeleteUseCase Delete { get; } = Substitute.For<OwnerDeleteUseCase>();
        public OwnerAssignUseCase Assign { get; } = Substitute.For<OwnerAssignUseCase>();
        public OwnerUnassignUseCase Unassign { get; } = Substitute.For<OwnerUnassignUseCase>();
        public OwnerGetActiveUseCase GetActive { get; } = Substitute.For<OwnerGetActiveUseCase>();

        public ServiceCollection CreateServices()
        {
            var services = new ServiceCollection();
            services.AddApplicationMapping(LgymApi.Api.Mapping.MappingAssemblyMarkers.All);
            services.AddCoachingModule();
            services.AddScoped(_ => Access);
            services.AddScoped(_ => ActiveLinks);
            services.AddScoped(_ => List);
            services.AddScoped(_ => Create);
            services.AddScoped(_ => Update);
            services.AddScoped(_ => Delete);
            services.AddScoped(_ => Assign);
            services.AddScoped(_ => Unassign);
            services.AddScoped(_ => GetActive);
            return services;
        }
    }
}
