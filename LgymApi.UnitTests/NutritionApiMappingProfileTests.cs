using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using LgymApi.Api;
using LgymApi.Api.Features.Trainer.Contracts;
using LgymApi.Application.Mapping;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.Nutrition.DietPlans.Models;
using LgymApi.Application.Nutrition.Supplementation.Models;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using UserEntity = LgymApi.Domain.Entities.User;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class NutritionApiMappingProfileTests
{
    [Test]
    public void Mapper_RegistersEveryNutritionPairExactlyOnce()
    {
        var mapper = CreateMapper();
        var registeredMappings = ((Mapper)mapper).RegisteredMappings;
        var expectedPairs = new (Type Source, Type Target)[]
        {
            (typeof(UpsertDietMealRequest), typeof(DietMealInput)),
            (typeof(UpsertDietPlanRequest), typeof(DietPlanUpsertData)),
            (typeof(UpsertSupplementPlanItemRequest), typeof(SupplementPlanItemInput)),
            (typeof(UpsertSupplementPlanRequest), typeof(SupplementPlanUpsertData)),
            (typeof(DietMealReadModel), typeof(DietMealDto)),
            (typeof(DietPlanReadModel), typeof(DietPlanDto)),
            (typeof(DietPlanHistoryReadModel), typeof(DietPlanHistoryDto)),
            (typeof(SupplementPlanItemReadModel), typeof(SupplementPlanItemDto)),
            (typeof(SupplementPlanReadModel), typeof(SupplementPlanDto)),
            (typeof(SupplementScheduleEntryReadModel), typeof(SupplementScheduleEntryDto)),
            (typeof(SupplementComplianceSummaryReadModel), typeof(SupplementComplianceSummaryDto))
        };

        foreach (var pair in expectedPairs)
        {
            registeredMappings.Count(registered => registered == pair).Should().Be(1);
        }

        registeredMappings.Should().OnlyHaveUniqueItems();
    }

    [Test]
    public void Mapper_MapsDietRequestAndReadModelsWithT2FieldAndOrderGoldens()
    {
        var mapper = CreateMapper();
        var request = new UpsertDietPlanRequest
        {
            Name = "Competition plan",
            StartDate = new DateOnly(2026, 7, 23),
            EndDate = null,
            EstimatedCalories = 2_750,
            ProteinGrams = 180.5m,
            CarbsGrams = null,
            FatGrams = 72.25m,
            Notes = null,
            IsActive = true,
            Meals =
            [
                new UpsertDietMealRequest
                {
                    Name = "Dinner",
                    Order = 2,
                    Description = null,
                    EstimatedCalories = 900,
                    ProteinGrams = 55m,
                    CarbsGrams = 100.5m,
                    FatGrams = 20.25m
                },
                new UpsertDietMealRequest
                {
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
        var readModel = new DietPlanReadModel(
            ParseId<DietPlan>("00000000-0000-0000-0000-000000000101"),
            ParseId<UserEntity>("00000000-0000-0000-0000-000000000102"),
            ParseId<UserEntity>("00000000-0000-0000-0000-000000000103"),
            request.Name,
            request.StartDate,
            request.EndDate,
            request.EstimatedCalories,
            request.ProteinGrams,
            request.CarbsGrams,
            request.FatGrams,
            request.Notes,
            request.IsActive,
            new DateTimeOffset(2026, 7, 23, 8, 9, 10, TimeSpan.FromHours(2)),
            new DateTimeOffset(2026, 7, 24, 11, 12, 13, TimeSpan.FromHours(-4)),
            [
                new DietMealReadModel(ParseId<DietMeal>("00000000-0000-0000-0000-000000000202"), "Dinner", 2, null, 900, 55m, 100.5m, 20.25m),
                new DietMealReadModel(ParseId<DietMeal>("00000000-0000-0000-0000-000000000201"), "Breakfast", 1, "Eggs and oats", null, null, 60m, 10m)
            ]);
        var history = new DietPlanHistoryReadModel(
            ParseId<DietPlanHistory>("00000000-0000-0000-0000-000000000203"),
            readModel.Id,
            readModel.TrainerId,
            readModel.UpdatedAt,
            "Updated",
            "{\"Name\":\"Competition plan\"}");

        var dietMealInput = mapper.Map<UpsertDietMealRequest, DietMealInput>(request.Meals[0]);
        var upsertData = mapper.Map<UpsertDietPlanRequest, DietPlanUpsertData>(request, mapper.CreateContext());
        var dietMeal = mapper.Map<DietMealReadModel, DietMealDto>(readModel.Meals[0]);
        var plan = mapper.Map<DietPlanReadModel, DietPlanDto>(readModel, mapper.CreateContext());
        var historyDto = mapper.Map<DietPlanHistoryReadModel, DietPlanHistoryDto>(history);

        dietMealInput.Should().Be(new DietMealInput("Dinner", 2, null, 900, 55m, 100.5m, 20.25m));
        upsertData.Name.Should().Be(request.Name);
        upsertData.StartDate.Should().Be(request.StartDate);
        upsertData.EndDate.Should().BeNull();
        upsertData.EstimatedCalories.Should().Be(2_750);
        upsertData.ProteinGrams.Should().Be(180.5m);
        upsertData.CarbsGrams.Should().BeNull();
        upsertData.FatGrams.Should().Be(72.25m);
        upsertData.Notes.Should().BeNull();
        upsertData.IsActive.Should().BeTrue();
        upsertData.Meals.Should().Equal(
            new DietMealInput("Dinner", 2, null, 900, 55m, 100.5m, 20.25m),
            new DietMealInput("Breakfast", 1, "Eggs and oats", null, null, 60m, 10m));
        dietMeal.Id.Should().Be("00000000-0000-0000-0000-000000000202");
        JsonSerializer.Serialize(plan).Should().Be("{\"_id\":\"00000000-0000-0000-0000-000000000101\",\"trainerId\":\"00000000-0000-0000-0000-000000000102\",\"traineeId\":\"00000000-0000-0000-0000-000000000103\",\"createdAt\":\"2026-07-23T08:09:10+02:00\",\"updatedAt\":\"2026-07-24T11:12:13-04:00\",\"meals\":[{\"_id\":\"00000000-0000-0000-0000-000000000202\",\"name\":\"Dinner\",\"order\":2,\"description\":null,\"estimatedCalories\":900,\"proteinGrams\":55,\"carbsGrams\":100.5,\"fatGrams\":20.25},{\"_id\":\"00000000-0000-0000-0000-000000000201\",\"name\":\"Breakfast\",\"order\":1,\"description\":\"Eggs and oats\",\"estimatedCalories\":null,\"proteinGrams\":null,\"carbsGrams\":60,\"fatGrams\":10}],\"name\":\"Competition plan\",\"startDate\":\"2026-07-23\",\"endDate\":null,\"notes\":null,\"isActive\":true,\"estimatedCalories\":2750,\"proteinGrams\":180.5,\"carbsGrams\":null,\"fatGrams\":72.25}");
        historyDto.Id.Should().Be("00000000-0000-0000-0000-000000000203");
        historyDto.DietPlanId.Should().Be(plan.Id);
        historyDto.ChangedByUserId.Should().Be(plan.TrainerId);
        historyDto.ChangeDate.Should().Be(plan.UpdatedAt);
        historyDto.ChangeType.Should().Be("Updated");
        historyDto.SnapshotJson.Should().Be("{\"Name\":\"Competition plan\"}");
    }

    [Test]
    public void Mapper_MapsSupplementRequestAndReadModelsWithT3FieldAndOrderGoldens()
    {
        var mapper = CreateMapper();
        var request = new UpsertSupplementPlanRequest
        {
            Name = "Night stack",
            Notes = null,
            Items =
            [
                new UpsertSupplementPlanItemRequest { SupplementName = "Magnesium", Dosage = "400 mg", TimeOfDay = "21:30", DaysOfWeekMask = 127, Order = 0 },
                new UpsertSupplementPlanItemRequest { SupplementName = "Vitamin D", Dosage = "2000 IU", TimeOfDay = "08:00", DaysOfWeekMask = 65, Order = 2 }
            ]
        };
        var readModel = new SupplementPlanReadModel(
            ParseId<SupplementPlan>("00000000-0000-0000-0000-000000000301"),
            ParseId<UserEntity>("00000000-0000-0000-0000-000000000302"),
            ParseId<UserEntity>("00000000-0000-0000-0000-000000000303"),
            request.Name,
            request.Notes,
            true,
            new DateTimeOffset(2026, 7, 1, 6, 30, 0, TimeSpan.Zero),
            [
                new SupplementPlanItemReadModel(ParseId<SupplementPlanItem>("00000000-0000-0000-0000-000000000401"), "Magnesium", "400 mg", "21:30", 127, 0),
                new SupplementPlanItemReadModel(ParseId<SupplementPlanItem>("00000000-0000-0000-0000-000000000402"), "Vitamin D", "2000 IU", "08:00", 65, 2)
            ]);
        var schedule = new SupplementScheduleEntryReadModel(
            readModel.Items[1].Id,
            "Vitamin D",
            "2000 IU",
            "08:00",
            new DateOnly(2026, 7, 3),
            true,
            new DateTimeOffset(2026, 7, 3, 8, 5, 0, TimeSpan.Zero));
        var compliance = new SupplementComplianceSummaryReadModel(
            readModel.TraineeId,
            new DateOnly(2026, 7, 1),
            new DateOnly(2026, 7, 3),
            3,
            1,
            33.33);

        var itemInput = mapper.Map<UpsertSupplementPlanItemRequest, SupplementPlanItemInput>(request.Items[0]);
        var upsertData = mapper.Map<UpsertSupplementPlanRequest, SupplementPlanUpsertData>(request, mapper.CreateContext());
        var item = mapper.Map<SupplementPlanItemReadModel, SupplementPlanItemDto>(readModel.Items[0]);
        var plan = mapper.Map<SupplementPlanReadModel, SupplementPlanDto>(readModel, mapper.CreateContext());
        var scheduleDto = mapper.Map<SupplementScheduleEntryReadModel, SupplementScheduleEntryDto>(schedule);
        var complianceDto = mapper.Map<SupplementComplianceSummaryReadModel, SupplementComplianceSummaryDto>(compliance);

        itemInput.Should().Be(new SupplementPlanItemInput("Magnesium", "400 mg", "21:30", 127, 0));
        upsertData.Name.Should().Be("Night stack");
        upsertData.Notes.Should().BeNull();
        upsertData.Items.Should().Equal(
            new SupplementPlanItemInput("Magnesium", "400 mg", "21:30", 127, 0),
            new SupplementPlanItemInput("Vitamin D", "2000 IU", "08:00", 65, 2));
        item.Id.Should().Be("00000000-0000-0000-0000-000000000401");
        item.DaysOfWeekMask.Should().Be(127);
        JsonSerializer.Serialize(plan).Should().Be("{\"_id\":\"00000000-0000-0000-0000-000000000301\",\"trainerId\":\"00000000-0000-0000-0000-000000000302\",\"traineeId\":\"00000000-0000-0000-0000-000000000303\",\"name\":\"Night stack\",\"notes\":null,\"isActive\":true,\"createdAt\":\"2026-07-01T06:30:00+00:00\",\"items\":[{\"_id\":\"00000000-0000-0000-0000-000000000401\",\"supplementName\":\"Magnesium\",\"dosage\":\"400 mg\",\"timeOfDay\":\"21:30\",\"daysOfWeekMask\":127,\"order\":0},{\"_id\":\"00000000-0000-0000-0000-000000000402\",\"supplementName\":\"Vitamin D\",\"dosage\":\"2000 IU\",\"timeOfDay\":\"08:00\",\"daysOfWeekMask\":65,\"order\":2}]}");
        JsonSerializer.Serialize(scheduleDto).Should().Be("{\"planItemId\":\"00000000-0000-0000-0000-000000000402\",\"supplementName\":\"Vitamin D\",\"dosage\":\"2000 IU\",\"timeOfDay\":\"08:00\",\"intakeDate\":\"2026-07-03\",\"taken\":true,\"takenAt\":\"2026-07-03T08:05:00+00:00\"}");
        JsonSerializer.Serialize(complianceDto).Should().Be("{\"traineeId\":\"00000000-0000-0000-0000-000000000303\",\"fromDate\":\"2026-07-01\",\"toDate\":\"2026-07-03\",\"plannedDoses\":3,\"takenDoses\":1,\"adherenceRate\":33.33}");
    }

    [Test]
    public void Mapper_FailsFastWhenTheNestedDietMealMapIsMissing()
    {
        var mapper = CreateIsolatedMapper(new MissingNestedDietMealMapProfile());
        var source = new DietPlanReadModel(
            ParseId<DietPlan>("00000000-0000-0000-0000-000000000101"),
            ParseId<UserEntity>("00000000-0000-0000-0000-000000000102"),
            ParseId<UserEntity>("00000000-0000-0000-0000-000000000103"),
            "Plan",
            new DateOnly(2026, 7, 1),
            null,
            null,
            null,
            null,
            null,
            null,
            true,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            [new DietMealReadModel(ParseId<DietMeal>("00000000-0000-0000-0000-000000000201"), "Breakfast", 1, null, null, null, null, null)]);

        var action = () => mapper.Map<DietPlanReadModel, DietPlanDto>(source, mapper.CreateContext());

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("Mapping from DietMealReadModel to DietMealDto is not registered.");
    }

    [Test]
    public void NutritionResponseDtoIds_RemainApiStrings()
    {
        var idProperties = new (Type Type, string PropertyName)[]
        {
            (typeof(DietPlanDto), nameof(DietPlanDto.Id)),
            (typeof(DietPlanDto), nameof(DietPlanDto.TrainerId)),
            (typeof(DietPlanDto), nameof(DietPlanDto.TraineeId)),
            (typeof(DietMealDto), nameof(DietMealDto.Id)),
            (typeof(DietPlanHistoryDto), nameof(DietPlanHistoryDto.Id)),
            (typeof(DietPlanHistoryDto), nameof(DietPlanHistoryDto.DietPlanId)),
            (typeof(DietPlanHistoryDto), nameof(DietPlanHistoryDto.ChangedByUserId)),
            (typeof(SupplementPlanDto), nameof(SupplementPlanDto.Id)),
            (typeof(SupplementPlanDto), nameof(SupplementPlanDto.TrainerId)),
            (typeof(SupplementPlanDto), nameof(SupplementPlanDto.TraineeId)),
            (typeof(SupplementPlanItemDto), nameof(SupplementPlanItemDto.Id)),
            (typeof(SupplementScheduleEntryDto), nameof(SupplementScheduleEntryDto.PlanItemId)),
            (typeof(SupplementComplianceSummaryDto), nameof(SupplementComplianceSummaryDto.TraineeId))
        };

        foreach (var (type, propertyName) in idProperties)
        {
            type.GetProperty(propertyName)!.PropertyType.Should().Be(typeof(string));
        }
    }

    private static IMapper CreateMapper()
    {
        var services = new ServiceCollection();
services.AddApplicationMapping(LgymApi.Api.Mapping.MappingAssemblyMarkers.All);

        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IMapper>();
    }

    private static IMapper CreateIsolatedMapper(params IMappingProfile[] profiles)
    {
        return (IMapper)Activator.CreateInstance(
            typeof(Mapper),
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            args: [profiles],
            culture: null)!;
    }

    private static Id<TEntity> ParseId<TEntity>(string value)
    {
        Id<TEntity>.TryParse(value, out var id).Should().BeTrue();
        return id;
    }

    private sealed class MissingNestedDietMealMapProfile : IMappingProfile
    {
        public void Configure(MappingConfiguration configuration)
        {
            configuration.CreateMap<DietPlanReadModel, DietPlanDto>((source, context) => new DietPlanDto
            {
                Meals = context!.MapList<DietMealDto>(source.Meals)
            });
        }
    }
}
