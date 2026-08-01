using FluentAssertions;
using LgymApi.Application.Mapping;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.Nutrition.DietPlans;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace LgymApi.UnitTests.Nutrition;

[TestFixture]
public sealed class DietPlanHistorySnapshotFactoryTests
{
    [Test]
    public void Create_MapsThenSerializesTheExactLegacyHistorySnapshot()
    {
        var trainerId = ParseId<User>("00000000-0000-0000-0000-000000000101");
        var plan = new DietPlan
        {
            Id = ParseId<DietPlan>("00000000-0000-0000-0000-000000000201"),
            TrainerId = trainerId,
            TraineeId = ParseId<User>("00000000-0000-0000-0000-000000000102"),
            Name = "Lean Bulk",
            StartDate = new DateOnly(2026, 6, 1),
            EstimatedCalories = 2_800,
            ProteinGrams = 180.5m,
            FatGrams = 70.25m,
            IsActive = true,
            CreatedAt = new DateTimeOffset(2026, 6, 1, 8, 9, 10, TimeSpan.FromHours(2)),
            UpdatedAt = new DateTimeOffset(2026, 6, 2, 9, 10, 11, TimeSpan.FromHours(2)),
            Meals =
            [
                new DietMeal
                {
                    Id = ParseId<DietMeal>("00000000-0000-0000-0000-000000000301"),
                    Name = "Dinner",
                    Order = 2,
                    Description = "Rice",
                    EstimatedCalories = 900,
                    ProteinGrams = 55m,
                    CarbsGrams = 100.5m,
                    FatGrams = 20.25m
                },
                new DietMeal
                {
                    Id = ParseId<DietMeal>("00000000-0000-0000-0000-000000000302"),
                    Name = "Breakfast",
                    Order = 1,
                    EstimatedCalories = 500,
                    ProteinGrams = 30.5m,
                    CarbsGrams = 60m,
                    FatGrams = 10m
                }
            ]
        };

        var history = new DietPlanHistorySnapshotFactory(CreateMapper()).Create(plan, trainerId, "Updated");

        history.ChangeType.Should().Be("Updated");
        history.DietPlanId.Should().Be(plan.Id);
        history.SnapshotJson.Should().Be("{\"Id\":{\"Value\":\"00000000-0000-0000-0000-000000000201\",\"IsEmpty\":false},\"TrainerId\":{\"Value\":\"00000000-0000-0000-0000-000000000101\",\"IsEmpty\":false},\"TraineeId\":{\"Value\":\"00000000-0000-0000-0000-000000000102\",\"IsEmpty\":false},\"Name\":\"Lean Bulk\",\"StartDate\":\"2026-06-01\",\"EndDate\":null,\"EstimatedCalories\":2800,\"ProteinGrams\":180.5,\"CarbsGrams\":null,\"FatGrams\":70.25,\"Notes\":null,\"IsActive\":true,\"CreatedAt\":\"2026-06-01T08:09:10+02:00\",\"UpdatedAt\":\"2026-06-02T09:10:11+02:00\",\"Meals\":[{\"Id\":{\"Value\":\"00000000-0000-0000-0000-000000000302\",\"IsEmpty\":false},\"Name\":\"Breakfast\",\"Order\":1,\"Description\":null,\"EstimatedCalories\":500,\"ProteinGrams\":30.5,\"CarbsGrams\":60,\"FatGrams\":10},{\"Id\":{\"Value\":\"00000000-0000-0000-0000-000000000301\",\"IsEmpty\":false},\"Name\":\"Dinner\",\"Order\":2,\"Description\":\"Rice\",\"EstimatedCalories\":900,\"ProteinGrams\":55,\"CarbsGrams\":100.5,\"FatGrams\":20.25}]}");
    }

    private static IMapper CreateMapper()
    {
        var services = new ServiceCollection();
        services.AddApplicationMapping(LgymApi.Api.Mapping.MappingAssemblyMarkers.All);

        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IMapper>();
    }

    private static Id<TEntity> ParseId<TEntity>(string value)
        where TEntity : class
    {
        Id<TEntity>.TryParse(value, out var id).Should().BeTrue();
        return id;
    }
}
