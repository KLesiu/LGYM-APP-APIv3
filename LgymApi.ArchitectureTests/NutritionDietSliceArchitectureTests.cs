using System.Reflection;
using FluentAssertions;
using LgymApi.Application.Nutrition;
using LgymApi.Application.Nutrition.DietPlans;
using LgymApi.Application.Nutrition.DietPlans.ActivateTraineePlan;
using LgymApi.Application.Nutrition.DietPlans.ActivateTraineePlan.Contracts;
using LgymApi.Application.Nutrition.DietPlans.ActivateTraineePlan.Models;
using LgymApi.Application.Nutrition.DietPlans.CreateTraineePlan;
using LgymApi.Application.Nutrition.DietPlans.DeleteTraineePlan;
using LgymApi.Application.Nutrition.DietPlans.DeleteTraineePlan.Contracts;
using LgymApi.Application.Nutrition.DietPlans.DeleteTraineePlan.Models;
using LgymApi.Application.Nutrition.DietPlans.GetCurrentDietPlan;
using LgymApi.Application.Nutrition.DietPlans.GetCurrentDietPlans;
using LgymApi.Application.Nutrition.DietPlans.GetTraineePlan;
using LgymApi.Application.Nutrition.DietPlans.GetTraineePlanHistory;
using LgymApi.Application.Nutrition.DietPlans.GetTraineePlanHistory.Contracts;
using LgymApi.Application.Nutrition.DietPlans.GetTraineePlanHistory.Models;
using LgymApi.Application.Nutrition.DietPlans.GetTraineePlans;
using LgymApi.Application.Nutrition.DietPlans.UpdateTraineePlan;
using LgymApi.Application.Nutrition.DietPlans.UpdateTraineePlan.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace LgymApi.ArchitectureTests;

[TestFixture]
public sealed class NutritionDietSliceArchitectureTests
{
    private static readonly (Type Contract, string ImplementationName)[] Slices =
    [
        (typeof(ICreateTraineeDietPlanUseCase), "LgymApi.Application.Nutrition.DietPlans.CreateTraineePlan.CreateTraineeDietPlanUseCase"),
        (typeof(IUpdateTraineeDietPlanUseCase), "LgymApi.Application.Nutrition.DietPlans.UpdateTraineePlan.UpdateTraineeDietPlanUseCase"),
        (typeof(IDeleteTraineeDietPlanUseCase), "LgymApi.Application.Nutrition.DietPlans.DeleteTraineePlan.DeleteTraineeDietPlanUseCase"),
        (typeof(IActivateTraineeDietPlanUseCase), "LgymApi.Application.Nutrition.DietPlans.ActivateTraineePlan.ActivateTraineeDietPlanUseCase"),
        (typeof(IGetTraineeDietPlanUseCase), "LgymApi.Application.Nutrition.DietPlans.GetTraineePlan.GetTraineeDietPlanUseCase"),
        (typeof(IGetTraineeDietPlansUseCase), "LgymApi.Application.Nutrition.DietPlans.GetTraineePlans.GetTraineeDietPlansUseCase"),
        (typeof(IGetCurrentDietPlansUseCase), "LgymApi.Application.Nutrition.DietPlans.GetCurrentDietPlans.GetCurrentDietPlansUseCase"),
        (typeof(IGetTraineeDietPlanHistoryUseCase), "LgymApi.Application.Nutrition.DietPlans.GetTraineePlanHistory.GetTraineeDietPlanHistoryUseCase"),
        (typeof(IGetCurrentDietPlanUseCase), "LgymApi.Application.Nutrition.DietPlans.GetCurrentDietPlan.GetCurrentDietPlanUseCase")
    ];

    [Test]
    public void DietSlices_ExposeOneMethodPublicContractsWithInternalImplementations()
    {
        var assembly = typeof(IGetCurrentDietPlanUseCase).Assembly;

        foreach (var slice in Slices)
        {
            slice.Contract.IsPublic.Should().BeTrue();
            slice.Contract.GetMethods(BindingFlags.Public | BindingFlags.Instance).Should().ContainSingle();
            assembly.GetType(slice.ImplementationName)!.IsNotPublic.Should().BeTrue();
        }
    }

    [Test]
    public void DietPublicInputs_AreSealedAndRecursivelyUseTypedIdentifiers()
    {
        var models = new[]
        {
            typeof(CreateTraineeDietPlanCommand), typeof(UpdateTraineeDietPlanCommand),
            typeof(DeleteTraineeDietPlanCommand), typeof(ActivateTraineeDietPlanCommand),
            typeof(GetTraineeDietPlanQuery), typeof(GetTraineeDietPlansQuery),
            typeof(GetCurrentDietPlansQuery), typeof(GetTraineeDietPlanHistoryQuery),
            typeof(GetCurrentDietPlanQuery)
        };

        models.Should().OnlyContain(model => model.IsSealed && model.GetProperties().All(property => IsAllowed(property.PropertyType)));
    }

    [Test]
    public void DietSlices_ExposeCancellationOnTheirOnlyMethod()
    {
        foreach (var (contract, _) in Slices)
        {
            contract.GetMethods().Single().GetParameters().Should().Contain(parameter => parameter.ParameterType == typeof(CancellationToken));
        }
    }

    [Test]
    public void DietSlices_AndHistoryFactory_AreRegisteredExactlyOnce()
    {
        var services = new ServiceCollection();
        services.AddNutritionModule();

        foreach (var (contract, _) in Slices)
        {
            services.Count(descriptor => descriptor.ServiceType == contract).Should().Be(1);
        }

        services.Count(descriptor => descriptor.ServiceType.FullName ==
            "LgymApi.Application.Nutrition.DietPlans.DietPlanHistorySnapshotFactory").Should().Be(1);
    }

    private static bool IsAllowed(Type type)
    {
        var nullableType = Nullable.GetUnderlyingType(type);
        if (nullableType is not null)
        {
            return IsAllowed(nullableType);
        }

        if (type == typeof(string) || type == typeof(bool) || type == typeof(int)
            || type == typeof(decimal) || type == typeof(DateOnly) || type == typeof(DateTimeOffset))
        {
            return true;
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(LgymApi.Domain.ValueObjects.Id<>))
        {
            return type.GetGenericArguments().Single() is var argument
                && argument is not null
                && argument.Namespace == "LgymApi.Domain.Entities"
                && argument.Name is "User" or "DietPlan";
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IReadOnlyList<>))
        {
            return IsAllowed(type.GetGenericArguments().Single());
        }

        return type.Namespace == "LgymApi.Application.Nutrition.DietPlans.Models"
            && type.GetProperties().All(property => IsAllowed(property.PropertyType));
    }
}
