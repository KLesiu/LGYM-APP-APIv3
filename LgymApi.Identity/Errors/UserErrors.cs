using LgymApi.Application.BuildingBlocks.Errors;

namespace LgymApi.Application.Identity.Errors;

/// <summary>
/// Error indicating the requested user resource was not found (HTTP 404).
/// </summary>
public sealed class UserNotFoundError : NotFoundError
{
    /// <summary>
    /// Initializes a new instance of the <see cref="UserNotFoundError"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public UserNotFoundError(string message) : base(message)
    {
    }
}

/// <summary>
/// Error indicating invalid user data was provided (HTTP 400).
/// </summary>
public sealed class InvalidUserError : BadRequestError
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InvalidUserError"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public InvalidUserError(string message) : base(message)
    {
    }
}

/// <summary>
/// Error indicating authentication failed for user operations (HTTP 401).
/// </summary>
public sealed class UserUnauthorizedError : UnauthorizedError
{
    /// <summary>
    /// Initializes a new instance of the <see cref="UserUnauthorizedError"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public UserUnauthorizedError(string message) : base(message)
    {
    }
}
