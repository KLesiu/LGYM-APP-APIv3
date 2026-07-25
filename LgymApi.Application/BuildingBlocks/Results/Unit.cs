namespace LgymApi.Application.BuildingBlocks.Results;

/// <summary>
/// Represents a void result for operations that don't return a value.
/// </summary>
public readonly struct Unit
{
    /// <summary>
    /// Gets the default instance of Unit.
    /// </summary>
    public static Unit Value => default;
}
