using LgymApi.Application.BuildingBlocks.Errors;

namespace LgymApi.Application.Platform.ReferenceData.Errors;

/// <summary>
/// Error indicating invalid enumeration data was provided (HTTP 400).
/// </summary>
public sealed class InvalidEnumError : BadRequestError
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InvalidEnumError"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public InvalidEnumError(string message) : base(message)
    {
    }
}
