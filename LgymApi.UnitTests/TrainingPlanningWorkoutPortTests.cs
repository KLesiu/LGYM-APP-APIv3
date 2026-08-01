using FluentAssertions;
using LgymApi.Application.Mapping;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.TrainingPlanning.Contracts.PlanDay;
using LgymApi.Application.WorkoutProgress;
using LgymApi.Application.WorkoutProgress.Adapters;
using LgymApi.Application.WorkoutProgress.Persistence;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using LgymApi.TrainingPlanning.Contracts;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class TrainingPlanningWorkoutPortTests
{
    [Test]
    public async Task Catalog_ReturnsBatchedCultureAwareItemsAndForwardsCancellation()
    {
        var globalId = Id<Exercise>.New();
        var accountId = Id<User>.New();
        var accountExerciseId = Id<Exercise>.New();
        var exercises = Substitute.For<IWorkoutExercisePersistence>();
        exercises.GetByIdsAsync(Arg.Any<IReadOnlyCollection<Id<Exercise>>>(), Arg.Any<CancellationToken>())
            .Returns([
                new WorkoutExercisePersistenceModel(globalId, null, "Global", BodyParts.Chest, ExerciseEloFormula.Standard, null, null, false, default, default),
                new WorkoutExercisePersistenceModel(accountExerciseId, accountId.Rebind<AccountReference>(), "Custom", BodyParts.Back, ExerciseEloFormula.Standard, null, null, false, default, default)
            ]);
        using var cancellation = new CancellationTokenSource();
        var cultures = new[] { "pl-PL", "pl", "en" };
        exercises.GetTranslationsAsync(Arg.Any<IReadOnlyCollection<Id<Exercise>>>(), cultures, cancellation.Token)
            .Returns(new Dictionary<Id<Exercise>, string> { [globalId] = "Globalny" });

        var result = await new PlanExerciseCatalogAdapter(exercises, CreateMapper()).GetByIdsAsync(
            [globalId.Rebind<PlanExerciseReference>(), accountExerciseId.Rebind<PlanExerciseReference>()],
            cultures,
            cancellation.Token);

        result.Should().ContainKeys(globalId.Rebind<PlanExerciseReference>(), accountExerciseId.Rebind<PlanExerciseReference>());
        result[globalId.Rebind<PlanExerciseReference>()].Name.Should().Be("Globalny");
        result[accountExerciseId.Rebind<PlanExerciseReference>()].Name.Should().Be("Custom");
        await exercises.Received(1).GetTranslationsAsync(
            Arg.Is<IReadOnlyCollection<Id<Exercise>>>(ids => ids.SequenceEqual(new[] { globalId })),
            cultures,
            cancellation.Token);
    }

    [Test]
    public async Task TrainingActivity_ReturnsNullForEveryRequestedPlanDayWithoutHistory()
    {
        var firstPlanDayId = Id<PlanDayReference>.New();
        var secondPlanDayId = Id<PlanDayReference>.New();
        var trainings = Substitute.For<IWorkoutTrainingPersistence>();
        trainings.GetLastTrainingDatesAsync(Arg.Any<IReadOnlyCollection<Id<PlanDayReference>>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Id<PlanDayReference>, DateTime?>
            {
                [firstPlanDayId] = null,
                [secondPlanDayId] = null
            });

        var result = await new PlanTrainingActivityAdapter(trainings).GetLastTrainingDatesAsync(
            [firstPlanDayId, secondPlanDayId],
            CancellationToken.None);

        result.Should().Equal(new Dictionary<Id<PlanDayReference>, DateTime?>
        {
            [firstPlanDayId] = null,
            [secondPlanDayId] = null
        });
    }

    [Test]
    public async Task Clone_StagesOnlyCustomExerciseCopiesAndPreservesGlobalIds()
    {
        var globalId = Id<Exercise>.New();
        var customId = Id<Exercise>.New();
        var targetAccountId = Id<AccountReference>.New();
        var exercises = Substitute.For<IWorkoutExercisePersistence>();
        exercises.GetByIdsAsync(Arg.Any<IReadOnlyCollection<Id<Exercise>>>(), Arg.Any<CancellationToken>())
            .Returns([
                new WorkoutExercisePersistenceModel(globalId, null, "Global", BodyParts.Chest, ExerciseEloFormula.Standard, null, null, false, default, default),
                new WorkoutExercisePersistenceModel(customId, Id<AccountReference>.New(), "Custom", BodyParts.Back, ExerciseEloFormula.Standard, "Description", "image", false, default, default)
            ]);
        WorkoutExerciseWriteModel? staged = null;
        exercises.AddAsync(Arg.Do<WorkoutExerciseWriteModel>(exercise => staged = exercise), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var result = await new PlanExerciseCloneAdapter(exercises).StageClonesAsync(
            targetAccountId,
            [globalId.Rebind<PlanExerciseReference>(), customId.Rebind<PlanExerciseReference>()],
            CancellationToken.None);

        result[globalId.Rebind<PlanExerciseReference>()].Should().Be(globalId.Rebind<PlanExerciseReference>());
        result[customId.Rebind<PlanExerciseReference>()].Should().NotBe(customId.Rebind<PlanExerciseReference>());
        staged.Should().BeEquivalentTo(new
        {
            Name = "Custom",
            OwnerId = targetAccountId,
            BodyPart = BodyParts.Back,
            Description = "Description",
            Image = "image",
            IsDeleted = false
        });
        await exercises.Received(1).AddAsync(Arg.Any<WorkoutExerciseWriteModel>(), CancellationToken.None);
    }

    [Test]
    public async Task Clone_PropagatesRepositoryCancellationWithoutStagingWrites()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var exercises = Substitute.For<IWorkoutExercisePersistence>();
        exercises.GetByIdsAsync(Arg.Any<IReadOnlyCollection<Id<Exercise>>>(), cancellation.Token)
            .Returns(Task.FromCanceled<IReadOnlyList<WorkoutExercisePersistenceModel>>(cancellation.Token));

        var action = () => new PlanExerciseCloneAdapter(exercises).StageClonesAsync(
            Id<AccountReference>.New(),
            [Id<PlanExerciseReference>.New()],
            cancellation.Token);

        await action.Should().ThrowAsync<TaskCanceledException>();
        await exercises.DidNotReceive().AddAsync(Arg.Any<WorkoutExerciseWriteModel>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public void WorkoutComposition_RegistersEachPlanningPortExactlyOnce()
    {
        var services = new ServiceCollection();
        services.AddWorkoutAndProgressModule();

        AssertRegistration<IPlanExerciseCatalogPort, PlanExerciseCatalogAdapter>(services);
        AssertRegistration<IPlanExerciseClonePort, PlanExerciseCloneAdapter>(services);
        AssertRegistration<IPlanTrainingActivityPort, PlanTrainingActivityAdapter>(services);
    }

    private static IMapper CreateMapper()
    {
        var services = new ServiceCollection();
        services.AddApplicationMapping(LgymApi.Api.Mapping.MappingAssemblyMarkers.All);
        return services.BuildServiceProvider().GetRequiredService<IMapper>();
    }

    private static void AssertRegistration<TPort, TAdapter>(IServiceCollection services)
    {
        var registrations = services.Where(descriptor => descriptor.ServiceType == typeof(TPort)).ToList();
        registrations.Should().ContainSingle();
        var registration = registrations[0];
        registration.ImplementationType.Should().Be(typeof(TAdapter));
        registration.Lifetime.Should().Be(ServiceLifetime.Scoped);
    }
}
