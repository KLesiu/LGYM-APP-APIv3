using LgymApi.Application.BuildingBlocks.Errors;

namespace LgymApi.Application.Identity.Errors;

/// <summary>
/// Error indicating the requested admin user was not found (HTTP 404).
/// </summary>
public sealed class AdminUserNotFoundError : NotFoundError
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AdminUserNotFoundError"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public AdminUserNotFoundError(string message) : base(message)
    {
    }
}

/// <summary>
/// Error indicating invalid admin user data was provided (HTTP 400).
/// </summary>
public sealed class InvalidAdminUserError : BadRequestError
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InvalidAdminUserError"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public InvalidAdminUserError(string message) : base(message)
    {
    }
}
