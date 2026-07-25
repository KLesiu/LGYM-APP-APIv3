using LgymApi.Application.BuildingBlocks.Errors;

namespace LgymApi.Application.WorkoutProgress.Errors;

/// <summary>
/// Error indicating the requested main record was not found (HTTP 404).
/// </summary>
public sealed class MainRecordsNotFoundError : NotFoundError
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MainRecordsNotFoundError"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public MainRecordsNotFoundError(string message) : base(message)
    {
    }
}

/// <summary>
/// Error indicating invalid main record data was provided (HTTP 400).
/// </summary>
public sealed class InvalidMainRecordsError : BadRequestError
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InvalidMainRecordsError"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public InvalidMainRecordsError(string message) : base(message)
    {
    }
}

/// <summary>
/// Error indicating insufficient permissions for main records operations (HTTP 403).
/// </summary>
public sealed class MainRecordsForbiddenError : ForbiddenError
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MainRecordsForbiddenError"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public MainRecordsForbiddenError(string message) : base(message)
    {
    }
}
