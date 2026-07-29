using System.Net;
using System.Net.Http;
using System.Reflection;
using FluentAssertions;
using Google.Apis.Auth;
using LgymApi.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NUnit.Framework;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class GoogleTokenValidatorTests
{
    [Test]
    public void ValidateAsync_WhenClientIdMissing_ThrowsInvalidOperationException()
    {
        var validator = CreateValidator(new ConfigurationBuilder().Build(), new StubHttpMessageHandler(_ => throw new NotSupportedException()));

        var action = async () => await validator.ValidateAsync("token", null, CancellationToken.None);

        action.Should().ThrowAsync<InvalidOperationException>().WithMessage("*GoogleAuth:ClientId*");
    }

    [Test]
    public async Task ValidateAsync_WhenIdTokenInvalid_ReturnsNull()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["GoogleAuth:ClientId"] = "client-id" })
            .Build();
        var validator = CreateValidator(configuration, new StubHttpMessageHandler(_ => throw new NotSupportedException()));

        var result = await validator.ValidateAsync("definitely-not-a-google-jwt", null, CancellationToken.None);

        result.Should().BeNull();
    }

    [Test]
    public async Task ResolveUserInfoAsync_WhenPayloadAlreadyContainsEmail_UsesPayloadWithoutHttpCall()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var validator = CreateValidator(DefaultConfiguration(), handler);
        var payload = new GoogleJsonWebSignature.Payload
        {
            Subject = "subject-1",
            Email = "user@example.com",
            EmailVerified = true,
            Name = "User",
            Picture = "pic"
        };

        var result = await InvokeResolveUserInfoAsync(validator, payload, "access-token");

        GetProperty<string?>(result, "Email").Should().Be("user@example.com");
        GetProperty<bool>(result, "EmailVerified").Should().BeTrue();
        handler.CallCount.Should().Be(0);
    }

    [Test]
    public async Task ResolveUserInfoAsync_WhenAccessTokenMissing_ReturnsEmpty()
    {
        var validator = CreateValidator(DefaultConfiguration(), new StubHttpMessageHandler(_ => throw new NotSupportedException()));
        var payload = new GoogleJsonWebSignature.Payload { Subject = "subject-1" };

        var result = await InvokeResolveUserInfoAsync(validator, payload, null);

        GetProperty<string?>(result, "Email").Should().BeNull();
        GetProperty<bool>(result, "EmailVerified").Should().BeFalse();
    }

    [Test]
    public async Task ResolveUserInfoAsync_WhenUserInfoMatchesSubject_ReturnsTrimmedEnrichedPayload()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""
                {
                  "sub": "subject-1",
                  "email": "  user@example.com  ",
                  "email_verified": true,
                  "name": "Google User",
                  "picture": "avatar"
                }
                """)
        });
        var validator = CreateValidator(DefaultConfiguration(), handler);
        var payload = new GoogleJsonWebSignature.Payload
        {
            Subject = "subject-1",
            Name = "Fallback Name",
            Picture = "fallback-picture"
        };

        var result = await InvokeResolveUserInfoAsync(validator, payload, "access-token");

        GetProperty<string?>(result, "Email").Should().Be("user@example.com");
        GetProperty<bool>(result, "EmailVerified").Should().BeTrue();
        GetProperty<string?>(result, "Name").Should().Be("Google User");
        GetProperty<string?>(result, "Picture").Should().Be("avatar");
        handler.LastRequest!.Headers.Authorization!.Scheme.Should().Be("Bearer");
    }

    [Test]
    public async Task ResolveUserInfoAsync_WhenSubjectDoesNotMatch_ReturnsEmpty()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{" + "\"sub\":\"other-subject\",\"email\":\"user@example.com\"}")
        });
        var validator = CreateValidator(DefaultConfiguration(), handler);
        var payload = new GoogleJsonWebSignature.Payload { Subject = "subject-1" };

        var result = await InvokeResolveUserInfoAsync(validator, payload, "access-token");

        GetProperty<string?>(result, "Email").Should().BeNull();
    }

    [Test]
    public async Task ResolveUserInfoAsync_WhenUserInfoRequestFails_ReturnsEmpty()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var validator = CreateValidator(DefaultConfiguration(), handler);
        var payload = new GoogleJsonWebSignature.Payload { Subject = "subject-1" };

        var result = await InvokeResolveUserInfoAsync(validator, payload, "access-token");

        GetProperty<string?>(result, "Email").Should().BeNull();
    }

    private static IConfiguration DefaultConfiguration()
        => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["GoogleAuth:ClientId"] = "client-id" })
            .Build();

    private static GoogleTokenValidator CreateValidator(IConfiguration configuration, HttpMessageHandler handler)
    {
        var clientFactory = Substitute.For<IHttpClientFactory>();
        clientFactory.CreateClient().Returns(new HttpClient(handler));
        return new GoogleTokenValidator(configuration, clientFactory, NullLogger<GoogleTokenValidator>.Instance);
    }

    private static async Task<object> InvokeResolveUserInfoAsync(GoogleTokenValidator validator, GoogleJsonWebSignature.Payload payload, string? accessToken)
    {
        var method = typeof(GoogleTokenValidator).GetMethod("ResolveUserInfoAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var task = (Task)method.Invoke(validator, [payload, accessToken, CancellationToken.None])!;
        await task;
        return task.GetType().GetProperty("Result")!.GetValue(task)!;
    }

    private static T GetProperty<T>(object instance, string propertyName)
        => (T)instance.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(instance)!;

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFactory;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        public int CallCount { get; private set; }
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            LastRequest = request;
            return Task.FromResult(_responseFactory(request));
        }
    }
}
