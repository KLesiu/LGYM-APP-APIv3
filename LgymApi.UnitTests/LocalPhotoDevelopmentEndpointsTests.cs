using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using FluentAssertions.Execution;
using LgymApi.Api.Configuration;
using LgymApi.Application.Options;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class LocalPhotoDevelopmentEndpointsTests
{
    private const string TestSigningKey = "task6-local-photo-test-key-32-bytes-minimum";
    private LocalPhotoDevelopmentStore _store = null!;
    private DevelopmentPhotoHost _host = null!;
    private string _testPrefix = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        _store = new LocalPhotoDevelopmentStore();
        _host = await DevelopmentPhotoHost.StartAsync(_store);
    }

    [SetUp]
    public void SetUp()
    {
        _testPrefix = $"tests/{Id<User>.New()}";
    }

    [TearDown]
    public void TearDown()
    {
        var rootPath = _store.ResolvePath(_testPrefix);
        if (Directory.Exists(rootPath))
        {
            Directory.Delete(rootPath, recursive: true);
        }
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        await _host.DisposeAsync();
    }

    [Test]
    public async Task Upload_WhenEnvironmentIsNotDevelopment_ReturnsNotFound()
    {
        var request = new DefaultHttpContext().Request;
        request.Body = new MemoryStream(new byte[] { 1 });

        var result = await LocalPhotoDevelopmentEndpoints.UploadAsync(
            $"{_testPrefix}/photo.jpg",
            request,
            _store,
            _host.UrlSigner,
            _host.Options,
            new StubWebHostEnvironment(isDevelopment: false),
            CancellationToken.None);

        result.Result.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.NotFound>();
    }

    [Test]
    public async Task Upload_WhenStorageKeyMissing_ReturnsNotFound()
    {
        var request = new DefaultHttpContext().Request;
        request.Body = new MemoryStream(new byte[] { 1 });

        var result = await LocalPhotoDevelopmentEndpoints.UploadAsync(
            " ",
            request,
            _store,
            _host.UrlSigner,
            _host.Options,
            new StubWebHostEnvironment(isDevelopment: true),
            CancellationToken.None);

        result.Result.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.NotFound>();
    }

    [Test]
    [Category("Task6Baseline")]
    public async Task Upload_WhenDevelopment_SavesDecodedFileAndReturnsNoContent()
    {
        var storageKey = $"{_testPrefix}/photos/my image.png";
        var capabilityUri = new Uri(await _host.GenerateUploadUrlAsync(storageKey));
        var request = new DefaultHttpContext().Request;
        request.Method = HttpMethods.Put;
        request.Path = capabilityUri.AbsolutePath;
        request.QueryString = new QueryString(capabilityUri.Query);
        request.HttpContext.Features.Get<IHttpRequestFeature>()!.RawTarget = capabilityUri.PathAndQuery;
        request.ContentType = "image/png";
        request.Body = new MemoryStream(new byte[] { 10, 20, 30 });

        var result = await LocalPhotoDevelopmentEndpoints.UploadAsync(
            Uri.EscapeDataString(storageKey),
            request,
            _store,
            _host.UrlSigner,
            _host.Options,
            new StubWebHostEnvironment(isDevelopment: true),
            CancellationToken.None);

        result.Result.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.NoContent>();
        (await _store.ReadAsync(storageKey)).Should().Equal(new byte[] { 10, 20, 30 });
    }

    [Test]
    public async Task Read_WhenFileDoesNotExist_ReturnsNotFound()
    {
        var storageKey = $"{_testPrefix}/missing.jpg";
        var capabilityUri = new Uri(await _host.GenerateReadUrlAsync(storageKey));
        var request = new DefaultHttpContext().Request;
        request.Method = HttpMethods.Get;
        request.Path = capabilityUri.AbsolutePath;
        request.QueryString = new QueryString(capabilityUri.Query);

        var result = await LocalPhotoDevelopmentEndpoints.ReadAsync(
            storageKey,
            request,
            _store,
            _host.UrlSigner,
            new StubWebHostEnvironment(isDevelopment: true),
            CancellationToken.None);

        result.Result.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.NotFound>();
    }

    [Test]
    [Category("Task6Baseline")]
    public async Task Read_WhenDevelopment_ReturnsFileResultWithResolvedContentType()
    {
        var storageKey = $"{_testPrefix}/photos/result.png";
        await using (var stream = new MemoryStream(new byte[] { 7, 8, 9 }))
        {
            await _store.SaveAsync(storageKey, stream);
        }
        var capabilityUri = new Uri(await _host.GenerateReadUrlAsync(storageKey));
        var request = new DefaultHttpContext().Request;
        request.Method = HttpMethods.Get;
        request.Path = capabilityUri.AbsolutePath;
        request.QueryString = new QueryString(capabilityUri.Query);

        var result = await LocalPhotoDevelopmentEndpoints.ReadAsync(
            Uri.EscapeDataString(storageKey),
            request,
            _store,
            _host.UrlSigner,
            new StubWebHostEnvironment(isDevelopment: true),
            CancellationToken.None);

        result.Result.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.FileContentHttpResult>();
        var fileResult = (Microsoft.AspNetCore.Http.HttpResults.FileContentHttpResult)result.Result!;
        fileResult.ContentType.Should().Be("image/png");
        fileResult.FileContents.Should().Equal(new byte[] { 7, 8, 9 });
    }

    [Test]
    [Category("Task6Baseline")]
    public async Task GeneratedCurrentUploadUrl_WhenUsedAgainstDevelopmentEndpoint_SavesFile()
    {
        var storageKey = $"{_testPrefix}/photos/generated baseline.jpg";
        var capabilityUrl = await _host.GenerateUploadUrlAsync(storageKey);
        using var content = new ByteArrayContent(new byte[] { 4, 5, 6 });
        content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");

        using var response = await _host.Client.PutAsync(capabilityUrl, content);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await _store.ReadAsync(storageKey)).Should().Equal(new byte[] { 4, 5, 6 });
    }

    [Test]
    [Category("Task6Baseline")]
    public async Task GeneratedCurrentReadUrl_WhenUsedAgainstDevelopmentEndpoint_ReturnsKnownFileAndContentType()
    {
        var storageKey = await SavePhotoAsync("generated-read.png", new byte[] { 7, 8, 9 });
        var capabilityUrl = await _host.GenerateReadUrlAsync(storageKey);

        using var response = await _host.Client.GetAsync(capabilityUrl);
        var responseBytes = await response.Content.ReadAsByteArrayAsync();

        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            response.Content.Headers.ContentType!.MediaType.Should().Be("image/png");
            responseBytes.Should().Equal(new byte[] { 7, 8, 9 });
        }
    }

    [Test]
    [Category("Task6Baseline")]
    public async Task GeneratedReadUrl_WhenTargetIsExclusivelyLocked_ConfirmsLockDetectsConcreteStoreAccess()
    {
        var storageKey = await SavePhotoAsync("locked-read.jpg");
        var capabilityUrl = await _host.GenerateReadUrlAsync(storageKey);
        await using var targetLock = OpenExclusiveTargetLock(storageKey);

        using var response = await _host.Client.GetAsync(capabilityUrl);

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
    }

    [TestCase("sig")]
    [TestCase("expires")]
    [Category("Task6Red")]
    public async Task Read_WhenRequiredCapabilityParameterIsMissing_ReturnsNotFoundBeforeStoreAccess(
        string parameterName)
    {
        var storageKey = await SavePhotoAsync($"missing-{parameterName}.jpg");
        var capabilityUrl = RemoveQueryParameter(await _host.GenerateReadUrlAsync(storageKey), parameterName);

        await AssertReadRejectedBeforeStoreAccessAsync(capabilityUrl, storageKey);
    }

    [TestCase("sig")]
    [TestCase("expires")]
    [Category("Task6Red")]
    public async Task Read_WhenCapabilityQueryParameterIsDuplicated_ReturnsNotFoundBeforeStoreAccess(
        string parameterName)
    {
        var storageKey = await SavePhotoAsync($"duplicate-{parameterName}.jpg");
        var capabilityUrl = DuplicateQueryParameter(await _host.GenerateReadUrlAsync(storageKey), parameterName);

        await AssertReadRejectedBeforeStoreAccessAsync(capabilityUrl, storageKey);
    }

    [Test]
    [Category("Task6Red")]
    public async Task Read_WhenSignatureIsTampered_ReturnsNotFoundBeforeStoreAccess()
    {
        var storageKey = await SavePhotoAsync("tampered-signature.jpg");
        var capabilityUrl = ReplaceQueryParameter(
            await _host.GenerateReadUrlAsync(storageKey),
            "sig",
            new string('A', 43));

        await AssertReadRejectedBeforeStoreAccessAsync(capabilityUrl, storageKey);
    }

    [TestCase("not-a-number")]
    [TestCase("01")]
    [TestCase("+1")]
    [Category("Task6Red")]
    public async Task Read_WhenExpiryIsMalformedOrNoncanonical_ReturnsNotFoundBeforeStoreAccess(string expiry)
    {
        var storageKey = await SavePhotoAsync($"malformed-{Id<User>.New()}.jpg");
        var capabilityUrl = ReplaceQueryParameter(await _host.GenerateReadUrlAsync(storageKey), "expires", expiry);

        await AssertReadRejectedBeforeStoreAccessAsync(capabilityUrl, storageKey);
    }

    [Test]
    [Category("Task6Red")]
    public async Task Read_WhenCapabilityIsExpired_ReturnsNotFoundBeforeStoreAccess()
    {
        var storageKey = await SavePhotoAsync("expired.jpg");
        var capabilityUrl = ReplaceQueryParameter(await _host.GenerateReadUrlAsync(storageKey), "expires", "1");

        await AssertReadRejectedBeforeStoreAccessAsync(capabilityUrl, storageKey);
    }

    [Test]
    [Category("Task6Red")]
    public async Task Read_WhenSignedExpiryIsChanged_ReturnsNotFoundBeforeStoreAccess()
    {
        var storageKey = await SavePhotoAsync("changed-expiry.jpg");
        const string changedExpiry = "2000000600";
        var capabilityUrl = ReplaceQueryParameter(
            await _host.GenerateReadUrlAsync(storageKey),
            "expires",
            changedExpiry);

        await AssertReadRejectedBeforeStoreAccessAsync(capabilityUrl, storageKey);
    }

    [Test]
    [Category("Task6Red")]
    public async Task Read_WhenSignedStorageKeyIsChanged_ReturnsNotFoundBeforeOtherFileAccess()
    {
        var signedStorageKey = await SavePhotoAsync("signed-key.jpg", new byte[] { 1 });
        var otherStorageKey = await SavePhotoAsync("other-key.jpg", new byte[] { 9 });
        var capabilityUrl = ReplaceStorageKey(
            await _host.GenerateReadUrlAsync(signedStorageKey),
            signedStorageKey,
            otherStorageKey);

        await AssertReadRejectedBeforeStoreAccessAsync(capabilityUrl, otherStorageKey);
        (await _store.ReadAsync(otherStorageKey)).Should().Equal(new byte[] { 9 });
    }

    [Test]
    [Category("Task6Red")]
    public async Task UploadCapability_WhenReusedForReadMethodAndPath_ReturnsNotFound()
    {
        var storageKey = await SavePhotoAsync("wrong-method.jpg");
        var uploadCapability = await _host.GenerateUploadUrlAsync(storageKey);
        var readAttempt = uploadCapability.Replace("/upload/", "/read/", StringComparison.Ordinal);

        await AssertReadRejectedBeforeStoreAccessAsync(readAttempt, storageKey);
    }

    [Test]
    [Category("Task6Red")]
    public async Task Upload_WhenHttpMethodDoesNotMatchSignedCapability_ReturnsNotFound()
    {
        var storageKey = $"{_testPrefix}/photos/post-method.jpg";
        var capabilityUrl = await _host.GenerateUploadUrlAsync(storageKey);
        using var request = new HttpRequestMessage(HttpMethod.Post, capabilityUrl);

        using var response = await _host.Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    [Category("Task6Red")]
    public async Task Read_WhenPathEncodingIsNoncanonical_ReturnsNotFoundBeforeStoreAccess()
    {
        var storageKey = await SavePhotoAsync("noncanonical.jpg");
        var capabilityUrl = await _host.GenerateReadUrlAsync(storageKey);
        var noncanonicalUrl = capabilityUrl.Replace("%2F", "%2f", StringComparison.Ordinal);

        await AssertReadRejectedBeforeStoreAccessAsync(noncanonicalUrl, storageKey);
    }

    [Test]
    [Category("Task6Red")]
    public async Task Read_WhenEncodedKeyDecodesToTraversal_ReturnsNotFoundBeforeDecodeOrStoreAccess()
    {
        var storageKey = await SavePhotoAsync("decode-order.jpg");
        var capabilityUrl = ReplaceStorageKey(
            await _host.GenerateReadUrlAsync(storageKey),
            storageKey,
            "%2E%2E%2Foutside.jpg");

        using var response = await _host.Client.GetAsync(capabilityUrl);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    [Category("Task6Red")]
    public async Task Upload_WhenStreamExceedsConfiguredMaximum_RejectsAndLeavesNoFinalOrTemporaryFile()
    {
        var storageKey = $"{_testPrefix}/photos/oversized.jpg";
        var capabilityUrl = await _host.GenerateUploadUrlAsync(storageKey);
        await using var stream = new NonSeekableReadStream(new byte[] { 1, 2, 3, 4 });
        using var content = new StreamContent(stream);
        content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");

        using var response = await _host.Client.PutAsync(capabilityUrl, content);

        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
            AssertNoFilesRemain();
        }
    }

    [TestCase("sig")]
    [TestCase("expires")]
    [Category("Task6Red")]
    public async Task Upload_WhenRequiredCapabilityParameterIsMissing_ReturnsNotFoundBeforeStoreAccess(
        string parameterName)
    {
        var storageKey = await SavePhotoAsync($"upload-missing-{parameterName}.jpg");
        var capabilityUrl = RemoveQueryParameter(await _host.GenerateUploadUrlAsync(storageKey), parameterName);

        await AssertUploadRejectedBeforeStoreAccessAsync(capabilityUrl, storageKey);
    }

    [TestCase("sig")]
    [TestCase("expires")]
    [Category("Task6Red")]
    public async Task Upload_WhenCapabilityQueryParameterIsDuplicated_ReturnsNotFoundBeforeStoreAccess(
        string parameterName)
    {
        var storageKey = await SavePhotoAsync($"upload-duplicate-{parameterName}.jpg");
        var capabilityUrl = DuplicateQueryParameter(await _host.GenerateUploadUrlAsync(storageKey), parameterName);

        await AssertUploadRejectedBeforeStoreAccessAsync(capabilityUrl, storageKey);
    }

    [Test]
    [Category("Task6Red")]
    public async Task Upload_WhenExpiryIsMalformed_ReturnsNotFoundBeforeStoreAccess()
    {
        var storageKey = await SavePhotoAsync("upload-malformed-expiry.jpg");
        var capabilityUrl = ReplaceQueryParameter(
            await _host.GenerateUploadUrlAsync(storageKey),
            "expires",
            "not-a-number");

        await AssertUploadRejectedBeforeStoreAccessAsync(capabilityUrl, storageKey);
    }

    [Test]
    [Category("Task6Red")]
    public async Task Upload_WhenCapabilityIsExpired_ReturnsNotFoundBeforeStoreAccess()
    {
        var storageKey = await SavePhotoAsync("upload-expired.jpg");
        var capabilityUrl = ReplaceQueryParameter(await _host.GenerateUploadUrlAsync(storageKey), "expires", "1");

        await AssertUploadRejectedBeforeStoreAccessAsync(capabilityUrl, storageKey);
    }

    [Test]
    [Category("Task6Red")]
    public async Task Upload_WhenSignatureIsTampered_ReturnsNotFoundBeforeStoreAccess()
    {
        var storageKey = await SavePhotoAsync("upload-tampered-signature.jpg");
        var capabilityUrl = ReplaceQueryParameter(
            await _host.GenerateUploadUrlAsync(storageKey),
            "sig",
            new string('A', 43));

        await AssertUploadRejectedBeforeStoreAccessAsync(capabilityUrl, storageKey);
    }

    [Test]
    [Category("Task6Red")]
    public async Task Upload_WhenContentTypeIsDisallowed_RejectsAndLeavesNoFinalOrTemporaryFile()
    {
        var storageKey = $"{_testPrefix}/photos/disallowed-media.jpg";
        var capabilityUrl = await _host.GenerateUploadUrlAsync(storageKey);
        await using var stream = new NonSeekableReadStream(new byte[] { 1, 2, 3 });
        using var content = new StreamContent(stream);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");

        using var response = await _host.Client.PutAsync(capabilityUrl, content);

        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            AssertNoFilesRemain();
        }
    }

    [Test]
    [Category("Task6Red")]
    public async Task Upload_WhenValidStreamIsInProgress_PublishesCompleteFileOnlyAfterAtomicMove()
    {
        var storageKey = $"{_testPrefix}/photos/atomic.jpg";
        var finalPath = _store.ResolvePath(storageKey);
        var capabilityUrl = await _host.GenerateUploadUrlAsync(storageKey);
        var capabilityUri = new Uri(capabilityUrl);
        await using var stream = new GatedReadStream(
            firstChunk: new byte[] { 1 },
            remainingBytes: new byte[] { 2, 3 });
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Put;
        context.Request.Path = capabilityUri.AbsolutePath;
        context.Request.QueryString = new QueryString(capabilityUri.Query);
        context.Request.ContentType = "image/jpeg";
        context.Request.Body = stream;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var uploadTask = LocalPhotoDevelopmentEndpoints.UploadAsync(
            Uri.EscapeDataString(storageKey),
            context.Request,
            _store,
            _host.UrlSigner,
            _host.Options,
            new StubWebHostEnvironment(isDevelopment: true),
            timeout.Token);
        bool finalExistedDuringUpload;
        string[] filesDuringUpload;
        try
        {
            await stream.FirstChunkRead.WaitAsync(timeout.Token);
            filesDuringUpload = await WaitForStoredFilesAsync(
                _store.ResolvePath(_testPrefix),
                timeout.Token);
            finalExistedDuringUpload = File.Exists(finalPath);
        }
        finally
        {
            stream.ReleaseRemainingBytes();
        }

        var result = await uploadTask;
        var finalBytes = await _store.ReadAsync(storageKey);
        var filesAfterUpload = Directory
            .EnumerateFiles(_store.ResolvePath(_testPrefix), "*", SearchOption.AllDirectories)
            .ToArray();

        using (new AssertionScope())
        {
            finalExistedDuringUpload.Should().BeFalse();
            filesDuringUpload.Should().NotContain(finalPath);
            result.Result.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.NoContent>();
            finalBytes.Should().Equal(new byte[] { 1, 2, 3 });
            filesAfterUpload.Should().ContainSingle().Which.Should().Be(finalPath);
        }
    }

    private static async Task<string[]> WaitForStoredFilesAsync(
        string rootPath,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            if (Directory.Exists(rootPath))
            {
                var files = Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories).ToArray();
                if (files.Length > 0)
                {
                    return files;
                }
            }

            await Task.Delay(TimeSpan.FromMilliseconds(10), cancellationToken);
        }
    }

    private async Task<string> SavePhotoAsync(string fileName, byte[]? bytes = null)
    {
        var storageKey = $"{_testPrefix}/photos/{fileName}";
        await using var stream = new MemoryStream(bytes ?? new byte[] { 1, 2, 3 });
        await _store.SaveAsync(storageKey, stream);
        return storageKey;
    }

    private async Task AssertReadRejectedBeforeStoreAccessAsync(string capabilityUrl, string storageKey)
    {
        await using var targetLock = OpenExclusiveTargetLock(storageKey);

        using var response = await _host.Client.GetAsync(capabilityUrl);

        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
            targetLock.CanRead.Should().BeTrue();
        }
    }

    private async Task AssertUploadRejectedBeforeStoreAccessAsync(string capabilityUrl, string storageKey)
    {
        var originalBytes = await _store.ReadAsync(storageKey);
        HttpStatusCode statusCode;
        bool lockRemainedOpen;

        await using (var targetLock = OpenExclusiveTargetLock(storageKey))
        {
            await using var stream = new NonSeekableReadStream(new byte[] { 9, 8, 7 });
            using var content = new StreamContent(stream);
            content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            using var response = await _host.Client.PutAsync(capabilityUrl, content);
            statusCode = response.StatusCode;
            lockRemainedOpen = targetLock.CanWrite;
        }

        using (new AssertionScope())
        {
            statusCode.Should().Be(HttpStatusCode.NotFound);
            lockRemainedOpen.Should().BeTrue();
            (await _store.ReadAsync(storageKey)).Should().Equal(originalBytes!);
        }
    }

    private FileStream OpenExclusiveTargetLock(string storageKey)
    {
        return new FileStream(
            _store.ResolvePath(storageKey),
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.None,
            bufferSize: 1,
            FileOptions.Asynchronous);
    }

    private void AssertNoFilesRemain()
    {
        var rootPath = _store.ResolvePath(_testPrefix);
        if (Directory.Exists(rootPath))
        {
            Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories).Should().BeEmpty();
        }
    }

    private static string RemoveQueryParameter(string url, string parameterName)
    {
        var uri = new Uri(url);
        var retained = uri.Query
            .TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(pair => !pair.StartsWith($"{parameterName}=", StringComparison.Ordinal));
        var query = string.Join("&", retained);
        return query.Length == 0 ? uri.GetLeftPart(UriPartial.Path) : $"{uri.GetLeftPart(UriPartial.Path)}?{query}";
    }

    private static string ReplaceQueryParameter(string url, string parameterName, string value)
    {
        var withoutParameter = RemoveQueryParameter(url, parameterName);
        var separator = withoutParameter.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        return $"{withoutParameter}{separator}{parameterName}={value}";
    }

    private static string DuplicateQueryParameter(string url, string parameterName)
    {
        var pair = new Uri(url).Query
            .TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .SingleOrDefault(value => value.StartsWith($"{parameterName}=", StringComparison.Ordinal))
            ?? $"{parameterName}=missing";
        return $"{url}&{pair}&{pair}";
    }

    private static string ReplaceStorageKey(string url, string currentStorageKey, string replacementStorageKey)
    {
        return url.Replace(
            Uri.EscapeDataString(currentStorageKey),
            Uri.EscapeDataString(replacementStorageKey),
            StringComparison.Ordinal);
    }

    private sealed class StubWebHostEnvironment : IWebHostEnvironment
    {
        public StubWebHostEnvironment(bool isDevelopment)
        {
            EnvironmentName = isDevelopment ? Environments.Development : Environments.Production;
        }

        public string ApplicationName { get; set; } = "LgymApi.UnitTests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = AppContext.BaseDirectory;
        public string EnvironmentName { get; set; }
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class DevelopmentPhotoHost : IAsyncDisposable
    {
        private readonly WebApplication _application;

        private DevelopmentPhotoHost(
            WebApplication application,
            HttpClient client,
            LocalPhotoDevelopmentUrlSigner urlSigner,
            PhotoStorageOptions options)
        {
            _application = application;
            Client = client;
            UrlSigner = urlSigner;
            Options = options;
        }

        public HttpClient Client { get; }
        public LocalPhotoDevelopmentUrlSigner UrlSigner { get; }
        public PhotoStorageOptions Options { get; }

        public static async Task<DevelopmentPhotoHost> StartAsync(LocalPhotoDevelopmentStore store)
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                ApplicationName = typeof(LocalPhotoDevelopmentEndpoints).Assembly.FullName,
                EnvironmentName = Environments.Development
            });
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PhotoStorage:LocalDevelopmentSigningKey"] = TestSigningKey
            });
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Services.AddSingleton(store);
            var timeProvider = new FixedTimeProvider(DateTimeOffset.FromUnixTimeSeconds(2_000_000_000));
            var options = new PhotoStorageOptions
            {
                LocalDevelopmentSigningKey = TestSigningKey,
                MaxFileSizeBytes = 3
            };
            var urlSigner = new LocalPhotoDevelopmentUrlSigner(TestSigningKey, timeProvider);
            builder.Services.AddSingleton<TimeProvider>(timeProvider);
            builder.Services.AddSingleton(options);
            builder.Services.AddSingleton(urlSigner);

            var application = builder.Build();
            application.MapLocalPhotoDevelopmentEndpoints();
            await application.StartAsync();

            var server = application.Services.GetRequiredService<IServer>();
            var address = server.Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            return new DevelopmentPhotoHost(
                application,
                new HttpClient { BaseAddress = new Uri(address) },
                urlSigner,
                options);
        }

        public Task<string> GenerateUploadUrlAsync(string storageKey)
        {
            return CreateProvider().GenerateSignedUploadUrlAsync(
                storageKey,
                "image/jpeg",
                TimeSpan.FromMinutes(5));
        }

        public Task<string> GenerateReadUrlAsync(string storageKey)
        {
            return CreateProvider().GenerateSignedReadUrlAsync(storageKey, TimeSpan.FromMinutes(5));
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _application.StopAsync();
            await _application.DisposeAsync();
        }

        private LocalPhotoStorageProvider CreateProvider()
        {
            var context = new DefaultHttpContext();
            context.Request.Scheme = Client.BaseAddress!.Scheme;
            context.Request.Host = HostString.FromUriComponent(Client.BaseAddress);
            var accessor = new HttpContextAccessor { HttpContext = context };
            return ActivatorUtilities.CreateInstance<LocalPhotoStorageProvider>(_application.Services, accessor);
        }
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

    private sealed class NonSeekableReadStream : Stream
    {
        private readonly MemoryStream _inner;

        public NonSeekableReadStream(byte[] bytes)
        {
            _inner = new MemoryStream(bytes);
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            return _inner.ReadAsync(buffer, cancellationToken);
        }

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            await _inner.DisposeAsync();
            GC.SuppressFinalize(this);
        }
    }

    private sealed class GatedReadStream : Stream
    {
        private readonly byte[] _firstChunk;
        private readonly byte[] _remainingBytes;
        private readonly TaskCompletionSource _firstChunkRead = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseRemainingBytes = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _readState;

        public GatedReadStream(byte[] firstChunk, byte[] remainingBytes)
        {
            _firstChunk = firstChunk;
            _remainingBytes = remainingBytes;
        }

        public Task FirstChunkRead => _firstChunkRead.Task;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public void ReleaseRemainingBytes() => _releaseRemainingBytes.TrySetResult();

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (_readState == 0)
            {
                _firstChunk.AsSpan().CopyTo(buffer.Span);
                _readState = 1;
                _firstChunkRead.TrySetResult();
                return _firstChunk.Length;
            }

            if (_readState == 1)
            {
                await _releaseRemainingBytes.Task.WaitAsync(cancellationToken);
                _remainingBytes.AsSpan().CopyTo(buffer.Span);
                _readState = 2;
                return _remainingBytes.Length;
            }

            return 0;
        }

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
