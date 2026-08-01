using System.Globalization;
using System.Net;
using System.Net.Http.Json;
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

    [Test]
    public async Task AppConfigUnknownEnum_UsesPolishValidatorResourceInCompatibilityBody()
    {
        var polishMessage = GetMessageForCulture("pl", () => Messages.FieldRequired);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/appConfig/getAppVersion")
        {
            Content = JsonContent.Create(new { platform = "Unknown" })
        };
        request.Headers.AcceptLanguage.ParseAdd("pl");

        using var response = await Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("errors").GetProperty("platform")[0].GetString().Should().Be(polishMessage);
        body.RootElement.TryGetProperty("msg", out _).Should().BeFalse();
        body.RootElement.TryGetProperty("message", out _).Should().BeFalse();
    }

    [Test]
    public async Task AppConfigMalformedEnum_PreservesStrictBadRequestCompatibilityBody()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/appConfig/getAppVersion")
        {
            Content = JsonContent.Create(new { platform = "InvalidPlatform" })
        };

        using var response = await Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("status").GetInt32().Should().Be(400);
        var platformErrors = body.RootElement.GetProperty("errors").GetProperty("$.platform");
        platformErrors.GetArrayLength().Should().Be(1);
        platformErrors[0].GetString().Should().Contain("could not be converted to LgymApi.Domain.Enums.Platforms");
        body.RootElement.TryGetProperty("msg", out _).Should().BeFalse();
        body.RootElement.TryGetProperty("message", out _).Should().BeFalse();
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
        return GetMessageForCulture(cultureName, () => Messages.InvalidToken);
    }

    private static string GetMessageForCulture(string cultureName, Func<string> getMessage)
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            var culture = CultureInfo.GetCultureInfo(cultureName);
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
            return getMessage();
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }
}
