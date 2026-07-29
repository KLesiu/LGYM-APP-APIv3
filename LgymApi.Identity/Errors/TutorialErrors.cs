using LgymApi.Application.BuildingBlocks.Errors;

namespace LgymApi.Application.Identity.Errors;

/// <summary>
/// Error indicating an invalid user ID was provided (HTTP 400).
/// </summary>
public sealed class InvalidUserIdError : BadRequestError
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InvalidUserIdError"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public InvalidUserIdError(string message) : base(message)
    {
    }
}

/// <summary>
/// Error indicating an invalid tutorial type was provided (HTTP 400).
/// </summary>
public sealed class InvalidTutorialTypeError : BadRequestError
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InvalidTutorialTypeError"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public InvalidTutorialTypeError(string message) : base(message)
    {
    }
}

/// <summary>
/// Error indicating an invalid tutorial step was provided (HTTP 400).
/// </summary>
public sealed class InvalidTutorialStepError : BadRequestError
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InvalidTutorialStepError"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public InvalidTutorialStepError(string message) : base(message)
    {
    }
}

/// <summary>
/// Error indicating the requested tutorial progress was not found (HTTP 404).
/// </summary>
public sealed class TutorialProgressNotFoundError : NotFoundError
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TutorialProgressNotFoundError"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public TutorialProgressNotFoundError(string message) : base(message)
    {
    }
}
