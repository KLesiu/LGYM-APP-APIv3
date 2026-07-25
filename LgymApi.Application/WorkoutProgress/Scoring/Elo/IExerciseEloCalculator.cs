using LgymApi.Domain.Enums;

namespace LgymApi.Application.WorkoutProgress.Scoring.Elo;

public interface IExerciseEloCalculator
{
    ExerciseEloFormula Formula { get; }

    int Calculate(ExerciseEloCalculationInput input);
}
