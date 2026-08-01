using System.Text.Json;
using FluentAssertions;
using LgymApi.Api.Features.Trainer.Contracts;
using NUnit.Framework;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class DietPlanDtosTests
{
    [Test]
    public void UpsertDietPlanRequest_RoundTripsInheritedProperties()
    {
        var request = new UpsertDietPlanRequest
        {
            Name = "Cut",
            StartDate = new DateOnly(2026, 7, 1),
            EndDate = new DateOnly(2026, 8, 1),
            EstimatedCalories = 2200,
            ProteinGrams = 180,
            CarbsGrams = 150,
            FatGrams = 60,
            Notes = "Keep deficit",
            IsActive = true,
            Meals =
            [
                new UpsertDietMealRequest
                {
                    Name = "Breakfast",
                    Order = 1,
                    Description = "Oats",
                    EstimatedCalories = 500,
                    ProteinGrams = 30,
                    CarbsGrams = 70,
                    FatGrams = 10
                }
            ]
        };

        var json = JsonSerializer.Serialize(request);
        var roundTrip = JsonSerializer.Deserialize<UpsertDietPlanRequest>(json);

        roundTrip.Should().NotBeNull();
        roundTrip!.Name.Should().Be("Cut");
        roundTrip.EstimatedCalories.Should().Be(2200);
        roundTrip.ProteinGrams.Should().Be(180);
        roundTrip.Meals.Should().ContainSingle();
        roundTrip.Meals[0].Description.Should().Be("Oats");
        roundTrip.Meals[0].FatGrams.Should().Be(10);
    }

    [Test]
    public void DietPlanDto_RoundTripsResultAndNestedMeals()
    {
        var dto = new DietPlanDto
        {
            Id = "plan-1",
            TrainerId = "trainer-1",
            TraineeId = "trainee-1",
            Name = "Lean bulk",
            StartDate = new DateOnly(2026, 7, 1),
            EndDate = new DateOnly(2026, 7, 31),
            EstimatedCalories = 2900,
            ProteinGrams = 190,
            CarbsGrams = 320,
            FatGrams = 75,
            Notes = "Weekly check-in",
            IsActive = true,
            CreatedAt = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero),
            UpdatedAt = new DateTimeOffset(2026, 7, 2, 0, 0, 0, TimeSpan.Zero),
            Meals =
            [
                new DietMealDto
                {
                    Id = "meal-1",
                    Name = "Dinner",
                    Order = 2,
                    Description = "Rice and chicken",
                    EstimatedCalories = 900,
                    ProteinGrams = 55,
                    CarbsGrams = 100,
                    FatGrams = 20
                }
            ]
        };

        var json = JsonSerializer.Serialize(dto);
        var roundTrip = JsonSerializer.Deserialize<DietPlanDto>(json);

        roundTrip.Should().NotBeNull();
        roundTrip!.TrainerId.Should().Be("trainer-1");
        roundTrip.TraineeId.Should().Be("trainee-1");
        roundTrip.Name.Should().Be("Lean bulk");
        roundTrip.Meals.Should().ContainSingle();
        roundTrip.Meals[0].Id.Should().Be("meal-1");
        roundTrip.Meals[0].EstimatedCalories.Should().Be(900);
    }

    [Test]
    public void DietPlanContracts_SerializeExactGoldenJson()
    {
        var plan = new DietPlanDto
        {
            Id = "00000000-0000-0000-0000-000000000101",
            TrainerId = "00000000-0000-0000-0000-000000000102",
            TraineeId = "00000000-0000-0000-0000-000000000103",
            Name = "Competition plan",
            StartDate = new DateOnly(2026, 7, 23),
            EndDate = null,
            EstimatedCalories = 2_750,
            ProteinGrams = 180.5m,
            CarbsGrams = null,
            FatGrams = 72.25m,
            Notes = null,
            IsActive = true,
            CreatedAt = new DateTimeOffset(2026, 7, 23, 8, 9, 10, TimeSpan.FromHours(2)),
            UpdatedAt = new DateTimeOffset(2026, 7, 24, 11, 12, 13, TimeSpan.FromHours(-4)),
            Meals =
            [
                new DietMealDto
                {
                    Id = "00000000-0000-0000-0000-000000000202",
                    Name = "Dinner",
                    Order = 2,
                    Description = null,
                    EstimatedCalories = 900,
                    ProteinGrams = 55m,
                    CarbsGrams = 100.5m,
                    FatGrams = 20.25m
                },
                new DietMealDto
                {
                    Id = "00000000-0000-0000-0000-000000000201",
                    Name = "Breakfast",
                    Order = 1,
                    Description = "Eggs and oats",
                    EstimatedCalories = null,
                    ProteinGrams = null,
                    CarbsGrams = 60m,
                    FatGrams = 10m
                }
            ]
        };

        var json = JsonSerializer.Serialize(plan);
        const string expected = "{\"_id\":\"00000000-0000-0000-0000-000000000101\",\"trainerId\":\"00000000-0000-0000-0000-000000000102\",\"traineeId\":\"00000000-0000-0000-0000-000000000103\",\"createdAt\":\"2026-07-23T08:09:10+02:00\",\"updatedAt\":\"2026-07-24T11:12:13-04:00\",\"meals\":[{\"_id\":\"00000000-0000-0000-0000-000000000202\",\"name\":\"Dinner\",\"order\":2,\"description\":null,\"estimatedCalories\":900,\"proteinGrams\":55,\"carbsGrams\":100.5,\"fatGrams\":20.25},{\"_id\":\"00000000-0000-0000-0000-000000000201\",\"name\":\"Breakfast\",\"order\":1,\"description\":\"Eggs and oats\",\"estimatedCalories\":null,\"proteinGrams\":null,\"carbsGrams\":60,\"fatGrams\":10}],\"name\":\"Competition plan\",\"startDate\":\"2026-07-23\",\"endDate\":null,\"notes\":null,\"isActive\":true,\"estimatedCalories\":2750,\"proteinGrams\":180.5,\"carbsGrams\":null,\"fatGrams\":72.25}";
        const string wrongLegacyCopy = "{\"id\":\"00000000-0000-0000-0000-000000000101\",\"trainerId\":\"00000000-0000-0000-0000-000000000102\",\"traineeId\":\"00000000-0000-0000-0000-000000000103\",\"createdAt\":\"2026-07-23T08:09:10+02:00\",\"updatedAt\":\"2026-07-24T11:12:13-04:00\",\"meals\":[{\"id\":\"00000000-0000-0000-0000-000000000202\",\"name\":\"Dinner\",\"order\":2,\"description\":null,\"estimatedCalories\":900,\"proteinGrams\":55,\"carbsGrams\":100.5,\"fatGrams\":20.25},{\"id\":\"00000000-0000-0000-0000-000000000201\",\"name\":\"Breakfast\",\"order\":1,\"description\":\"Eggs and oats\",\"estimatedCalories\":null,\"proteinGrams\":null,\"carbsGrams\":60,\"fatGrams\":10}],\"name\":\"Competition plan\",\"startDate\":\"2026-07-23\",\"endDate\":null,\"notes\":null,\"isActive\":true,\"estimatedCalories\":2750,\"proteinGrams\":180.5,\"carbsGrams\":null,\"fatGrams\":72.25}";

        json.Should().Be(expected);
        json.Should().NotBe(wrongLegacyCopy);
    }
}
