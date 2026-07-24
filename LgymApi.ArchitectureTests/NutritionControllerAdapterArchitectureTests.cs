using System.Reflection;
using LgymApi.Api.Features.Trainer.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace LgymApi.ArchitectureTests;

[TestFixture]
public sealed class NutritionControllerAdapterArchitectureTests
{
    private static readonly ControllerSpec[] Controllers =
    [
        new(typeof(TrainerDietPlansController),
        [
            new("GetTraineePlans", "GET", "trainees/{traineeId}/diet-plans", StatusCodes.Status200OK),
            new("GetTraineePlan", "GET", "trainees/{traineeId}/diet-plans/{dietPlanId}", StatusCodes.Status200OK),
            new("CreateTraineePlan", "POST", "trainees/{traineeId}/diet-plans", StatusCodes.Status201Created),
            new("UpdateTraineePlan", "POST", "trainees/{traineeId}/diet-plans/{dietPlanId}/update", StatusCodes.Status200OK),
            new("ActivateTraineePlan", "POST", "trainees/{traineeId}/diet-plans/{dietPlanId}/activate", StatusCodes.Status200OK),
            new("DeleteTraineePlan", "POST", "trainees/{traineeId}/diet-plans/{dietPlanId}/delete", StatusCodes.Status200OK),
            new("GetTraineePlanHistory", "GET", "trainees/{traineeId}/diet-plans/{dietPlanId}/history", StatusCodes.Status200OK)
        ]),
        new(typeof(TraineeDietPlanController),
        [
            new("GetCurrentPlans", "GET", "diet-plans/current", StatusCodes.Status200OK),
            new("GetCurrentPlan", "GET", "diet-plan/current", StatusCodes.Status200OK)
        ]),
        new(typeof(TrainerSupplementationController),
        [
            new("GetTraineePlans", "GET", "trainees/{traineeId}/supplement-plans", StatusCodes.Status200OK),
            new("CreateTraineePlan", "POST", "trainees/{traineeId}/supplement-plans", StatusCodes.Status201Created),
            new("UpdateTraineePlan", "POST", "trainees/{traineeId}/supplement-plans/{planId}/update", StatusCodes.Status200OK),
            new("DeleteTraineePlan", "POST", "trainees/{traineeId}/supplement-plans/{planId}/delete", StatusCodes.Status200OK),
            new("AssignTraineePlan", "POST", "trainees/{traineeId}/supplement-plans/{planId}/assign", StatusCodes.Status200OK),
            new("UnassignTraineePlan", "POST", "trainees/{traineeId}/supplement-plans/unassign", StatusCodes.Status200OK),
            new("GetComplianceSummary", "GET", "trainees/{traineeId}/supplements/compliance", StatusCodes.Status200OK)
        ]),
        new(typeof(TraineeSupplementationController),
        [
            new("GetSchedule", "GET", "supplements/schedule", StatusCodes.Status200OK),
            new("CheckOffIntake", "POST", "supplements/intakes/check-off", StatusCodes.Status200OK)
        ])
    ];

    [Test]
    public void Nutrition_Controllers_Should_Keep_The_Exact_Eighteen_Action_Manifest()
    {
        var actualActions = Controllers.SelectMany(specification => specification.Actions.Select(action =>
        {
            var method = specification.Controller.GetMethod(action.Name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)!;
            var route = method.GetCustomAttribute<HttpMethodAttribute>()!;
            var responseCodes = method.GetCustomAttributes<ProducesResponseTypeAttribute>()
                .Select(attribute => attribute.StatusCode)
                .ToArray();

            return new ActionSpec(
                specification.Controller.FullName!,
                method.Name,
                route.HttpMethods.Single(),
                route.Template!,
                responseCodes.Contains(action.StatusCode));
        })).ToArray();

        var expectedActions = Controllers.SelectMany(specification => specification.Actions.Select(action =>
            new ActionSpec(specification.Controller.FullName!, action.Name, action.HttpMethod, action.Template, true))).ToArray();

        Assert.That(actualActions, Is.EquivalentTo(expectedActions));
        Assert.That(actualActions, Has.Length.EqualTo(18));
    }

    [Test]
    public void Nutrition_Controllers_Should_Depend_Only_On_Focused_Use_Cases_And_Mapper()
    {
        var dependencies = Controllers.SelectMany(specification => specification.Controller.GetConstructors().Single().GetParameters()
            .Select(parameter => (Controller: specification.Controller.FullName!, Dependency: parameter.ParameterType.FullName!)))
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(dependencies, Has.Length.EqualTo(22));
            Assert.That(dependencies.Select(dependency => dependency.Dependency), Has.Exactly(4).EqualTo("LgymApi.Application.Mapping.Core.IMapper"));
            Assert.That(
                dependencies.Where(dependency => dependency.Dependency != "LgymApi.Application.Mapping.Core.IMapper")
                    .Select(dependency => dependency.Dependency),
                Has.All.StartsWith("LgymApi.Application.Nutrition."));
            Assert.That(
                dependencies.Where(dependency => dependency.Dependency != "LgymApi.Application.Mapping.Core.IMapper")
                    .Select(dependency => dependency.Dependency),
                Is.Unique);
        });
    }

    private sealed record ControllerSpec(Type Controller, IReadOnlyList<ActionExpectation> Actions);

    private sealed record ActionExpectation(string Name, string HttpMethod, string Template, int StatusCode);

    private sealed record ActionSpec(string Controller, string Name, string HttpMethod, string Template, bool HasExpectedStatusCode);
}
