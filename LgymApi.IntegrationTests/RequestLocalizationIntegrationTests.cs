using System.Globalization;
using System.Net;
using System.Text.Json;
using FluentAssertions;
using LgymApi.Resources;

namespace LgymApi.IntegrationTests;

[TestFixture]
[NonParallelizable]
public sealed class RequestLocalizationIntegrationTests : IntegrationTestBase
{
    [Test]
    public async Task AuthenticationChallenge_UsesAcceptLanguageAndDoesNotLeakCultureAcrossRequests()
    {
        var englishMessage = GetMessageForCulture("en");
        var polishMessage = GetMessageForCulture("pl");
        englishMessage.Should().NotBe(polishMessage);

        (await GetChallengeMessageAsync()).Should().Be(englishMessage);
        (await GetChallengeMessageAsync(request => request.Headers.AcceptLanguage.ParseAdd("pl"))).Should().Be(polishMessage);
        (await GetChallengeMessageAsync()).Should().Be(englishMessage);
    }

    [TestCase("de")]
    [TestCase("pl;q=0.1, en;q=0.9")]
    public async Task AuthenticationChallenge_FallsBackToEnglishForUnsupportedOrLowerPriorityAcceptLanguage(string acceptLanguage)
    {
        var englishMessage = GetMessageForCulture("en");

        var message = await GetChallengeMessageAsync(request => request.Headers.AcceptLanguage.ParseAdd(acceptLanguage));

        message.Should().Be(englishMessage);
    }

    [Test]
    public async Task AuthenticationChallenge_IgnoresQueryStringAndCookieCultureProviders()
    {
        var englishMessage = GetMessageForCulture("en");

        var message = await GetChallengeMessageAsync(request =>
        {
            request.RequestUri = new Uri("/api/admin/users/not-a-guid?culture=pl", UriKind.Relative);
            request.Headers.TryAddWithoutValidation("Cookie", ".AspNetCore.Culture=c%3Dpl%7Cuic%3Dpl");
        });

        message.Should().Be(englishMessage);
    }

    private async Task<string> GetChallengeMessageAsync(Action<HttpRequestMessage>? configure = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/admin/users/not-a-guid");
        configure?.Invoke(request);
        using var response = await Client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("msg").GetString()!;
    }

    private static string GetMessageForCulture(string cultureName)
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            var culture = CultureInfo.GetCultureInfo(cultureName);
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
            return Messages.InvalidToken;
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }
}
