using FluentAssertions;
using LgymApi.Api.Logging;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class SerilogBootstrapTests
{
    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void ResolveElasticsearchEndpoint_WhenEndpointIsMissing_ReturnsNoTarget(string? endpoint)
    {
        var target = SerilogBootstrap.ResolveElasticsearchEndpoint(endpoint);

        target.Should().BeNull();
    }

    [Test]
    public void ResolveElasticsearchEndpoint_WhenEndpointIsConfigured_ReturnsConfiguredTarget()
    {
        const string endpoint = "http://elasticsearch.example:9200";

        var target = SerilogBootstrap.ResolveElasticsearchEndpoint(endpoint);

        target.Should().Be(new Uri(endpoint));
    }
}
