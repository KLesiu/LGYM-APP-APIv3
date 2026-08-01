using LgymApi.Application.BuildingBlocks.Errors;

namespace LgymApi.Application.Platform.ReferenceData.Errors;

/// <summary>
/// Error indicating the requested app configuration was not found (HTTP 404).
/// </summary>
public sealed class AppConfigNotFoundError : NotFoundError
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AppConfigNotFoundError"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public AppConfigNotFoundError(string message) : base(message)
    {
    }
}

/// <summary>
/// Error indicating insufficient permissions for app configuration operations (HTTP 403).
/// </summary>
public sealed class AppConfigForbiddenError : ForbiddenError
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AppConfigForbiddenError"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public AppConfigForbiddenError(string message) : base(message)
    {
    }
}

/// <summary>
/// Error indicating invalid app configuration data was provided (HTTP 400).
/// </summary>
public sealed class InvalidAppConfigError : BadRequestError
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InvalidAppConfigError"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public InvalidAppConfigError(string message) : base(message)
    {
    }
}
