using FluentAssertions;
using LgymApi.Application.Common.Errors;
using LgymApi.Application.Nutrition.DietPlans;
using LgymApi.Application.Nutrition.DietPlans.Models;
using LgymApi.Application.Nutrition.Supplementation;
using LgymApi.Application.Nutrition.Supplementation.Models;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using NUnit.Framework;

namespace LgymApi.UnitTests.Nutrition;

[TestFixture]
public sealed class NutritionRulesAndProjectorTests
{
    [Test]
    public void DietRules_NormalizeAndApplyTheDietSpecificAccessOrder()
    {
        var data = new DietPlanUpsertData(
            " Cut ",
            new DateOnly(2026, 7, 1),
            null,
            2200,
            null,
            null,
            null,
            " ",
            false,
            [new DietMealInput(" Dinner ", -1, " rice ", null, null, null, null)]);

        var normalized = DietPlanRules.Normalize(data);

        DietPlanRules.GetUpsertError(data).Should().BeNull();
        normalized.Name.Should().Be("Cut");
        normalized.Notes.Should().BeNull();
        normalized.Meals.Should().ContainSingle().Which.Should().Be(new DietMealInput("Dinner", 0, "rice", null, null, null, null));
        DietPlanAccess.GetTrainerAccessError(false, true)
            .Should().BeOfType<TrainerRelationshipForbiddenError>();
        DietPlanAccess.GetTrainerAccessError(true, false)
            .Should().BeOfType<NotFoundError>();
    }

    [Test]
    public void SupplementationRules_PreserveValidationNormalizationAndAccessPrecedence()
    {
        var data = new SupplementPlanUpsertData(
            " Daily ",
            " before bed ",
            [
                new SupplementPlanItemInput(" Zinc ", " 20 mg ", "09:00", 127, 2),
                new SupplementPlanItemInput(" Magnesium ", " 400 mg ", "08:00", 127, 2)
            ]);

        var normalized = SupplementationRules.Normalize(data);

        SupplementationRules.GetUpsertError(data).Should().BeNull();
        normalized.Items.Select(item => item.SupplementName).Should().Equal("Magnesium", "Zinc");
        SupplementationRules.GetComplianceRangeError(new DateOnly(2026, 7, 2), new DateOnly(2026, 7, 1))
            .Should().BeOfType<InvalidSupplementationError>();
        SupplementationAccess.GetTrainerAccessError(false, false, default)
            .Should().BeOfType<SupplementationForbiddenError>();
        SupplementationAccess.GetTrainerAccessError(true, true, default)
            .Should().BeOfType<InvalidSupplementationError>();
        SupplementationAccess.GetTrainerAccessError(true, false, Id<User>.New())
            .Should().BeOfType<SupplementationNotFoundError>();
    }

    [Test]
    public void NutritionAccessHelpers_DoNotExposeTheCoachingDecisionType()
    {
        var forbiddenType = "LgymApi.Application.Coaching.Contracts.Access.CoachingRelationshipAccessDecision";
        var parameterTypes = new[] { typeof(DietPlanAccess), typeof(SupplementationAccess) }
            .SelectMany(type => type.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
            .SelectMany(method => method.GetParameters())
            .Select(parameter => parameter.ParameterType.FullName);

        parameterTypes.Should().NotContain(forbiddenType);
    }

    [Test]
    public void SupplementProjectors_PreserveMondayOrderTakenStateAndInclusiveRounding()
    {
        var traineeId = Id<User>.New();
        var firstItemId = Id<SupplementPlanItem>.New();
        var secondItemId = Id<SupplementPlanItem>.New();
        var date = new DateOnly(2026, 7, 6);
        var plan = new SupplementPlan
        {
            Items =
            [
                new SupplementPlanItem
                {
                    Id = secondItemId,
                    SupplementName = "Zinc",
                    Dosage = "20 mg",
                    DaysOfWeekMask = DaysOfWeekSet.Monday,
                    TimeOfDay = new TimeSpan(9, 0, 0),
                    Order = 1,
                    CreatedAt = new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero)
                },
                new SupplementPlanItem
                {
                    Id = firstItemId,
                    SupplementName = "Magnesium",
                    Dosage = "400 mg",
                    DaysOfWeekMask = DaysOfWeekSet.Monday,
                    TimeOfDay = new TimeSpan(8, 0, 0),
                    Order = 1,
                    CreatedAt = new DateTimeOffset(2026, 7, 1, 8, 0, 0, TimeSpan.Zero)
                }
            ]
        };
        var logs = new[]
        {
            new SupplementIntakeLog
            {
                TraineeId = traineeId,
                PlanItemId = firstItemId,
                IntakeDate = date,
                TakenAt = new DateTimeOffset(2026, 7, 6, 8, 5, 0, TimeSpan.Zero)
            }
        };

        var schedule = SupplementScheduleProjector.Project(plan, date, logs);
        var compliance = SupplementComplianceProjector.Project(traineeId, plan.Items, date, date.AddDays(2), logs);

        schedule.Select(entry => entry.SupplementName).Should().Equal("Magnesium", "Zinc");
        schedule[0].Taken.Should().BeTrue();
        schedule[0].TakenAt.Should().Be(logs[0].TakenAt);
        schedule[1].Taken.Should().BeFalse();
        compliance.PlannedDoses.Should().Be(2);
        compliance.TakenDoses.Should().Be(1);
        compliance.AdherenceRate.Should().Be(50);
    }
}
