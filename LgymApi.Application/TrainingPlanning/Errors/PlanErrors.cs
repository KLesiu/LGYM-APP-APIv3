namespace LgymApi.Application.TrainingPlanning.Errors;

/// <summary>
/// Error indicating the requested plan was not found (HTTP 404).
/// </summary>
public sealed class PlanNotFoundError : NotFoundError
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PlanNotFoundError"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public PlanNotFoundError(string message) : base(message)
    {
    }
}

/// <summary>
/// Error indicating invalid plan data was provided (HTTP 400).
/// </summary>
public sealed class InvalidPlanError : BadRequestError
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InvalidPlanError"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public InvalidPlanError(string message) : base(message)
    {
    }
}

/// <summary>
/// Error indicating the user does not have permission for plan operations (HTTP 403).
/// </summary>
public sealed class PlanForbiddenError : ForbiddenError
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PlanForbiddenError"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public PlanForbiddenError(string message) : base(message)
    {
    }
}

/// <summary>
/// Error indicating the user is not authenticated for plan operations (HTTP 401).
/// </summary>
public sealed class PlanUnauthorizedError : UnauthorizedError
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PlanUnauthorizedError"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public PlanUnauthorizedError(string message) : base(message)
    {
    }
}
