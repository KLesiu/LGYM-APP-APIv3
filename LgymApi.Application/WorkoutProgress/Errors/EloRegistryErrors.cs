using LgymApi.Application.BuildingBlocks.Errors;

namespace LgymApi.Application.WorkoutProgress.Errors;

/// <summary>
/// Error indicating the requested ELO registry data was not found (HTTP 404).
/// </summary>
public sealed class EloRegistryNotFoundError : NotFoundError
{
    /// <summary>
    /// Initializes a new instance of the <see cref="EloRegistryNotFoundError"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public EloRegistryNotFoundError(string message) : base(message)
    {
    }
}

/// <summary>
/// Error indicating invalid ELO registry data was provided (HTTP 400).
/// </summary>
public sealed class InvalidEloRegistryError : BadRequestError
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InvalidEloRegistryError"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public InvalidEloRegistryError(string message) : base(message)
    {
    }
}
