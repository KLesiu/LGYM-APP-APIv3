using LgymApi.Application.BuildingBlocks.Errors;

namespace LgymApi.Application.WorkoutProgress.Errors;

/// <summary>
/// Error indicating the requested exercise was not found (HTTP 404).
/// </summary>
public sealed class ExerciseNotFoundError : NotFoundError
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ExerciseNotFoundError"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public ExerciseNotFoundError(string message) : base(message)
    {
    }
}

/// <summary>
/// Error indicating invalid exercise data was provided (HTTP 400).
/// </summary>
public sealed class InvalidExerciseError : BadRequestError
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InvalidExerciseError"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public InvalidExerciseError(string message) : base(message)
    {
    }
}

/// <summary>
/// Error indicating the user does not have permission for exercise operations (HTTP 403).
/// </summary>
public sealed class ExerciseForbiddenError : ForbiddenError
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ExerciseForbiddenError"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public ExerciseForbiddenError(string message) : base(message)
    {
    }
}
