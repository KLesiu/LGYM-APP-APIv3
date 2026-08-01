using LgymApi.Domain.Enums;

namespace LgymApi.Application.WorkoutProgress.Dashboard.Models;

public sealed record WorkoutProgressDashboardTrainingReadModel(
    string Id,
    string TypePlanDayId,
    DateTime CreatedAt,
    WorkoutProgressDashboardPlanDayReadModel? PlanDay,
    string? Gym,
    IReadOnlyList<WorkoutProgressDashboardExerciseReadModel> Exercises);

public sealed record WorkoutProgressDashboardPlanDayReadModel(string Id, string Name);

public sealed record WorkoutProgressDashboardExerciseReadModel(
    string ExerciseScoreId,
    WorkoutProgressDashboardExerciseDetailsReadModel ExerciseDetails,
    IReadOnlyList<WorkoutProgressDashboardExerciseScoreReadModel> ScoresDetails);

public sealed record WorkoutProgressDashboardExerciseDetailsReadModel(
    string Id,
    string Name,
    string? UserId,
    BodyParts BodyPart,
    ExerciseEloFormula? EloFormula,
    string? Description,
    string? Image);

public sealed record WorkoutProgressDashboardExerciseScoreReadModel(
    string Id,
    string ExerciseId,
    double Weight,
    WeightUnits Unit,
    double Reps,
    int Series);
