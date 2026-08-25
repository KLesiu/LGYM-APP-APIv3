using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;

namespace LgymApi.IntegrationTests;

/// <summary>
/// Smoke tests validating the test fixture helpers for reliability scenarios.
/// Demonstrates:
/// - Idempotency-key header setting and repeated request execution
/// - Durable-state assertions for uniqueness verification
/// - Baseline counts for transaction isolation
/// 
/// These fixtures enable T12 (reliability integration tests) to be practical.
/// </summary>
[TestFixture]
public class ReliabilityFixtureSmokeTests : IntegrationTestBase
{
    /// <summary>
    /// Demonstrates the fixture can set idempotency headers and send repeated requests.
    /// This is a prerequisite for T12 replay and duplicate-request tests.
    /// </summary>
    [Test]
    public async Task SetIdempotencyKey_HeaderIsSet_AndCanBeSent()
    {
        // Arrange
        await SeedAdminAsync();
        var idempotencyKey = $"smoke-test-{Id<object>.New():N}";

        // Act
        SetIdempotencyKey(idempotencyKey);
        var headerValue = Client.DefaultRequestHeaders.FirstOrDefault(h => h.Key == "Idempotency-Key").Value?.FirstOrDefault();

        // Assert
        headerValue.Should().Be(idempotencyKey, "Idempotency-Key header must be set correctly");

        // Clean up
        ClearIdempotencyKey();
        var clearedValue = Client.DefaultRequestHeaders.FirstOrDefault(h => h.Key == "Idempotency-Key").Value?.FirstOrDefault();
        clearedValue.Should().BeNull("Idempotency-Key header must be clearable");
    }

    /// <summary>
    /// Demonstrates that SendRepeatedRequestAsync can execute the same request twice.
    /// This is the foundation for idempotency and replay testing.
    /// </summary>
    [Test]
    public async Task SendRepeatedRequestAsync_SendsSameRequestTwice_WithoutErrors()
    {
        // Arrange
        await SeedAdminAsync();
        var idempotencyKey = $"repeated-request-{Id<object>.New():N}";
        var payload = new { name = "Test User", email = "test@example.com", password = "TempPass123!", cpassword = "TempPass123!", isVisibleInRanking = true, adultConfirmed = true };

        // Act
        var (firstResponse, secondResponse) = await SendRepeatedRequestAsync("/api/register", payload, idempotencyKey);

        // Assert
        firstResponse.Should().NotBeNull("First request must succeed");
        secondResponse.Should().NotBeNull("Second request must succeed");
        // Both should complete without throwing (status code may differ per endpoint idempotency handling)
    }

    /// <summary>
    /// Demonstrates CommandEnvelope uniqueness assertion for a single correlation ID.
    /// Validates the fixture can verify duplicate-protection enforcement.
    /// </summary>
    [Test]
    public void AssertCommandEnvelopeUniquenessAsync_WithNoRecords_Fails()
    {
        // Arrange
        var correlationId = Id<CorrelationScope>.New();

        // Act & Assert
        // When no record exists, count will be 0, and assertion should fail
        // The helper is called and should throw AssertionException
        var act = () => AssertCommandEnvelopeUniquenessAsync(correlationId).GetAwaiter().GetResult();
        act.Should().Throw<AssertionException>("Expected AssertionException when no CommandEnvelope exists for the correlation ID");
    }

    /// <summary>
    /// Demonstrates that baseline record count queries work without errors.
    /// Enables transaction isolation and concurrency testing in T12.
    /// </summary>
    [Test]
    public async Task CountAllCommandEnvelopesAsync_Returns_NonNegativeCount()
    {
        // Act
        var count = await CountAllCommandEnvelopesAsync();

        // Assert
        count.Should().BeGreaterThanOrEqualTo(0, "CommandEnvelope count must be non-negative");
    }

    /// <summary>
    /// Demonstrates that baseline notification record count queries work without errors.
    /// Enables email deduplication verification in T12.
    /// </summary>
    [Test]
    public async Task CountAllNotificationMessagesAsync_Returns_NonNegativeCount()
    {
        // Act
        var count = await CountAllNotificationMessagesAsync();

        // Assert
        count.Should().BeGreaterThanOrEqualTo(0, "NotificationMessage count must be non-negative");
    }

    /// <summary>
    /// Demonstrates the fixture can query command envelopes by correlation ID.
    /// Enables state inspection for crash-window and retry scenarios in T12.
    /// </summary>
    [Test]
    public async Task CountCommandEnvelopesByCorrelationIdAsync_WithNoMatches_ReturnsZero()
    {
        // Arrange
        var correlationId = Id<CorrelationScope>.New();

        // Act
        var count = await CountCommandEnvelopesByCorrelationIdAsync(correlationId);

        // Assert
        count.Should().Be(0, "Count must be zero for non-existent correlation ID");
    }

    /// <summary>
    /// Demonstrates the fixture can retrieve full status history for a correlation ID.
    /// Enables replay and state-transition verification in T12.
    /// </summary>
    [Test]
    public async Task GetCommandEnvelopeStatusesAsync_WithNoMatches_ReturnsEmptyList()
    {
        // Arrange
        var correlationId = Id<CorrelationScope>.New();

        // Act
        var statuses = await GetCommandEnvelopeStatusesAsync(correlationId);

        // Assert
        statuses.Should().BeEmpty("Status list must be empty for non-existent correlation ID");
    }

    /// <summary>
    /// Demonstrates ProcessPendingCommandsAsync executes without errors.
    /// This is used in all T12 scenarios to simulate background job processing.
    /// </summary>
    [Test]
    public async Task ProcessPendingCommandsAsync_WithNoPendingCommands_CompletesSuccessfully()
    {
        // Act & Assert
        var act = async () => await ProcessPendingCommandsAsync();
        await act.Should().NotThrowAsync("ProcessPendingCommandsAsync must be idempotent and safe even with no pending commands");
    }
}
