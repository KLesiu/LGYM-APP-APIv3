using LgymApi.Application.BuildingBlocks.Errors;

namespace LgymApi.Application.Reporting.Errors;

/// <summary>
/// Error indicating the requested reporting resource was not found (HTTP 404).
/// </summary>
public sealed class ReportingNotFoundError : NotFoundError
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ReportingNotFoundError"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public ReportingNotFoundError(string message) : base(message)
    {
    }
}

/// <summary>
/// Error indicating invalid reporting data was provided (HTTP 400).
/// </summary>
public sealed class InvalidReportingError : BadRequestError
{
    private readonly int _httpStatusCode;

    /// <summary>
    /// Initializes a new instance of the <see cref="InvalidReportingError"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public InvalidReportingError(string message, int httpStatusCode = 400, object? payload = null) : base(message, payload)
    {
        _httpStatusCode = httpStatusCode;
    }

    public override int HttpStatusCode => _httpStatusCode;
}

/// <summary>
/// Error indicating insufficient permissions for reporting operations (HTTP 403).
/// </summary>
public sealed class ReportingForbiddenError : ForbiddenError
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ReportingForbiddenError"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public ReportingForbiddenError(string message) : base(message)
    {
    }
}

/// <summary>
/// Error indicating a conflict with the current reporting state (HTTP 409).
/// </summary>
public sealed class ReportingConflictError : ConflictError
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ReportingConflictError"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public ReportingConflictError(string message) : base(message)
    {
    }
}
