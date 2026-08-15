using LgymApi.E2ETests.Configuration;
using LgymApi.E2ETests.Harness;
using LgymApi.E2ETests.Lifecycle;
using Microsoft.Playwright;

namespace LgymApi.E2ETests.Given;

internal sealed class WebBusinessScenarioState : IWebLifecycleScenarioState, IDisposable
{
    private static readonly IReadOnlyList<PublicTutorialStep> OnboardingSteps =
    [
        PublicTutorialStep.CreateArea,
        PublicTutorialStep.CreateGym,
        PublicTutorialStep.CreatePlan,
        PublicTutorialStep.CreatePlanDay,
        PublicTutorialStep.CreateTraining,
        PublicTutorialStep.LastTreningResult
    ];

    private readonly IReadOnlyList<string> _secretCanaries;
    private HttpClient? _httpClient;
    private PublicHttpGivenClient? _given;

    private WebBusinessScenarioState(SyntheticCredentials credentials, string wrongPassword)
    {
        Credentials = credentials;
        WrongPassword = wrongPassword;
        _secretCanaries = Array.AsReadOnly([
            credentials.Name,
            credentials.Email,
            credentials.Password,
            wrongPassword,
            credentials.RegistrationIdempotencyKey
        ]);
    }

    internal SyntheticCredentials Credentials { get; }

    internal string WrongPassword { get; }

    internal IReadOnlyList<string> SecretCanaries => _secretCanaries;

    internal IPage Page { get; private set; } = null!;

    internal Uri ApiBaseAddress { get; private set; } = null!;

    internal static WebBusinessScenarioState Create()
    {
        var credentials = SyntheticCredentials.Create();
        return new WebBusinessScenarioState(credentials, SyntheticCredentials.CreatePassword());
    }

    public void Attach(IPage page, Uri apiBaseAddress)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(apiBaseAddress);
        if (_given is not null)
        {
            throw new InvalidOperationException("E2E business scenario resources are already attached.");
        }

        var options = E2EConfiguration.Load(TestContext.CurrentContext.TestDirectory, RepositoryRoot.Find());
        Page = page;
        ApiBaseAddress = apiBaseAddress;
        _httpClient = new HttpClient { BaseAddress = apiBaseAddress };
        _given = new PublicHttpGivenClient(
            _httpClient,
            TimeSpan.FromSeconds(options.Timeouts.HttpRequestSeconds));
    }

    internal Task RegisterAsync(CancellationToken cancellationToken) =>
        GetGiven().RegisterAsync(Credentials, cancellationToken);

    internal async Task VerifyInitialActiveOnboardingAsync(CancellationToken cancellationToken)
    {
        using var token = await GetGiven().LoginAsync(Credentials, cancellationToken);
        var tutorials = await GetGiven().GetActiveTutorialsAsync(token, cancellationToken);
        if (tutorials.Count != 1 || tutorials[0].TutorialType != PublicTutorialType.OnboardingDemo ||
            !tutorials[0].RemainingSteps.SequenceEqual([PublicTutorialStep.CreateArea]))
        {
            throw new PublicHttpGivenException("Public HTTP active onboarding state is invalid.");
        }
    }

    internal async Task CompleteOnboardingAsync(CancellationToken cancellationToken)
    {
        using var token = await GetGiven().LoginAsync(Credentials, cancellationToken);
        foreach (var step in OnboardingSteps)
        {
            await GetGiven().CompleteStepAsync(token, PublicTutorialType.OnboardingDemo, step, cancellationToken);
        }

        var tutorials = await GetGiven().GetActiveTutorialsAsync(token, cancellationToken);
        if (tutorials.Any(tutorial => tutorial.TutorialType == PublicTutorialType.OnboardingDemo))
        {
            throw new PublicHttpGivenException("Public HTTP onboarding tutorial remains active.");
        }
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
        _httpClient = null;
        _given = null;
    }

    public override string ToString() => "<web-business-scenario-state>";

    private PublicHttpGivenClient GetGiven() => _given
        ?? throw new InvalidOperationException("E2E business scenario resources are unavailable.");
}
