using LgymApi.Domain.Enums;

namespace LgymApi.Application.WorkoutProgress.Scoring.Elo;

public sealed class StandardExerciseEloCalculator : IExerciseEloCalculator
{
    public ExerciseEloFormula Formula => ExerciseEloFormula.Standard;

    public int Calculate(ExerciseEloCalculationInput input)
        => ExerciseEloCalculationMath.CalculateStandard(input);
}

public sealed class StrengthWeightedExerciseEloCalculator : IExerciseEloCalculator
{
    public ExerciseEloFormula Formula => ExerciseEloFormula.StrengthWeighted;

    public int Calculate(ExerciseEloCalculationInput input)
        => ExerciseEloCalculationMath.CalculateWeighted(input, 0.8, 0.2);
}

public sealed class VolumeWeightedExerciseEloCalculator : IExerciseEloCalculator
{
    public ExerciseEloFormula Formula => ExerciseEloFormula.VolumeWeighted;

    public int Calculate(ExerciseEloCalculationInput input)
        => ExerciseEloCalculationMath.CalculateWeighted(input, 0.2, 0.8);
}

public sealed class PullupWeightedExerciseEloCalculator : IExerciseEloCalculator
{
    public ExerciseEloFormula Formula => ExerciseEloFormula.PullupWeighted;

    public int Calculate(ExerciseEloCalculationInput input)
        => ExerciseEloCalculationMath.CalculateInverseWeighted(input, 0.7, 0.3, 200);
}
