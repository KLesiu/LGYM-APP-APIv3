namespace LgymApi.Application.TrainingPlanning.Errors;

/// <summary>
/// Error indicating the requested plan day was not found (HTTP 404).
/// </summary>
public sealed class PlanDayNotFoundError : NotFoundError
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PlanDayNotFoundError"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public PlanDayNotFoundError(string message) : base(message)
    {
    }
}

/// <summary>
/// Error indicating invalid plan day data was provided (HTTP 400).
/// </summary>
public sealed class InvalidPlanDayError : BadRequestError
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InvalidPlanDayError"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public InvalidPlanDayError(string message) : base(message)
    {
    }
}

/// <summary>
/// Error indicating the user does not have permission for plan day operations (HTTP 403).
/// </summary>
public sealed class PlanDayForbiddenError : ForbiddenError
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PlanDayForbiddenError"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public PlanDayForbiddenError(string message) : base(message)
    {
    }
}
