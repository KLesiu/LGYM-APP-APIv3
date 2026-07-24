using FluentAssertions;
using LgymApi.Application.Mapping;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.Nutrition.DietPlans;
using LgymApi.Application.Nutrition.DietPlans.Models;
using LgymApi.Application.Nutrition.Supplementation;
using LgymApi.Application.Nutrition.Supplementation.Models;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace LgymApi.UnitTests.Nutrition;

[TestFixture]
public sealed class NutritionMappingProfileTests
{
    [Test]
    public void Mapper_MapsNormalizedDietAndNestedReadModelsThroughOneContext()
    {
        var mapper = CreateMapper();
        var plan = mapper.Map<NormalizedDietPlanData, DietPlan>(
            DietPlanRules.Normalize(new DietPlanUpsertData(
                " Lean bulk ",
                new DateOnly(2026, 7, 1),
                null,
                2800,
                180m,
                300m,
                70m,
                " weekly ",
                true,
                [
                    new DietMealInput("Dinner", 2, null, 900, 55m, 100m, 20m),
                    new DietMealInput("Breakfast", 1, " eggs ", 500, 30m, 60m, 10m)
                ])),
            mapper.CreateContext());
        plan.Id = Id<DietPlan>.New();
        plan.TrainerId = Id<User>.New();
        plan.TraineeId = Id<User>.New();
        plan.CreatedAt = new DateTimeOffset(2026, 7, 1, 8, 0, 0, TimeSpan.Zero);
        plan.UpdatedAt = plan.CreatedAt.AddDays(1);
        plan.Meals.ElementAt(0).Id = Id<DietMeal>.New();
        plan.Meals.ElementAt(1).Id = Id<DietMeal>.New();

        var readModel = mapper.Map<DietPlan, DietPlanReadModel>(plan, mapper.CreateContext());

        plan.Name.Should().Be("Lean bulk");
        plan.Meals.Select(meal => meal.Name).Should().Equal("Breakfast", "Dinner");
        readModel.Meals.Select(meal => meal.Name).Should().Equal("Breakfast", "Dinner");
        readModel.Notes.Should().Be("weekly");
    }

    [Test]
    public void Mapper_MapsDietHistoryAndAllSupplementPlanPairs()
    {
        var mapper = CreateMapper();
        var trainerId = Id<User>.New();
        var traineeId = Id<User>.New();
        var supplementPlan = mapper.Map<NormalizedSupplementPlanData, SupplementPlan>(
            SupplementationRules.Normalize(new SupplementPlanUpsertData(
                "Daily",
                "Morning",
                [
                    new SupplementPlanItemInput("Zinc", "20 mg", "09:30", 127, 1),
                    new SupplementPlanItemInput("Magnesium", "400 mg", "08:00", 127, 1)
                ])),
            mapper.CreateContext());
        supplementPlan.Id = Id<SupplementPlan>.New();
        supplementPlan.TrainerId = trainerId;
        supplementPlan.TraineeId = traineeId;
        supplementPlan.CreatedAt = new DateTimeOffset(2026, 7, 1, 6, 0, 0, TimeSpan.Zero);
        supplementPlan.Items.ElementAt(0).Id = Id<SupplementPlanItem>.New();
        supplementPlan.Items.ElementAt(1).Id = Id<SupplementPlanItem>.New();
        var history = new DietPlanHistory
        {
            Id = Id<DietPlanHistory>.New(),
            DietPlanId = Id<DietPlan>.New(),
            ChangedByUserId = trainerId,
            ChangeDate = supplementPlan.CreatedAt,
            ChangeType = "Created",
            SnapshotJson = "{}"
        };

        var planReadModel = mapper.Map<SupplementPlan, SupplementPlanReadModel>(supplementPlan, mapper.CreateContext());
        var historyReadModel = mapper.Map<DietPlanHistory, DietPlanHistoryReadModel>(history, mapper.CreateContext());

        supplementPlan.Items.Select(item => item.SupplementName).Should().Equal("Magnesium", "Zinc");
        planReadModel.Items.Select(item => item.TimeOfDay).Should().Equal("08:00", "09:30");
        historyReadModel.Should().Be(new DietPlanHistoryReadModel(
            history.Id,
            history.DietPlanId,
            trainerId,
            history.ChangeDate,
            "Created",
            "{}"));
    }

    private static IMapper CreateMapper()
    {
        var services = new ServiceCollection();
        services.AddApplicationMapping(typeof(IMappingProfile).Assembly);

        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IMapper>();
    }
}
