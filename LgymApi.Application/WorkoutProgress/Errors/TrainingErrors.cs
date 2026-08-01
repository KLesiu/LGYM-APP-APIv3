using LgymApi.Application.BuildingBlocks.Errors;

namespace LgymApi.Application.WorkoutProgress.Errors;

/// <summary>
/// Error indicating the requested training was not found (HTTP 404).
/// </summary>
public sealed class TrainingNotFoundError : NotFoundError
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TrainingNotFoundError"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public TrainingNotFoundError(string message) : base(message)
    {
    }
}

/// <summary>
/// Error indicating invalid training data was provided (HTTP 400).
/// </summary>
public sealed class InvalidTrainingDataError : BadRequestError
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InvalidTrainingDataError"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public InvalidTrainingDataError(string message) : base(message)
    {
    }
}
