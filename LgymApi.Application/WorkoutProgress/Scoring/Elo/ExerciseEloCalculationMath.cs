namespace LgymApi.Application.WorkoutProgress.Scoring.Elo;

internal static class ExerciseEloCalculationMath
{
    private const int K = 32;

    internal static int CalculateStandard(ExerciseEloCalculationInput input)
        => Calculate(input, GetStandardWeightedScore);

    internal static int CalculateWeighted(ExerciseEloCalculationInput input, double weightRatio, double repsRatio)
        => Calculate(input, (weight, reps) => weight * weightRatio + reps * repsRatio);

    internal static int CalculateInverseWeighted(ExerciseEloCalculationInput input, double weightRatio, double repsRatio, double weightCap)
        => Calculate(input, (weight, reps) => (weightCap - weight) * weightRatio + reps * repsRatio);

    private static int Calculate(
        ExerciseEloCalculationInput input,
        Func<double, double, double> scoreSelector)
    {
        var prevScore = scoreSelector(input.PreviousWeight, input.PreviousReps);
        var accScore = scoreSelector(input.CurrentWeight, input.CurrentReps);
        var toleranceThreshold = input.PreviousWeight > 80 ? 0.1 * prevScore : 0.05 * prevScore;

        var expectedScore = Math.Abs(accScore - prevScore) <= toleranceThreshold
            ? 0.5
            : prevScore / (prevScore + accScore);

        var actualScore = accScore >= prevScore ? 1 : 0;
        var scoreDifference = (actualScore - expectedScore) * (Math.Abs(accScore - prevScore) < toleranceThreshold ? 0.5 : 1);
        var points = K * scoreDifference;

        return (int)Math.Round(points);
    }

    private static double GetStandardWeightedScore(double weight, double reps)
    {
        if (weight <= 15)
        {
            return weight * 0.3 + reps * 0.7;
        }

        if (weight <= 80)
        {
            return weight * 0.5 + reps * 0.5;
        }

        return weight * 0.7 + reps * 0.3;
    }
}
