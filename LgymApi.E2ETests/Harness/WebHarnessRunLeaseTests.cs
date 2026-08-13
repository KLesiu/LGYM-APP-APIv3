namespace LgymApi.E2ETests.Harness;

[TestFixture]
[Category("WebHarness")]
public sealed class WebHarnessRunLeaseTests
{
    [Test]
    public async Task WebHarnessRunLease_disposes_scenario_browser_Expo_and_source_once_in_reverse_order()
    {
        var events = new List<string>();
        var source = new RecordingWebHarnessLayer("source-run", events, new(true, true, true, true));
        var expo = new RecordingWebHarnessLayer("expo", events, new(true, true, true, false));
        var browser = new RecordingWebHarnessLayer("browser", events, new(true, false, false, true));
        var scenario = new RecordingWebHarnessLayer("scenario", events, new(false, false, false, false));
        var lease = WebHarnessRunLease.Create(source, [expo], browser, [scenario], TimeSpan.FromSeconds(1));

        await Task.WhenAll(lease.DisposeAsync().AsTask(), lease.DisposeAsync().AsTask());

        Assert.Multiple(() =>
        {
            Assert.That(events, Is.EqualTo(["scenario", "browser", "expo", "source-run"]));
            Assert.That(lease.CleanupReceipt.AttemptedCategories,
                Is.EqualTo(["scenario", "browser", "expo", "source-run"]));
            Assert.That(lease.CleanupReceipt.FailureCount, Is.Zero);
            Assert.That(lease.CleanupReceipt.SourceStateMatched, Is.True);
            Assert.That(lease.CleanupReceipt.ProcessTreeAbsent, Is.True);
            Assert.That(lease.CleanupReceipt.StagedSourceAbsent, Is.True);
            Assert.That(lease.CleanupReceipt.BrowserClosed, Is.True);
            Assert.That(lease.ToString(), Is.EqualTo("<web-harness-run-lease>"));
        });
    }

    [TestCase("scenario")]
    [TestCase("browser")]
    [TestCase("expo")]
    [TestCase("source-run")]
    public async Task WebHarnessRunLease_aggregates_each_layer_failure_and_continues_later_cleanup(string failingCategory)
    {
        var events = new List<string>();
        var source = new RecordingWebHarnessLayer("source-run", events, new(true, true, true, true),
            failures: failingCategory == "source-run" ? 2 : 0);
        var expo = new RecordingWebHarnessLayer("expo", events, new(true, true, true, false),
            failures: failingCategory == "expo" ? 1 : 0);
        var browser = new RecordingWebHarnessLayer("browser", events, new(true, false, false, true),
            failures: failingCategory == "browser" ? 1 : 0);
        var scenario = new RecordingWebHarnessLayer("scenario", events, new(false, false, false, false),
            failures: failingCategory == "scenario" ? 1 : 0);
        var lease = WebHarnessRunLease.Create(source, [expo], browser, [scenario], TimeSpan.FromSeconds(1));

        var exception = Assert.ThrowsAsync<WebHarnessCleanupException>(async () => await lease.DisposeAsync());
        var repeated = Assert.ThrowsAsync<WebHarnessCleanupException>(async () => await lease.DisposeAsync());

        Assert.Multiple(() =>
        {
            IReadOnlyList<string> expectedEvents = failingCategory == "source-run"
                ? ["scenario", "browser", "expo", "source-run", "source-run"]
                : ["scenario", "browser", "expo", "source-run"];
            Assert.That(events, Is.EqualTo(expectedEvents));
            Assert.That(exception!.Receipt.AttemptedCategories,
                Is.EqualTo(["scenario", "browser", "expo", "source-run"]));
            Assert.That(repeated, Is.SameAs(exception));
            Assert.That(exception.Receipt.FailureCount,
                Is.EqualTo(failingCategory == "source-run" ? 2 : 1));
            Assert.That(exception.ToString(), Does.Not.Contain("private failure canary"));
            Assert.That(source.DisposeCount, Is.EqualTo(failingCategory == "source-run" ? 2 : 1));
            Assert.That(expo.DisposeCount, Is.EqualTo(1));
            Assert.That(browser.DisposeCount, Is.EqualTo(1));
            Assert.That(scenario.DisposeCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task WebHarnessRunLease_partial_startup_cleans_only_acquired_layers_and_retries_source_after_handles_release()
    {
        var events = new List<string>();
        var source = new RecordingWebHarnessLayer("source-run", events, new(true, true, true, true), failures: 1);
        var expo = new RecordingWebHarnessLayer("expo", events, new(true, true, true, false));
        var lease = WebHarnessRunLease.Create(source, [expo], browser: null, scenarios: [], TimeSpan.FromSeconds(1));

        await lease.DisposeAsync();

        Assert.Multiple(() =>
        {
            Assert.That(events, Is.EqualTo(["expo", "source-run", "source-run"]));
            Assert.That(lease.CleanupReceipt.AttemptedCategories, Is.EqualTo(["expo", "source-run"]));
            Assert.That(lease.CleanupReceipt.FailureCount, Is.Zero);
            Assert.That(lease.CleanupReceipt.SourceStateMatched, Is.True);
            Assert.That(lease.CleanupReceipt.ProcessTreeAbsent, Is.True);
            Assert.That(lease.CleanupReceipt.StagedSourceAbsent, Is.True);
            Assert.That(lease.CleanupReceipt.BrowserClosed, Is.True);
        });
    }

    private sealed class RecordingWebHarnessLayer(
        string category,
        ICollection<string> events,
        WebHarnessCleanupFacts facts,
        int failures = 0) : IWebHarnessCleanupLayer
    {
        private int _remainingFailures = failures;

        public string Category => category;

        public WebHarnessCleanupFacts Facts => facts;

        internal int DisposeCount { get; private set; }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            events.Add(category);
            if (_remainingFailures-- > 0)
            {
                throw new IOException("private failure canary");
            }

            return ValueTask.CompletedTask;
        }
    }
}
