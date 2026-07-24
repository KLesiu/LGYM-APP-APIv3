using LgymApi.Application.Nutrition.DietPlans.Models;
using LgymApi.Application.Nutrition.Supplementation.Models;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;

namespace LgymApi.ArchitectureTests;

[TestFixture]
public sealed class NutritionPublicContractGuardTests
{
    private static readonly Type[] ApprovedPublicModels =
    [
        typeof(DietPlanUpsertData), typeof(DietMealInput), typeof(DietPlanReadModel), typeof(DietMealReadModel),
        typeof(DietPlanHistoryReadModel), typeof(SupplementPlanUpsertData), typeof(SupplementPlanItemInput),
        typeof(SupplementPlanReadModel), typeof(SupplementPlanItemReadModel), typeof(SupplementScheduleEntryReadModel),
        typeof(SupplementComplianceSummaryReadModel)
    ];

    [Test]
    public void Nutrition_Public_Models_Should_Be_Exact_Sealed_Recursive_Value_Contracts()
    {
        var actualModels = typeof(DietPlanUpsertData).Assembly.GetTypes()
            .Where(type => type.IsPublic && type.Namespace is not null &&
                (type.Namespace.StartsWith("LgymApi.Application.Nutrition.DietPlans.Models", StringComparison.Ordinal) ||
                 type.Namespace.StartsWith("LgymApi.Application.Nutrition.Supplementation.Models", StringComparison.Ordinal)))
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToArray();
        var violations = actualModels.SelectMany(FindContractViolations).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(actualModels, Is.EquivalentTo(ApprovedPublicModels), "Nutrition model roster changed; add an explicit approved contract rather than a broad allowlist.");
            Assert.That(actualModels, Has.All.Matches<Type>(type => type.IsSealed && type.IsClass), "Nutrition public models must remain sealed records.");
            Assert.That(violations, Is.Empty, string.Join(Environment.NewLine, violations));
        });
    }

    [Test]
    public void Nutrition_Public_Model_Guard_Should_Reject_A_Foreign_Entity_Property_With_A_Targeted_Diagnostic()
    {
        var violations = FindContractViolations(typeof(ForeignEntityFixture));

        Assert.That(violations, Is.EqualTo(new[]
        {
            "ForeignEntityFixture.ForeignUser exposes forbidden type LgymApi.Domain.Entities.User."
        }));
    }

    private static IEnumerable<string> FindContractViolations(Type modelType)
    {
        foreach (var property in modelType.GetProperties())
        {
            foreach (var forbiddenType in FindForbiddenTypes(property.PropertyType))
            {
                yield return $"{modelType.Name}.{property.Name} exposes forbidden type {forbiddenType.FullName}.";
            }
        }
    }

    private static IEnumerable<Type> FindForbiddenTypes(Type type)
    {
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Id<>))
        {
            yield break;
        }

        if (type.IsArray)
        {
            foreach (var nested in FindForbiddenTypes(type.GetElementType()!))
            {
                yield return nested;
            }
            yield break;
        }

        if (type.IsGenericType)
        {
            foreach (var argument in type.GetGenericArguments())
            {
                foreach (var nested in FindForbiddenTypes(argument))
                {
                    yield return nested;
                }
            }
        }

        if (type.Namespace?.StartsWith("LgymApi.Domain.Entities", StringComparison.Ordinal) == true ||
            type.Namespace?.StartsWith("LgymApi.Application.Repositories", StringComparison.Ordinal) == true ||
            type.Namespace?.StartsWith("LgymApi.Infrastructure", StringComparison.Ordinal) == true ||
            type.Namespace?.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal) == true)
        {
            yield return type;
        }
    }

    private sealed record ForeignEntityFixture(User ForeignUser);
}
