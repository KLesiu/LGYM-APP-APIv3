namespace LgymApi.Application.Nutrition.Errors;

/// <summary>
/// Error indicating the requested supplementation resource was not found (HTTP 404).
/// </summary>
public sealed class SupplementationNotFoundError : NotFoundError
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SupplementationNotFoundError"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public SupplementationNotFoundError(string message) : base(message)
    {
    }
}

/// <summary>
/// Error indicating invalid supplementation data was provided (HTTP 400).
/// </summary>
public sealed class InvalidSupplementationError : BadRequestError
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InvalidSupplementationError"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public InvalidSupplementationError(string message) : base(message)
    {
    }
}

/// <summary>
/// Error indicating insufficient permissions for supplementation operations (HTTP 403).
/// </summary>
public sealed class SupplementationForbiddenError : ForbiddenError
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SupplementationForbiddenError"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public SupplementationForbiddenError(string message) : base(message)
    {
    }
}
