using LgymApi.Application.BuildingBlocks.Errors;

namespace LgymApi.Application.Identity.Errors;

/// <summary>
/// Error indicating the requested role was not found (HTTP 404).
/// </summary>
public sealed class RoleNotFoundError : NotFoundError
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RoleNotFoundError"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public RoleNotFoundError(string message) : base(message)
    {
    }
}

/// <summary>
/// Error indicating insufficient permissions for role operations (HTTP 403).
/// </summary>
public sealed class RoleForbiddenError : ForbiddenError
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RoleForbiddenError"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public RoleForbiddenError(string message) : base(message)
    {
    }
}

/// <summary>
/// Error indicating invalid role data was provided (HTTP 400).
/// </summary>
public sealed class InvalidRoleError : BadRequestError
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InvalidRoleError"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public InvalidRoleError(string message) : base(message)
    {
    }
}

/// <summary>
/// Error indicating a role with that name already exists (HTTP 409).
/// </summary>
public sealed class RoleAlreadyExistsError : ConflictError
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RoleAlreadyExistsError"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public RoleAlreadyExistsError(string message) : base(message)
    {
    }
}
