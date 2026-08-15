using Reqnroll;

namespace LgymApi.E2ETests.Given;

[Binding]
public sealed class WebBusinessGivenBindings
{
    private readonly ScenarioContext _scenarioContext;

    public WebBusinessGivenBindings(ScenarioContext scenarioContext)
    {
        _scenarioContext = scenarioContext;
    }

    [Given("a registered public HTTP account")]
    public Task GivenARegisteredPublicHttpAccountAsync() => State.RegisterAsync(CancellationToken.None);

    [Given("an active public HTTP onboarding tutorial")]
    public async Task GivenAnActivePublicHttpOnboardingTutorialAsync()
    {
        await State.RegisterAsync(CancellationToken.None);
        await State.VerifyInitialActiveOnboardingAsync(CancellationToken.None);
    }

    [Given("a public HTTP account with completed onboarding")]
    public async Task GivenAPublicHttpAccountWithCompletedOnboardingAsync()
    {
        await State.RegisterAsync(CancellationToken.None);
        await State.CompleteOnboardingAsync(CancellationToken.None);
    }

    private WebBusinessScenarioState State => _scenarioContext.Get<WebBusinessScenarioState>();
}
