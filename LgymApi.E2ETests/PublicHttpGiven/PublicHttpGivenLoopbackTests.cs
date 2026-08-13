using LgymApi.E2ETests.Given;

namespace LgymApi.E2ETests.PublicHttpGiven;

[TestFixture]
[Category("WebHarness")]
public sealed class PublicHttpGivenLoopbackTests
{
    [Test]
    public async Task Public_HTTP_client_reaches_test_owned_loopback_and_cleans_listener()
    {
        // Given
        var server = PublicHttpGivenLoopbackServer.Start();
        LoopbackWireReceipt receipt;

        // When
        await using (server)
        using (var httpClient = new HttpClient { BaseAddress = new Uri(server.BaseAddress, "prefix/") })
        {
            var client = new PublicHttpGivenClient(httpClient, TimeSpan.FromSeconds(2));
            await client.RegisterAsync(SyntheticCredentials.Create(), CancellationToken.None);
            receipt = await server.GetReceiptAsync();
        }

        // Then
        Assert.Multiple(() =>
        {
            Assert.That(receipt.Method, Is.EqualTo("POST"));
            Assert.That(receipt.Path, Is.EqualTo("/api/register"));
            Assert.That(receipt.Language, Is.EqualTo("en"));
            Assert.That(receipt.AuthorizationPresent, Is.False);
            Assert.That(receipt.BodyRetained, Is.False);
            Assert.That(server.IsStopped, Is.True);
        });
        TestContext.Out.WriteLine($"receipt category=public-http-given loopback=true {receipt} listenerStopped=true");
    }
}
