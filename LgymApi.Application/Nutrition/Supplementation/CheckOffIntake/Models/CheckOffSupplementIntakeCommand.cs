using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using UserEntity = LgymApi.Domain.Entities.User;

namespace LgymApi.Application.Nutrition.Supplementation.CheckOffIntake.Models;

public sealed record CheckOffSupplementIntakeCommand(
    Id<UserEntity> TraineeId,
    Id<SupplementPlanItem> PlanItemId,
    DateOnly IntakeDate,
    DateTimeOffset? TakenAt);
