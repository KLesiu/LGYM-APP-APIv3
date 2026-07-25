namespace LgymApi.Application.BuildingBlocks.Results;

/// <summary>
/// Generic result type representing success or failure with typed values and errors.
/// </summary>
/// <typeparam name="T">The type of value on success.</typeparam>
/// <typeparam name="TError">The type of error on failure.</typeparam>
public readonly record struct Result<T, TError>
{
    private readonly T? _value;
    private readonly TError? _error;

    private Result(T value)
    {
        _value = value;
        _error = default;
        IsSuccess = true;
    }

    private Result(TError error)
    {
        _value = default;
        _error = error;
        IsSuccess = false;
    }

    /// <summary>
    /// Gets a value indicating whether the result represents a successful outcome.
    /// </summary>
    public bool IsSuccess { get; }

    /// <summary>
    /// Gets a value indicating whether the result represents a failed outcome.
    /// </summary>
    public bool IsFailure => !IsSuccess;

    /// <summary>
    /// Gets the value of the result. Throws if the result represents failure.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when the result is a failure.</exception>
    public T Value
    {
        get
        {
            if (IsFailure)
            {
                throw new InvalidOperationException("Cannot access Value of a failed result.");
            }

            return _value!;
        }
    }

    /// <summary>
    /// Gets the error of the result. Throws if the result represents success.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when the result is a success.</exception>
    public TError Error
    {
        get
        {
            if (IsSuccess)
            {
                throw new InvalidOperationException("Cannot access Error of a successful result.");
            }

            return _error!;
        }
    }

    /// <summary>
    /// Creates a successful result with the specified value.
    /// </summary>
    /// <param name="value">The success value.</param>
    /// <returns>A successful result.</returns>
    public static Result<T, TError> Success(T value) => new(value);

    /// <summary>
    /// Creates a failed result with the specified error.
    /// </summary>
    /// <param name="error">The error.</param>
    /// <returns>A failed result.</returns>
    public static Result<T, TError> Failure(TError error) => new(error);
}

/// <summary>
/// Helper static class for creating Result instances without type parameter specification.
/// </summary>
public static class Result
{
    /// <summary>
    /// Creates a successful result with the specified value.
    /// </summary>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <typeparam name="TError">The type of the error.</typeparam>
    /// <param name="value">The success value.</param>
    /// <returns>A successful result.</returns>
    public static Result<T, TError> Success<T, TError>(T value) => Result<T, TError>.Success(value);

    /// <summary>
    /// Creates a failed result with the specified error.
    /// </summary>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <typeparam name="TError">The type of the error.</typeparam>
    /// <param name="error">The error.</param>
    /// <returns>A failed result.</returns>
    public static Result<T, TError> Failure<T, TError>(TError error) => Result<T, TError>.Failure(error);
}
