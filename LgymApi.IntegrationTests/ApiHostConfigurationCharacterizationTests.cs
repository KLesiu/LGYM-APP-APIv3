using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using FluentAssertions.Execution;
using LgymApi.Api.Extensions;
using LgymApi.Application.Platform.Contracts.Serialization;
using LgymApi.Domain.Security;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Data;
using LgymApi.Resources;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using HttpJsonOptions = Microsoft.AspNetCore.Http.Json.JsonOptions;
using MvcJsonOptions = Microsoft.AspNetCore.Mvc.JsonOptions;

namespace LgymApi.IntegrationTests;

[TestFixture]
[NonParallelizable]
public sealed class ApiHostConfigurationCharacterizationTests
{
    private static readonly IReadOnlyDictionary<string, string> ExpectedPolicies =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [AuthConstants.Policies.AdminAccess] = AuthConstants.Permissions.AdminAccess,
            [AuthConstants.Policies.ManageUserRoles] = AuthConstants.Permissions.ManageUserRoles,
            [AuthConstants.Policies.ManageAppConfig] = AuthConstants.Permissions.ManageAppConfig,
            [AuthConstants.Policies.ManageGlobalExercises] = AuthConstants.Permissions.ManageGlobalExercises,
            [AuthConstants.Policies.TrainerAccess] = AuthConstants.Permissions.TrainerAccess
        };

    private ApiHostFactory _factory = null!;
    private HttpClient _client = null!;

    [OneTimeSetUp]
    public void CreateHost()
    {
        _factory = new ApiHostFactory();
        _client = _factory.CreateClient();
    }

    [OneTimeTearDown]
    public void DisposeHost()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Test]
    public void ControllerAndHttpJsonOptions_PreserveLegacySerializationBehavior()
    {
        var typedId = Id<JsonContractEntity>.New();
        var value = new JsonContractSample(
            "visible",
            null,
            new Dictionary<string, string> { ["PascalKey"] = "entry" },
            typedId,
            JsonContractMode.ExactName);
        var optionSets = new Dictionary<string, JsonSerializerOptions>(StringComparer.Ordinal)
        {
            ["controller"] = _factory.Services.GetRequiredService<IOptions<MvcJsonOptions>>().Value.JsonSerializerOptions,
            ["http"] = _factory.Services.GetRequiredService<IOptions<HttpJsonOptions>>().Value.SerializerOptions
        };

        foreach (var (optionSetName, options) in optionSets)
        {
            var json = JsonSerializer.Serialize(value, options);
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            using (new AssertionScope(optionSetName))
            {
                root.EnumerateObject().Select(property => property.Name).Should().Equal(
                    "displayName",
                    "lookup",
                    "entityId",
                    "mode");
                root.GetProperty("displayName").GetString().Should().Be("visible");
                root.TryGetProperty("omittedValue", out _).Should().BeFalse();
                root.GetProperty("lookup").EnumerateObject().Select(property => property.Name).Should().Equal("pascalKey");
                root.GetProperty("entityId").GetString().Should().Be(typedId.ToString());
                root.GetProperty("mode").GetString().Should().Be(nameof(JsonContractMode.ExactName));
                options.Converters.OfType<TypedIdJsonConverterFactory>().Should().ContainSingle();
                options.Converters.OfType<JsonStringEnumConverter>().Should().ContainSingle();
                options.Invoking(current => JsonSerializer.Deserialize<JsonContractMode>("0", current))
                    .Should().Throw<JsonException>();
            }
        }

        JsonSerializer.Deserialize<JsonContractMode>("0", SharedSerializationOptions.Current)
            .Should().Be(JsonContractMode.ExactName);
    }

    [Test]
    public void StrictHttpJsonRegistration_ProducesEquivalentMvcAndMinimalJsonAndRejectsNumericEnums()
    {
        var typedId = Id<JsonContractEntity>.New();
        var value = new JsonContractSample(
            "visible",
            null,
            new Dictionary<string, string> { ["PascalKey"] = "entry" },
            typedId,
            JsonContractMode.ExactName);
        var services = new ServiceCollection();
        services.AddStrictHttpJsonOptions();

        using var serviceProvider = services.BuildServiceProvider();
        var mvcOptions = serviceProvider.GetRequiredService<IOptions<MvcJsonOptions>>().Value.JsonSerializerOptions;
        var httpOptions = serviceProvider.GetRequiredService<IOptions<HttpJsonOptions>>().Value.SerializerOptions;
        var mvcJson = JsonSerializer.Serialize(value, mvcOptions);
        var httpJson = JsonSerializer.Serialize(value, httpOptions);
        Action deserializeMvcNumericEnum = () => JsonSerializer.Deserialize<JsonContractMode>("0", mvcOptions);
        Action deserializeHttpNumericEnum = () => JsonSerializer.Deserialize<JsonContractMode>("0", httpOptions);
        var persistedEnum = JsonSerializer.Deserialize<JsonContractMode>("0", SharedSerializationOptions.Current);

        Assert.Multiple(() =>
        {
            mvcJson.Should().Be("{\"displayName\":\"visible\",\"lookup\":{\"pascalKey\":\"entry\"},\"entityId\":\"" + typedId + "\",\"mode\":\"ExactName\"}");
            httpJson.Should().Be(mvcJson);
            deserializeMvcNumericEnum.Should().Throw<JsonException>();
            deserializeHttpNumericEnum.Should().Throw<JsonException>();
            persistedEnum.Should().Be(JsonContractMode.ExactName);
        });
    }

    [Test]
    public async Task JwtBearerOptions_PreserveValidationAndHubQueryTokenBehavior()
    {
        var options = GetJwtBearerOptions();
        var validation = options.TokenValidationParameters;

        using (new AssertionScope())
        {
            validation.ValidateIssuer.Should().BeFalse();
            validation.ValidateAudience.Should().BeFalse();
            validation.ValidateIssuerSigningKey.Should().BeTrue();
            validation.ClockSkew.Should().Be(TimeSpan.Zero);
            validation.IssuerSigningKey.Should().BeOfType<SymmetricSecurityKey>()
                .Which.Key.Should().Equal(Encoding.UTF8.GetBytes(ApiHostFactory.JwtSigningKey));
        }

        var hubHttpContext = CreateHttpContext("/hubs/notifications", "?access_token=hub-token");
        var hubContext = new MessageReceivedContext(hubHttpContext, BearerScheme, options);
        await options.Events.MessageReceived(hubContext);
        hubContext.Token.Should().Be("hub-token");

        var apiHttpContext = CreateHttpContext("/api/users", "?access_token=api-token");
        var apiContext = new MessageReceivedContext(apiHttpContext, BearerScheme, options);
        await options.Events.MessageReceived(apiContext);
        apiContext.Token.Should().BeNull();
    }

    [Test]
    public async Task JwtBearerEvents_PreserveLegacyErrorResponses()
    {
        var options = GetJwtBearerOptions();

        var expiredHttpContext = CreateHttpContext("/api/protected");
        var expiredContext = new AuthenticationFailedContext(expiredHttpContext, BearerScheme, options)
        {
            Exception = new SecurityTokenExpiredException()
        };
        await options.Events.AuthenticationFailed(expiredContext);
        await AssertErrorResponseAsync(expiredHttpContext, StatusCodes.Status401Unauthorized, Messages.ExpiredToken);

        var invalidHttpContext = CreateHttpContext("/api/protected");
        var invalidContext = new AuthenticationFailedContext(invalidHttpContext, BearerScheme, options)
        {
            Exception = new SecurityTokenInvalidSignatureException()
        };
        await options.Events.AuthenticationFailed(invalidContext);
        invalidHttpContext.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        invalidHttpContext.Response.Body.Length.Should().Be(0);

        var challengeHttpContext = CreateHttpContext("/api/protected");
        var challengeContext = new JwtBearerChallengeContext(
            challengeHttpContext,
            BearerScheme,
            options,
            new AuthenticationProperties());
        await options.Events.Challenge(challengeContext);
        challengeContext.Handled.Should().BeTrue();
        await AssertErrorResponseAsync(challengeHttpContext, StatusCodes.Status401Unauthorized, Messages.InvalidToken);

        var forbiddenHttpContext = CreateHttpContext("/api/protected");
        var forbiddenContext = new ForbiddenContext(forbiddenHttpContext, BearerScheme, options);
        await options.Events.Forbidden(forbiddenContext);
        await AssertErrorResponseAsync(forbiddenHttpContext, StatusCodes.Status403Forbidden, Messages.Unauthorized);
    }

    [Test]
    public async Task AuthorizationOptions_RegisterTheFivePermissionPolicies()
    {
        var policyProvider = _factory.Services.GetRequiredService<IAuthorizationPolicyProvider>();
        var missingPolicies = await FindMissingPoliciesAsync(policyProvider);

        missingPolicies.Should().BeEmpty();
        ExpectedPolicies.Should().HaveCount(5);

        foreach (var (policyName, permission) in ExpectedPolicies)
        {
            var policy = await policyProvider.GetPolicyAsync(policyName);
            policy.Should().NotBeNull();
            policy!.AuthenticationSchemes.Should().BeEmpty();
            var claimRequirement = policy.Requirements
                .OfType<ClaimsAuthorizationRequirement>()
                .Should().ContainSingle().Subject;
            claimRequirement.ClaimType.Should().Be(AuthConstants.PermissionClaimType);
            claimRequirement.AllowedValues.Should().Equal(permission);
        }
    }

    [Test]
    public async Task MissingPolicyFixture_IsDetected()
    {
        var services = new ServiceCollection();
        var authorization = services.AddAuthorizationBuilder();
        foreach (var (policyName, permission) in ExpectedPolicies
                     .Where(policy => policy.Key != AuthConstants.Policies.TrainerAccess))
        {
            authorization.AddPolicy(policyName, policy =>
                policy.RequireClaim(AuthConstants.PermissionClaimType, permission));
        }

        using var fixtureServices = services.BuildServiceProvider();
        var policyProvider = fixtureServices.GetRequiredService<IAuthorizationPolicyProvider>();

        var missingPolicies = await FindMissingPoliciesAsync(policyProvider);

        missingPolicies.Should().Equal(AuthConstants.Policies.TrainerAccess);
    }

    [Test]
    public async Task RequestLocalization_PreservesEnglishDefaultAndAcceptLanguageOnlySelection()
    {
        var englishMessage = GetMessageForCulture("en", () => Messages.InvalidToken);
        var polishMessage = GetMessageForCulture("pl", () => Messages.InvalidToken);
        englishMessage.Should().NotBe(polishMessage);

        (await GetChallengeMessageAsync()).Should().Be(englishMessage);
        (await GetChallengeMessageAsync(request => request.Headers.AcceptLanguage.ParseAdd("pl"))).Should().Be(polishMessage);
        (await GetChallengeMessageAsync(request => request.Headers.AcceptLanguage.ParseAdd("de"))).Should().Be(englishMessage);
        (await GetChallengeMessageAsync(request => request.RequestUri = new Uri("/api/admin/users/not-a-guid?culture=pl", UriKind.Relative)))
            .Should().Be(englishMessage);
        (await GetChallengeMessageAsync(request => request.Headers.TryAddWithoutValidation(
                "Cookie",
                ".AspNetCore.Culture=c%3Dpl%7Cuic%3Dpl")))
            .Should().Be(englishMessage);
    }

    private JwtBearerOptions GetJwtBearerOptions()
    {
        return _factory.Services
            .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);
    }

    private DefaultHttpContext CreateHttpContext(string path, string? queryString = null)
    {
        var context = new DefaultHttpContext
        {
            RequestServices = _factory.Services
        };
        context.Request.Path = path;
        context.Request.QueryString = new QueryString(queryString ?? string.Empty);
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static async Task AssertErrorResponseAsync(HttpContext context, int statusCode, string message)
    {
        context.Response.StatusCode.Should().Be(statusCode);
        context.Response.Body.Position = 0;
        using var body = await JsonDocument.ParseAsync(context.Response.Body);
        body.RootElement.EnumerateObject().Select(property => property.Name).Should().Equal("msg");
        body.RootElement.GetProperty("msg").GetString().Should().Be(message);
    }

    private static async Task<IReadOnlyList<string>> FindMissingPoliciesAsync(IAuthorizationPolicyProvider policyProvider)
    {
        var missing = new List<string>();
        foreach (var policyName in ExpectedPolicies.Keys)
        {
            if (await policyProvider.GetPolicyAsync(policyName) == null)
            {
                missing.Add(policyName);
            }
        }

        return missing;
    }

    private async Task<string> GetChallengeMessageAsync(Action<HttpRequestMessage>? configure = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/admin/users/not-a-guid");
        configure?.Invoke(request);
        using var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("msg").GetString()!;
    }

    private static string GetMessageForCulture(string cultureName, Func<string> messageAccessor)
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            var culture = CultureInfo.GetCultureInfo(cultureName);
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
            return messageAccessor();
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    private static AuthenticationScheme BearerScheme { get; } = new(
        JwtBearerDefaults.AuthenticationScheme,
        displayName: null,
        typeof(JwtBearerHandler));

    private sealed class ApiHostFactory : WebApplicationFactory<Program>
    {
        internal const string JwtSigningKey = "ApiHostCharacterizationSigningKey_AtLeast32Characters!";

        private readonly InMemoryDatabaseRoot _databaseRoot = new();
        private readonly string _databaseName = $"api_host_characterization_{Id<ApiHostFactory>.New():N}";

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureServices(services =>
            {
                var descriptorsToRemove = services
                    .Where(descriptor => descriptor.ServiceType == typeof(DbContextOptions<AppDbContext>)
                        || descriptor.ServiceType == typeof(AppDbContext)
                        || descriptor.ServiceType.FullName?.Contains("EntityFrameworkCore", StringComparison.Ordinal) == true)
                    .ToList();

                foreach (var descriptor in descriptorsToRemove)
                {
                    services.Remove(descriptor);
                }

                services.AddDbContext<AppDbContext>(options =>
                    options.UseInMemoryDatabase(_databaseName, _databaseRoot));
            });

            builder.UseSetting("Jwt:SigningKey", JwtSigningKey);
            builder.UseSetting("Cors:AllowedOrigins:0", "http://localhost");
            builder.UseSetting("Email:Enabled", "false");
        }
    }

    private sealed class JsonContractEntity;

    private sealed record JsonContractSample(
        string DisplayName,
        string? OmittedValue,
        IReadOnlyDictionary<string, string> Lookup,
        Id<JsonContractEntity> EntityId,
        JsonContractMode Mode);

    private enum JsonContractMode
    {
        ExactName
    }
}
