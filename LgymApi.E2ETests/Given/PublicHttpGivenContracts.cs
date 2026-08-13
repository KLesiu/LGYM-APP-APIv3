using System.Text.Json.Serialization;

namespace LgymApi.E2ETests.Given;

internal enum PublicTutorialType
{
    Unknown,
    OnboardingDemo
}

internal enum PublicTutorialStep
{
    Unknown,
    CreateArea,
    CreateGym,
    CreatePlan,
    CreatePlanDay,
    CreateTraining,
    LastTreningResult
}

internal sealed record RegisterWireRequest(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("password")] string Password,
    [property: JsonPropertyName("cpassword")] string ConfirmPassword,
    [property: JsonPropertyName("isVisibleInRanking")] bool IsVisibleInRanking);

internal sealed record LoginWireRequest(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("password")] string Password);

internal sealed record LoginWireResponse(
    [property: JsonPropertyName("token")] string Token);

internal sealed record TutorialProgressWireResponse(
    [property: JsonPropertyName("tutorialType")] PublicTutorialType TutorialType,
    [property: JsonPropertyName("remainingSteps")] IReadOnlyList<PublicTutorialStep> RemainingSteps);

internal sealed record CompleteStepWireRequest(
    [property: JsonPropertyName("tutorialType")] PublicTutorialType TutorialType,
    [property: JsonPropertyName("step")] PublicTutorialStep Step);

internal sealed class SyntheticCredentials
{
    private SyntheticCredentials(
        string name,
        string email,
        string password,
        string registrationIdempotencyKey)
    {
        Name = name;
        Email = email;
        Password = password;
        RegistrationIdempotencyKey = registrationIdempotencyKey;
    }

    internal string Name { get; }

    internal string Email { get; }

    internal string Password { get; }

    internal bool IsVisibleInRanking => true;

    internal string RegistrationIdempotencyKey { get; }

    internal static SyntheticCredentials Create()
    {
        var identity = Guid.NewGuid().ToString("N");
        return new SyntheticCredentials(
            $"e2e-{identity}",
            $"e2e-{identity}@example.invalid",
            $"E2e!{Guid.NewGuid():N}",
            Guid.NewGuid().ToString("N"));
    }

    public override string ToString() => "<synthetic-credentials>";
}

internal sealed class InMemoryBearerToken : IDisposable
{
    private string? _value;

    private InMemoryBearerToken(string value)
    {
        _value = value;
    }

    internal static InMemoryBearerToken Create(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return new InMemoryBearerToken(value);
    }

    internal string GetValue() => _value ?? throw new ObjectDisposedException(nameof(InMemoryBearerToken));

    public void Dispose() => _value = null;

    public override string ToString() => "<redacted-bearer-token>";
}

internal sealed class PublicHttpGivenException(string message) : Exception(message);
