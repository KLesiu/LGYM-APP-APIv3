using LgymApi.Domain.ValueObjects;

namespace LgymApi.Application.Features.Exercise.Models;

public sealed record GetLastExerciseScoresInput(
    Id<LgymApi.Identity.Contracts.AccountReference> RouteUserId,
    Id<LgymApi.Identity.Contracts.AccountReference> CurrentUserId,
    Id<LgymApi.Domain.Entities.Exercise> ExerciseId,
    int Series,
    Id<LgymApi.Domain.Entities.Gym>? GymId,
    string ExerciseName);
