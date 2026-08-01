using System.Net;
using System.Text.Json;
using FluentAssertions;

namespace LgymApi.IntegrationTests;

/// <summary>
/// Reusable test harness for validating HTTP response parity between old exception-based 
/// and new Result-pattern control flows.
/// </summary>
public sealed class HttpResponseParityTestHarness
{
    /// <summary>
    /// Asserts that a response represents a successful operation with correct status code.
    /// </summary>
    public void AssertSuccessResponse(
        HttpResponseMessage response,
        HttpStatusCode expectedStatusCode,
        string? because = null)
    {
        response.StatusCode.Should().Be(expectedStatusCode,
            because ?? $"Expected status {expectedStatusCode} for success response");
    }

    /// <summary>
    /// Asserts that a response represents an error with ResponseMessageDto format (msg field).
    /// </summary>
    public async Task AssertErrorMessageResponseAsync(
        HttpResponseMessage response,
        HttpStatusCode expectedStatusCode,
        string? expectedMessageContent = null,
        string? because = null)
    {
        response.StatusCode.Should().Be(expectedStatusCode,
            because ?? $"Expected status {expectedStatusCode} for error response");

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);

        json.RootElement.TryGetProperty("msg", out var msgField)
            .Should().BeTrue("error response must contain 'msg' field in ResponseMessageDto format");

        var message = msgField.GetString();
        message.Should().NotBeNullOrWhiteSpace("error message should not be empty");

        json.RootElement.TryGetProperty("message", out _)
            .Should().BeFalse("response must use 'msg' field, not 'message' (legacy compatibility)");

        if (!string.IsNullOrEmpty(expectedMessageContent))
        {
            message.Should().Contain(expectedMessageContent,
                $"error message should contain '{expectedMessageContent}'");
        }
    }

    /// <summary>
    /// Asserts that a response represents an error with custom payload.
    /// </summary>
    public async Task AssertErrorPayloadResponseAsync(
        HttpResponseMessage response,
        HttpStatusCode expectedStatusCode,
        Action<JsonElement>? payloadAssertions = null,
        string? because = null)
    {
        response.StatusCode.Should().Be(expectedStatusCode,
            because ?? $"Expected status {expectedStatusCode} for error with payload");

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);

        json.RootElement.TryGetProperty("msg", out _)
            .Should().BeFalse("custom payload responses should not be wrapped in ResponseMessageDto format");

        if (payloadAssertions != null)
        {
            payloadAssertions(json.RootElement);
        }
    }

    /// <summary>
    /// Extracts and deserializes the response body as the given type.
    /// </summary>
    public async Task<T?> GetResponseBodyAsync<T>(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrEmpty(content))
        {
            return default;
        }

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        return JsonSerializer.Deserialize<T>(content, options);
    }

    /// <summary>
    /// Extracts the raw JSON document from the response for advanced assertions.
    /// </summary>
    public async Task<JsonDocument> GetResponseJsonAsync(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(content);
    }

    /// <summary>
    /// Asserts that success and error responses maintain consistent JSON field naming conventions.
    /// </summary>
    public async Task AssertResponseParityForErrorMessagesAsync(
        HttpResponseMessage oldResponse,
        HttpResponseMessage newResponse,
        HttpStatusCode expectedStatusCode,
        string? because = null)
    {
        oldResponse.StatusCode.Should().Be(expectedStatusCode,
            because ?? "old endpoint status code should match expectation");
        newResponse.StatusCode.Should().Be(expectedStatusCode,
            because ?? "new endpoint status code should match old endpoint");

        var oldContent = await oldResponse.Content.ReadAsStringAsync();
        var newContent = await newResponse.Content.ReadAsStringAsync();

        var oldJson = JsonDocument.Parse(oldContent);
        var newJson = JsonDocument.Parse(newContent);

        oldJson.RootElement.TryGetProperty("msg", out var oldMsg)
            .Should().BeTrue("old response must have 'msg' field");
        newJson.RootElement.TryGetProperty("msg", out var newMsg)
            .Should().BeTrue("new response must have 'msg' field");

        oldMsg.GetString().Should().NotBeNullOrWhiteSpace("old message should have content");
        newMsg.GetString().Should().NotBeNullOrWhiteSpace("new message should have content");

        oldJson.RootElement.TryGetProperty("message", out _)
            .Should().BeFalse("old response must use 'msg', not 'message'");
        newJson.RootElement.TryGetProperty("message", out _)
            .Should().BeFalse("new response must use 'msg', not 'message'");
    }

    /// <summary>
    /// Asserts that success responses maintain required fields.
    /// </summary>
    public async Task AssertSuccessResponseHasRequiredFieldsAsync(
        HttpResponseMessage successResponse,
        HttpStatusCode expectedStatusCode,
        IEnumerable<string> requiredFieldNames,
        string? because = null)
    {
        successResponse.StatusCode.Should().Be(expectedStatusCode,
            because ?? $"Expected status {expectedStatusCode}");

        var content = await successResponse.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);

        foreach (var fieldName in requiredFieldNames)
        {
            json.RootElement.TryGetProperty(fieldName, out _)
                .Should().BeTrue($"response must have '{fieldName}' field");
        }
    }

    /// <summary>
    /// Compares error message responses to ensure parity.
    /// </summary>
    public void AssertResponseContentParity(
        string oldResponseContent,
        string newResponseContent,
        string? because = null)
    {
        var oldJson = JsonDocument.Parse(oldResponseContent);
        var newJson = JsonDocument.Parse(newResponseContent);

        var oldNormalized = JsonSerializer.Serialize(oldJson.RootElement,
            new JsonSerializerOptions { WriteIndented = false });
        var newNormalized = JsonSerializer.Serialize(newJson.RootElement,
            new JsonSerializerOptions { WriteIndented = false });

        oldNormalized.Should().Be(newNormalized,
            because ?? "error response content should be identical between old and new implementations");
    }
}
