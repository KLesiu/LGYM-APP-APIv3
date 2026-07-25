using FluentAssertions;
using LgymApi.Application.Coaching.ManagedPlans.GetActive;
using LgymApi.Application.Coaching.ManagedPlans.List;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.Coaching.Errors;
using LgymApi.Application.TrainingPlanning.Errors;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Data;
using LgymApi.Infrastructure.Data.SeedData;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using CoachingAssign = LgymApi.Application.Coaching.ManagedPlans.Assign;
using CoachingCreate = LgymApi.Application.Coaching.ManagedPlans.Create;
using CoachingDelete = LgymApi.Application.Coaching.ManagedPlans.Delete;
using CoachingUnassign = LgymApi.Application.Coaching.ManagedPlans.Unassign;
using CoachingUpdate = LgymApi.Application.Coaching.ManagedPlans.Update;

namespace LgymApi.IntegrationTests;

[TestFixture]
public sealed class CoachingManagedPlanSliceIntegrationTests : IntegrationTestBase
{
    [Test]
    public async Task ManagedPlanSlices_PreserveTemplateCloneActivationPointerAndActiveReadSemantics()
    {
        var trainer = await SeedUserAsync("slice-plan-trainer", "slice-plan-trainer@example.test");
        var trainee = await SeedUserAsync("slice-plan-trainee", "slice-plan-trainee@example.test");
        await SeedRelationshipAsync(trainer.Id, trainee.Id);

        Id<Plan> templateId;
        Id<Plan> cloneId;
        using (var actionScope = Factory.Services.CreateScope())
        {
            var services = actionScope.ServiceProvider;
            var created = await services.GetRequiredService<CoachingCreate.ICreateTraineeManagedPlanUseCase>()
                .ExecuteAsync(new CoachingCreate.CreateTraineeManagedPlanCommand(trainer.Id, trainee.Id, "  Template  "));
            created.IsSuccess.Should().BeTrue();
            created.Value.Name.Should().Be("Template");
            created.Value.IsActive.Should().BeFalse();
            templateId = created.Value.Id;

            var updatedTemplate = await services.GetRequiredService<CoachingUpdate.IUpdateTraineeManagedPlanUseCase>()
                .ExecuteAsync(new CoachingUpdate.UpdateTraineeManagedPlanCommand(
                    trainer.Id,
                    trainee.Id,
                    templateId,
                    "  Updated template  "));
            updatedTemplate.Value.Name.Should().Be("Updated template");

            var beforeAssignment = await services.GetRequiredService<IListManagedPlansUseCase>()
                .ExecuteAsync(new ListManagedPlansQuery(trainer.Id, trainee.Id));
            beforeAssignment.Value.Should().BeEmpty();

            var assignedTemplate = await services.GetRequiredService<CoachingAssign.IAssignTraineeManagedPlanUseCase>()
                .ExecuteAsync(new CoachingAssign.AssignTraineeManagedPlanCommand(trainer.Id, trainee.Id, templateId));
            assignedTemplate.IsSuccess.Should().BeTrue();

            var afterClone = await services.GetRequiredService<IListManagedPlansUseCase>()
                .ExecuteAsync(new ListManagedPlansQuery(trainer.Id, trainee.Id));
            afterClone.Value.Should().ContainSingle();
            cloneId = afterClone.Value.Single().Id;
            cloneId.Should().NotBe(templateId);
            afterClone.Value.Single().IsActive.Should().BeTrue();

            var activeClone = await services.GetRequiredService<IGetActiveManagedPlanUseCase>()
                .ExecuteAsync(new GetActiveManagedPlanQuery(trainee.Id));
            activeClone.Value.Id.Should().Be(cloneId);

            var updatedClone = await services.GetRequiredService<CoachingUpdate.IUpdateTraineeManagedPlanUseCase>()
                .ExecuteAsync(new CoachingUpdate.UpdateTraineeManagedPlanCommand(
                    trainer.Id,
                    trainee.Id,
                    cloneId,
                    "Updated clone"));
            updatedClone.Value.Name.Should().Be("Updated clone");

            var unassigned = await services.GetRequiredService<CoachingUnassign.IUnassignTraineeManagedPlanUseCase>()
                .ExecuteAsync(new CoachingUnassign.UnassignTraineeManagedPlanCommand(trainer.Id, trainee.Id));
            unassigned.IsSuccess.Should().BeTrue();
            var noActivePlan = await services.GetRequiredService<IGetActiveManagedPlanUseCase>()
                .ExecuteAsync(new GetActiveManagedPlanQuery(trainee.Id));
            noActivePlan.Error.Should().BeOfType<PlanNotFoundError>();

            var assignedExisting = await services.GetRequiredService<CoachingAssign.IAssignTraineeManagedPlanUseCase>()
                .ExecuteAsync(new CoachingAssign.AssignTraineeManagedPlanCommand(trainer.Id, trainee.Id, cloneId));
            assignedExisting.IsSuccess.Should().BeTrue();

            var deletedTemplate = await services.GetRequiredService<CoachingDelete.IDeleteTraineeManagedPlanUseCase>()
                .ExecuteAsync(new CoachingDelete.DeleteTraineeManagedPlanCommand(trainer.Id, trainee.Id, templateId));
            deletedTemplate.IsSuccess.Should().BeTrue();
        }

        using var verificationScope = Factory.Services.CreateScope();
        var database = verificationScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var plans = await database.Plans.IgnoreQueryFilters()
            .Where(plan => plan.Id == templateId || plan.Id == cloneId)
            .ToListAsync();
        var persistedTemplate = plans.Single(plan => plan.Id == templateId);
        var persistedClone = plans.Single(plan => plan.Id == cloneId);
        var persistedTrainee = await database.Users.SingleAsync(user => user.Id == trainee.Id);

        persistedTemplate.UserId.Should().Be(trainer.Id);
        persistedTemplate.IsDeleted.Should().BeTrue();
        persistedTemplate.IsActive.Should().BeFalse();
        persistedClone.UserId.Should().Be(trainee.Id);
        persistedClone.Name.Should().Be("Updated clone");
        persistedClone.IsActive.Should().BeTrue();
        persistedTrainee.PlanId.Should().Be(cloneId);
    }

    [Test]
    public async Task TrainerManagedPlanSlices_RejectNonTrainerForeignRelationshipAndEmptyTraineeWithoutWrites()
    {
        var trainer = await SeedUserAsync("slice-plan-access-trainer", "slice-plan-access-trainer@example.test");
        var trainee = await SeedUserAsync("slice-plan-access-trainee", "slice-plan-access-trainee@example.test");
        var nonTrainer = await SeedUserAsync("slice-plan-access-user", "slice-plan-access-user@example.test");
        await SeedTrainerRoleAsync(trainer.Id);

        using var actionScope = Factory.Services.CreateScope();
        var services = actionScope.ServiceProvider;
        var forbidden = await services.GetRequiredService<CoachingCreate.ICreateTraineeManagedPlanUseCase>()
            .ExecuteAsync(new CoachingCreate.CreateTraineeManagedPlanCommand(nonTrainer.Id, trainee.Id, "Nope"));
        var foreign = await services.GetRequiredService<IListManagedPlansUseCase>()
            .ExecuteAsync(new ListManagedPlansQuery(trainer.Id, trainee.Id));
        var invalid = await services.GetRequiredService<CoachingCreate.ICreateTraineeManagedPlanUseCase>()
            .ExecuteAsync(new CoachingCreate.CreateTraineeManagedPlanCommand(trainer.Id, Id<User>.Empty, "Nope"));

        forbidden.Error.Should().BeOfType<TrainerRelationshipForbiddenError>();
        foreign.Error.Should().BeOfType<TrainerRelationshipNotFoundError>();
        invalid.Error.Should().BeOfType<InvalidTrainerRelationshipError>();
        (await services.GetRequiredService<AppDbContext>().Plans.CountAsync()).Should().Be(0);
    }

    [Test]
    public async Task TrainerManagedPlanSlices_PreserveOwnerValidationForeignPlanAndMissingOwnerFailures()
    {
        var trainer = await SeedUserAsync("slice-plan-failure-trainer", "slice-plan-failure-trainer@example.test");
        var trainee = await SeedUserAsync("slice-plan-failure-trainee", "slice-plan-failure-trainee@example.test");
        var missingOwner = await SeedUserAsync(
            "slice-plan-missing-owner",
            "slice-plan-missing-owner@example.test",
            isDeleted: true);
        var foreignOwner = await SeedUserAsync("slice-plan-foreign-owner", "slice-plan-foreign-owner@example.test");
        await SeedRelationshipAsync(trainer.Id, trainee.Id);
        await SeedRelationshipAsync(trainer.Id, missingOwner.Id, addTrainerRole: false);
        Id<Plan> templateId;
        Id<Plan> foreignPlanId;

        using (var seedScope = Factory.Services.CreateScope())
        {
            var database = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var template = new Plan { Id = Id<Plan>.New(), UserId = trainer.Id, Name = "Template" };
            var foreignPlan = new Plan { Id = Id<Plan>.New(), UserId = foreignOwner.Id, Name = "Foreign" };
            database.Plans.AddRange(template, foreignPlan);
            await database.SaveChangesAsync();
            templateId = template.Id;
            foreignPlanId = foreignPlan.Id;
        }

        using var actionScope = Factory.Services.CreateScope();
        var services = actionScope.ServiceProvider;
        var invalidName = await services.GetRequiredService<CoachingCreate.ICreateTraineeManagedPlanUseCase>()
            .ExecuteAsync(new CoachingCreate.CreateTraineeManagedPlanCommand(trainer.Id, trainee.Id, " "));
        var invalidPlanId = await services.GetRequiredService<CoachingUpdate.IUpdateTraineeManagedPlanUseCase>()
            .ExecuteAsync(new CoachingUpdate.UpdateTraineeManagedPlanCommand(trainer.Id, trainee.Id, Id<Plan>.Empty, "Name"));
        var foreignPlanResult = await services.GetRequiredService<CoachingAssign.IAssignTraineeManagedPlanUseCase>()
            .ExecuteAsync(new CoachingAssign.AssignTraineeManagedPlanCommand(trainer.Id, trainee.Id, foreignPlanId));
        var unavailableOwner = await services.GetRequiredService<CoachingAssign.IAssignTraineeManagedPlanUseCase>()
            .ExecuteAsync(new CoachingAssign.AssignTraineeManagedPlanCommand(trainer.Id, missingOwner.Id, templateId));

        invalidName.Error.Should().BeOfType<InvalidPlanError>();
        invalidPlanId.Error.Should().BeOfType<InvalidPlanError>();
        foreignPlanResult.Error.Should().BeOfType<PlanNotFoundError>();
        unavailableOwner.Error.Should().BeOfType<PlanNotFoundError>();
    }

    private async Task SeedRelationshipAsync(
        Id<User> trainerId,
        Id<User> traineeId,
        bool addTrainerRole = true)
    {
        using var seedScope = Factory.Services.CreateScope();
        var database = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (addTrainerRole)
        {
            database.UserRoles.Add(new UserRole
            {
                UserId = trainerId,
                RoleId = RoleSeedDataConfiguration.TrainerRoleSeedId
            });
        }

        database.TrainerTraineeLinks.Add(new TrainerTraineeLink
        {
            Id = Id<TrainerTraineeLink>.New(),
            TrainerId = trainerId,
            TraineeId = traineeId
        });
        await database.SaveChangesAsync();
    }

    private async Task SeedTrainerRoleAsync(Id<User> trainerId)
    {
        using var seedScope = Factory.Services.CreateScope();
        var database = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();
        database.UserRoles.Add(new UserRole
        {
            UserId = trainerId,
            RoleId = RoleSeedDataConfiguration.TrainerRoleSeedId
        });
        await database.SaveChangesAsync();
    }
}
