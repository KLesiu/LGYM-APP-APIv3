using LgymApi.Application.BuildingBlocks.Errors;

namespace LgymApi.Application.WorkoutProgress.Errors;

/// <summary>
/// Error indicating the requested exercise score was not found (HTTP 404).
/// </summary>
public sealed class ExerciseScoreNotFoundError : NotFoundError
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ExerciseScoreNotFoundError"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public ExerciseScoreNotFoundError(string message) : base(message)
    {
    }
}

/// <summary>
/// Error indicating invalid exercise score data was provided (HTTP 400).
/// </summary>
public sealed class InvalidExerciseScoreError : BadRequestError
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InvalidExerciseScoreError"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public InvalidExerciseScoreError(string message) : base(message)
    {
    }
}
