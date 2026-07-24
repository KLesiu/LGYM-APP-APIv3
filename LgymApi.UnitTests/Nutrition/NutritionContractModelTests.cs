using System.Reflection;
using FluentAssertions;
using LgymApi.Application.Nutrition.DietPlans.Models;
using LgymApi.Application.Nutrition.Supplementation.Models;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using NUnit.Framework;

namespace LgymApi.UnitTests.Nutrition;

[TestFixture]
public sealed class NutritionContractModelTests
{
    private static readonly Type[] ContractTypes =
    [
        typeof(DietPlanUpsertData),
        typeof(DietMealInput),
        typeof(DietPlanReadModel),
        typeof(DietMealReadModel),
        typeof(DietPlanHistoryReadModel),
        typeof(SupplementPlanUpsertData),
        typeof(SupplementPlanItemInput),
        typeof(SupplementPlanReadModel),
        typeof(SupplementPlanItemReadModel),
        typeof(SupplementScheduleEntryReadModel),
        typeof(SupplementComplianceSummaryReadModel)
    ];

    [Test]
    public void PublicNutritionContracts_AreSealedImmutableAndContainOnlyApprovedRecursiveTypes()
    {
        var violations = ContractTypes.SelectMany(type => GetViolations(type, type.Name)).ToArray();

        violations.Should().BeEmpty();
        ContractTypes.All(type => type.IsClass && type.IsSealed).Should().BeTrue();
        ContractTypes.All(type => type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .All(property => property.SetMethod == null || IsInitOnly(property.SetMethod))).Should().BeTrue();
    }

    [Test]
    public void RecursiveContractGuard_ReportsTheFullPathForForbiddenUserAndMutableForeignModel()
    {
        var userViolations = GetViolations(typeof(LeakingUserContract), nameof(LeakingUserContract)).ToArray();
        var mutableViolations = GetViolations(typeof(LeakingForeignContract), nameof(LeakingForeignContract)).ToArray();

        userViolations.Should().ContainSingle().Which.Should().Contain("LeakingUserContract.User");
        mutableViolations.Should().ContainSingle().Which.Should().Contain("LeakingForeignContract.Foreign");
    }

    private static IEnumerable<string> GetViolations(Type type, string path)
    {
        foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            foreach (var violation in InspectType(property.PropertyType, $"{path}.{property.Name}"))
            {
                yield return violation;
            }
        }
    }

    private static IEnumerable<string> InspectType(Type type, string path)
    {
        if (type == typeof(User))
        {
            yield return $"{path} exposes User.";
            yield break;
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Id<>))
        {
            yield break;
        }

        if (Nullable.GetUnderlyingType(type) is { } underlyingType)
        {
            foreach (var violation in InspectType(underlyingType, path))
            {
                yield return violation;
            }

            yield break;
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IReadOnlyList<>))
        {
            foreach (var violation in InspectType(type.GetGenericArguments()[0], $"{path}[]"))
            {
                yield return violation;
            }

            yield break;
        }

        if (typeof(System.Collections.IEnumerable).IsAssignableFrom(type) && type != typeof(string))
        {
            yield return $"{path} uses unsupported collection {type.Name}.";
            yield break;
        }

        if (type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal)
            || type == typeof(DateOnly) || type == typeof(DateTimeOffset) || type == typeof(TimeSpan))
        {
            yield break;
        }

        if (type.Namespace?.StartsWith("LgymApi.Application.Nutrition", StringComparison.Ordinal) == true)
        {
            foreach (var violation in GetViolations(type, path))
            {
                yield return violation;
            }

            yield break;
        }

        yield return $"{path} exposes mutable or forbidden foreign type {type.FullName}.";
    }

    private static bool IsInitOnly(MethodInfo setMethod)
        => setMethod.ReturnParameter.GetRequiredCustomModifiers().Contains(typeof(System.Runtime.CompilerServices.IsExternalInit));

    private sealed record LeakingUserContract(User User);

    private sealed record LeakingForeignContract(ExternalMutableModel Foreign);

    private sealed class ExternalMutableModel
    {
        public string Value { get; set; } = string.Empty;
    }
}
