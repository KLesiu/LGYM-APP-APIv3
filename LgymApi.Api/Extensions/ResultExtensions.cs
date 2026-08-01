using LgymApi.Api.Features.Common.Contracts;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using Microsoft.AspNetCore.Mvc;

using Unit = LgymApi.Application.BuildingBlocks.Results.Unit;

namespace LgymApi.Api.Extensions;

/// <summary>
/// Extension methods for converting Result types to ASP.NET Core IActionResult.
/// </summary>
public static class ResultExtensions
{
    /// <summary>
    /// Converts a Result&lt;T, TError&gt; to an IActionResult.
    /// Success results return 200 OK with the result value.
    /// Failure results return the HTTP status code from the error with either the error payload or a response message DTO.
    /// </summary>
    /// <typeparam name="T">The type of the result value.</typeparam>
    /// <typeparam name="TError">The type of the error, must be AppError or derived.</typeparam>
    /// <param name="result">The result to convert.</param>
    /// <returns>An IActionResult representing either success or failure.</returns>
    public static IActionResult ToActionResult<T, TError>(this Result<T, TError> result)
        where TError : AppError
    {
        if (result.IsSuccess)
        {
            return new OkObjectResult(result.Value);
        }

        var error = result.Error;
        var payload = error.GetPayload();

        if (payload != null)
        {
            return new ObjectResult(payload)
            {
                StatusCode = error.HttpStatusCode
            };
        }

        var responseDto = new ResponseMessageDto { Message = error.Message };
        return new ObjectResult(responseDto)
        {
            StatusCode = error.HttpStatusCode
        };
    }

    /// <summary>
    /// Converts a Result&lt;Unit, TError&gt; to an IActionResult.
    /// Success results return 200 OK with no content.
    /// Failure results return the HTTP status code from the error with either the error payload or a response message DTO.
    /// </summary>
    /// <typeparam name="TError">The type of the error, must be AppError or derived.</typeparam>
    /// <param name="result">The result to convert.</param>
    /// <returns>An IActionResult representing either success or failure.</returns>
    public static IActionResult ToActionResult<TError>(this Result<Unit, TError> result)
        where TError : AppError
    {
        if (result.IsSuccess)
        {
            return new OkResult();
        }

        var error = result.Error;
        var payload = error.GetPayload();

        if (payload != null)
        {
            return new ObjectResult(payload)
            {
                StatusCode = error.HttpStatusCode
            };
        }

        var responseDto = new ResponseMessageDto { Message = error.Message };
        return new ObjectResult(responseDto)
        {
            StatusCode = error.HttpStatusCode
        };
    }
}
