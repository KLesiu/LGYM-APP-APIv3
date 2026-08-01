using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;

namespace LgymApi.Application.Features.Exercise.Models;

public sealed record AddUserExerciseInput(
    Id<LgymApi.Identity.Contracts.AccountReference> UserId,
    string Name,
    BodyParts BodyPart,
    string? Description,
    string? Image);

public sealed record AddUserExerciseWithFormulaInput(
    Id<LgymApi.Identity.Contracts.AccountReference> UserId,
    string Name,
    BodyParts BodyPart,
    ExerciseEloFormula? EloFormula,
    string? Description,
    string? Image);

public sealed record AddExerciseWithFormulaInput(
    string Name,
    BodyParts BodyPart,
    ExerciseEloFormula? EloFormula,
    string? Description,
    string? Image);
