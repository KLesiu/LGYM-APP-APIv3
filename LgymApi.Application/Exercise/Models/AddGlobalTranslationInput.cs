using LgymApi.Domain.ValueObjects;

namespace LgymApi.Application.Features.Exercise.Models;

public sealed record AddGlobalTranslationInput(
    Id<LgymApi.Identity.Contracts.AccountReference> RouteUserId,
    Id<LgymApi.Domain.Entities.Exercise> ExerciseId,
    string? Culture,
    string? Name);
