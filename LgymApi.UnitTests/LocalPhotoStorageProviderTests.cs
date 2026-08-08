using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using LgymApi.Application.Options;
using LgymApi.Infrastructure.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class LocalPhotoStorageProviderTests
{
    private const string TestSigningKey = "task6-local-photo-test-key-32-bytes-minimum";
    private static readonly DateTimeOffset FixedUtcNow = DateTimeOffset.FromUnixTimeSeconds(2_000_000_000);

    [Test]
    [Category("Task6Baseline")]
    public async Task GenerateSignedUploadUrlAsync_WhenRequestHostAvailable_UsesCurrentHost()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Scheme = "https";
        httpContext.Request.Host = new HostString("api.example.com");
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(httpContext);
        var provider = new LocalPhotoStorageProvider(
            accessor,
            new LocalPhotoDevelopmentStore(),
            new LocalPhotoDevelopmentUrlSigner(TestSigningKey, TimeProvider.System));

        var result = await provider.GenerateSignedUploadUrlAsync("photos/key one.jpg", "image/jpeg", TimeSpan.FromMinutes(10));

        result.Should().StartWith("https://api.example.com/dev/photos/upload/");
        result.Should().Contain("photos%2Fkey%20one.jpg");

        var uri = new Uri(result);
        var query = QueryHelpers.ParseQuery(uri.Query);
        query.Should().HaveCount(3);
        query.Should().ContainKey("v").WhoseValue.Should().Equal("1");
        query.Should().ContainKey("expires");
        long.TryParse(query["expires"].Single(), out var expiresAt).Should().BeTrue();
        expiresAt.Should().BeGreaterThan(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        query.Should().ContainKey("sig").WhoseValue.Should().ContainSingle();
    }

    [Test]
    [Category("Task6Baseline")]
    public async Task GenerateSignedReadUrlAsync_WhenRequestMissing_UsesFallbackLocalhost()
    {
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns((HttpContext?)null);
        var provider = new LocalPhotoStorageProvider(
            accessor,
            new LocalPhotoDevelopmentStore(),
            new LocalPhotoDevelopmentUrlSigner(TestSigningKey, TimeProvider.System));

        var result = await provider.GenerateSignedReadUrlAsync("photos/key.jpg", TimeSpan.FromMinutes(5));

        result.Should().StartWith("https://localhost:7025/dev/photos/read/");
    }

    [Test]
    [Category("Task6Baseline")]
    public void CanonicalHmacSha256Oracle_WhenKnownTupleIsSigned_MatchesKnownVector()
    {
        var signature = CalculateExpectedSignature(
            "GET",
            "photos/reports/key one.jpg",
            2_000_000_300);

        signature.Should().Be("tmnfGtF-7OmRw3iNHw6nJ29BUNphS2DJjdGgE9ZeVh0");
    }

    [TestCase(true, "PUT", "/dev/photos/upload/")]
    [TestCase(false, "GET", "/dev/photos/read/")]
    [Category("Task6Red")]
    public async Task GeneratedUrl_WhenLocalCapabilityIsIssued_ContainsCanonicalHmacSha256Fields(
        bool upload,
        string expectedMethod,
        string expectedPathPrefix)
    {
        await using var services = CreateConfiguredServices();
        var provider = ActivatorUtilities.CreateInstance<LocalPhotoStorageProvider>(services);
        const string normalizedStorageKey = "photos/reports/key one.jpg";
        var expectedExpiry = FixedUtcNow.AddMinutes(5).ToUnixTimeSeconds();
        var expectedSignature = CalculateExpectedSignature(expectedMethod, normalizedStorageKey, expectedExpiry);

        var result = upload
            ? await provider.GenerateSignedUploadUrlAsync(normalizedStorageKey, "image/jpeg", TimeSpan.FromMinutes(5))
            : await provider.GenerateSignedReadUrlAsync(normalizedStorageKey, TimeSpan.FromMinutes(5));

        var uri = new Uri(result);
        var query = QueryHelpers.ParseQuery(uri.Query);
        query.Should().ContainKey("v").WhoseValue.Should().Equal("1");
        query.Should().ContainKey("expires");
        query["expires"].Should().ContainSingle();
        long.TryParse(query["expires"].Single(), out var expiresAt).Should().BeTrue();
        expiresAt.Should().Be(expectedExpiry);
        query.Should().ContainKey("sig");
        query["sig"].Should().ContainSingle();
        query["sig"].Single().Should().Be(expectedSignature);
        uri.AbsolutePath.Should().Be($"{expectedPathPrefix}{Uri.EscapeDataString(normalizedStorageKey)}");
    }

    private static ServiceProvider CreateConfiguredServices()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PhotoStorage:LocalDevelopmentSigningKey"] = TestSigningKey
            })
            .Build();
        var context = new DefaultHttpContext();
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("localhost:7025");

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IHttpContextAccessor>(new HttpContextAccessor { HttpContext = context });
        services.AddSingleton(new LocalPhotoDevelopmentStore());
        var timeProvider = new FixedTimeProvider(FixedUtcNow);
        services.AddSingleton<TimeProvider>(timeProvider);
        services.AddSingleton(new LocalPhotoDevelopmentUrlSigner(TestSigningKey, timeProvider));
        services.Configure<PhotoStorageOptions>(configuration.GetSection("PhotoStorage"));
        return services.BuildServiceProvider();
    }

    private static string CalculateExpectedSignature(string method, string normalizedStorageKey, long expiresAt)
    {
        var canonicalTuple = string.Join('\n', "1", method, normalizedStorageKey, expiresAt.ToString(CultureInfo.InvariantCulture));
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(TestSigningKey));
        return WebEncoders.Base64UrlEncode(hmac.ComputeHash(Encoding.UTF8.GetBytes(canonicalTuple)));
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        public FixedTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;
    }
}
