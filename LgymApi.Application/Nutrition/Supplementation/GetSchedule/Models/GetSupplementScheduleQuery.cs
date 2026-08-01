using LgymApi.Domain.ValueObjects;
using UserEntity = LgymApi.Domain.Entities.User;

namespace LgymApi.Application.Nutrition.Supplementation.GetSchedule.Models;

public sealed record GetSupplementScheduleQuery(
    Id<UserEntity> TraineeId,
    DateOnly IntakeDate);
