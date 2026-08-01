namespace LgymApi.Application.WorkoutProgress.Scoring.Elo;

public sealed record ExerciseEloCalculationInput(
    double PreviousWeight,
    double PreviousReps,
    double CurrentWeight,
    double CurrentReps);
