using LgymApi.Application.BuildingBlocks.Errors;

namespace LgymApi.Application.WorkoutProgress.Errors;

/// <summary>
/// Error indicating the requested gym was not found (HTTP 404).
/// </summary>
public sealed class GymNotFoundError : NotFoundError
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GymNotFoundError"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public GymNotFoundError(string message) : base(message)
    {
    }
}

/// <summary>
/// Error indicating invalid gym data was provided (HTTP 400).
/// </summary>
public sealed class InvalidGymError : BadRequestError
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InvalidGymError"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public InvalidGymError(string message) : base(message)
    {
    }
}

/// <summary>
/// Error indicating insufficient permissions for gym operations (HTTP 403).
/// </summary>
public sealed class GymForbiddenError : ForbiddenError
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GymForbiddenError"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public GymForbiddenError(string message) : base(message)
    {
    }
}
