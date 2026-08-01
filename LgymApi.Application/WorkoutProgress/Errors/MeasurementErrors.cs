using LgymApi.Application.BuildingBlocks.Errors;

namespace LgymApi.Application.WorkoutProgress.Errors;

/// <summary>
/// Error indicating the requested measurement was not found (HTTP 404).
/// </summary>
public sealed class MeasurementNotFoundError : NotFoundError
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MeasurementNotFoundError"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public MeasurementNotFoundError(string message) : base(message)
    {
    }
}

/// <summary>
/// Error indicating insufficient permissions for measurement operations (HTTP 403).
/// </summary>
public sealed class MeasurementForbiddenError : ForbiddenError
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MeasurementForbiddenError"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public MeasurementForbiddenError(string message) : base(message)
    {
    }
}

/// <summary>
/// Error indicating invalid measurement data was provided (HTTP 400).
/// </summary>
public sealed class InvalidMeasurementError : BadRequestError
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InvalidMeasurementError"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public InvalidMeasurementError(string message) : base(message)
    {
    }
}
