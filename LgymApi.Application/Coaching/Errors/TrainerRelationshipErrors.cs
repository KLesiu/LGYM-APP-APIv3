namespace LgymApi.Application.Coaching.Errors;

/// <summary>
/// Error indicating the requested trainer relationship was not found (HTTP 404).
/// </summary>
public sealed class TrainerRelationshipNotFoundError : NotFoundError
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TrainerRelationshipNotFoundError"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public TrainerRelationshipNotFoundError(string message) : base(message)
    {
    }
}

/// <summary>
/// Error indicating invalid trainer relationship data was provided (HTTP 400).
/// </summary>
public sealed class InvalidTrainerRelationshipError : BadRequestError
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InvalidTrainerRelationshipError"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public InvalidTrainerRelationshipError(string message) : base(message)
    {
    }
}

/// <summary>
/// Error indicating a trainer relationship conflict (HTTP 409).
/// </summary>
public sealed class TrainerRelationshipConflictError : ConflictError
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TrainerRelationshipConflictError"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public TrainerRelationshipConflictError(string message) : base(message)
    {
    }
}

/// <summary>
/// Error indicating the user does not have permission for trainer relationship operations (HTTP 403).
/// </summary>
public sealed class TrainerRelationshipForbiddenError : ForbiddenError
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TrainerRelationshipForbiddenError"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public TrainerRelationshipForbiddenError(string message) : base(message)
    {
    }
}
