using Elastic.Ingest.Elasticsearch;
using Elastic.Serilog.Sinks;
using Microsoft.AspNetCore.Builder;
using Serilog;

namespace LgymApi.Api.Logging;

public static class SerilogBootstrap
{
    public static void ConfigureSerilog(WebApplicationBuilder builder)
    {
        var elasticsearchEndpoint = ResolveElasticsearchEndpoint(builder.Configuration["Elasticsearch:Url"]);
        var loggerConfiguration = new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.WithProperty("Application", "lgym-api")
            .Enrich.WithProperty("Environment", builder.Environment.EnvironmentName)
            .Enrich.With<SensitiveDataEnricher>();

        if (elasticsearchEndpoint is not null)
        {
            ConfigureElasticsearchSink(loggerConfiguration, elasticsearchEndpoint);
        }

        Log.Logger = loggerConfiguration.CreateLogger();

        builder.Logging.ClearProviders();
        builder.Logging.AddSerilog();
        builder.Services.AddSerilog();
    }

    internal static Uri? ResolveElasticsearchEndpoint(string? endpointValue)
    {
        if (string.IsNullOrWhiteSpace(endpointValue))
        {
            return null;
        }

        return new Uri(endpointValue);
    }

    private static void ConfigureElasticsearchSink(LoggerConfiguration loggerConfiguration, Uri endpoint)
    {
        loggerConfiguration.WriteTo.Async(a => a.Elasticsearch(
                new[] { endpoint }, // NOSONAR: configured endpoint may intentionally use HTTP on a private Docker/loopback network
                opts =>
                {
                    opts.BootstrapMethod = BootstrapMethod.Silent;
                    opts.DataStream = new Elastic.Ingest.Elasticsearch.DataStreams.DataStreamName("logs", "lgym", "app");
                }));
    }
}
