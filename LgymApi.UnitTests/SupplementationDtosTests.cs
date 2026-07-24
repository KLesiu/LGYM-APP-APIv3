using System.Text.Json;
using FluentAssertions;
using LgymApi.Api.Features.Trainer.Contracts;
using NUnit.Framework;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class SupplementationDtosTests
{
    [Test]
    public void UpsertSupplementPlanRequest_SerializesExactGoldenJson()
    {
        var request = new UpsertSupplementPlanRequest
        {
            Name = "Daily supplements",
            Notes = "Before sleep",
            Items =
            [
                new UpsertSupplementPlanItemRequest
                {
                    SupplementName = "Magnesium",
                    Dosage = "400 mg",
                    TimeOfDay = "21:30",
                    DaysOfWeekMask = 65,
                    Order = 2
                }
            ]
        };

        JsonSerializer.Serialize(request).Should().Be(
            "{\"name\":\"Daily supplements\",\"notes\":\"Before sleep\",\"items\":[{\"supplementName\":\"Magnesium\",\"dosage\":\"400 mg\",\"timeOfDay\":\"21:30\",\"daysOfWeekMask\":65,\"order\":2}]}");
    }

    [Test]
    public void SupplementPlanDto_SerializesExactGoldenJson()
    {
        var dto = new SupplementPlanDto
        {
            Id = "plan-1",
            TrainerId = "trainer-1",
            TraineeId = "trainee-1",
            Name = "Night stack",
            Notes = null,
            IsActive = true,
            CreatedAt = new DateTimeOffset(2026, 7, 1, 6, 30, 0, TimeSpan.Zero),
            Items =
            [
                new SupplementPlanItemDto
                {
                    Id = "item-1",
                    SupplementName = "Magnesium",
                    Dosage = "400 mg",
                    TimeOfDay = "21:30",
                    DaysOfWeekMask = 127,
                    Order = 0
                }
            ]
        };

        JsonSerializer.Serialize(dto).Should().Be(
            "{\"_id\":\"plan-1\",\"trainerId\":\"trainer-1\",\"traineeId\":\"trainee-1\",\"name\":\"Night stack\",\"notes\":null,\"isActive\":true,\"createdAt\":\"2026-07-01T06:30:00+00:00\",\"items\":[{\"_id\":\"item-1\",\"supplementName\":\"Magnesium\",\"dosage\":\"400 mg\",\"timeOfDay\":\"21:30\",\"daysOfWeekMask\":127,\"order\":0}]}"
        );
    }

    [Test]
    public void CheckOffSupplementIntakeRequest_SerializesExactGoldenJson()
    {
        var request = new CheckOffSupplementIntakeRequest
        {
            PlanItemId = "item-1",
            IntakeDate = new DateOnly(2026, 7, 2),
            TakenAt = null
        };

        JsonSerializer.Serialize(request).Should().Be(
            "{\"planItemId\":\"item-1\",\"intakeDate\":\"2026-07-02\",\"takenAt\":null}");
    }

    [Test]
    public void SupplementScheduleAndComplianceDtos_SerializeExactGoldenJson()
    {
        var schedule = new SupplementScheduleEntryDto
        {
            PlanItemId = "item-1",
            SupplementName = "Vitamin D",
            Dosage = "2000 IU",
            TimeOfDay = "08:00",
            IntakeDate = new DateOnly(2026, 7, 3),
            Taken = true,
            TakenAt = new DateTimeOffset(2026, 7, 3, 8, 5, 0, TimeSpan.Zero)
        };
        var compliance = new SupplementComplianceSummaryDto
        {
            TraineeId = "trainee-1",
            FromDate = new DateOnly(2026, 7, 1),
            ToDate = new DateOnly(2026, 7, 3),
            PlannedDoses = 3,
            TakenDoses = 1,
            AdherenceRate = 33.33
        };

        JsonSerializer.Serialize(schedule).Should().Be(
            "{\"planItemId\":\"item-1\",\"supplementName\":\"Vitamin D\",\"dosage\":\"2000 IU\",\"timeOfDay\":\"08:00\",\"intakeDate\":\"2026-07-03\",\"taken\":true,\"takenAt\":\"2026-07-03T08:05:00+00:00\"}");
        JsonSerializer.Serialize(compliance).Should().Be(
            "{\"traineeId\":\"trainee-1\",\"fromDate\":\"2026-07-01\",\"toDate\":\"2026-07-03\",\"plannedDoses\":3,\"takenDoses\":1,\"adherenceRate\":33.33}");
    }
}
