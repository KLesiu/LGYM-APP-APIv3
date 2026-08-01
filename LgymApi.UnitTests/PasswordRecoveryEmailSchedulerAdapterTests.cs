using FluentAssertions;
using LgymApi.Application.Features.PasswordReset.Contracts;
using LgymApi.BackgroundWorker.Common.Notifications.Models;
using LgymApi.Application.Notifications.Contracts.Email;
using LgymApi.Notifications;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using NUnit.Framework;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class PasswordRecoveryEmailSchedulerAdapterTests
{
    [Test]
    public async Task ScheduleAsync_MapsSevenFieldIdentityRequestToWorkerPayload()
    {
        var scheduler = new CapturingEmailScheduler();
        var adapter = new PasswordRecoveryEmailSchedulerAdapter(scheduler);
        var cancellationToken = new CancellationTokenSource().Token;
        var request = new PasswordRecoveryEmailRequest(
            ParseId<User>("12121212-1212-1212-1212-121212121212"),
            ParseId<PasswordResetToken>("34343434-3434-3434-3434-343434343434"),
            "Identity Adapter User",
            "identity.adapter@example.test",
            "identity-reset-token-380",
            "https://identity-sentinel.example.test/reset",
            "!invalid-culture-380");

        await adapter.ScheduleAsync(request, cancellationToken);

        typeof(PasswordRecoveryEmailRequest).IsPublic.Should().BeTrue();
        typeof(PasswordRecoveryEmailSchedulerAdapter).IsNotPublic.Should().BeTrue();
        typeof(PasswordRecoveryEmailRequest).GetProperties()
            .Select(property => property.Name)
            .Should().Equal("UserId", "TokenId", "UserName", "RecipientEmail", "ResetToken", "ResetUrl", "CultureName");
        var adapterConstructor = typeof(PasswordRecoveryEmailSchedulerAdapter).GetConstructors().Should().ContainSingle().Which;
        adapterConstructor.GetParameters().Select(parameter => parameter.ParameterType)
            .Should().Equal(typeof(IEmailSchedulingPort<PasswordRecoveryEmailPayload>));
        var scheduleMethod = typeof(PasswordRecoveryEmailSchedulerAdapter)
            .GetMethod(nameof(PasswordRecoveryEmailSchedulerAdapter.ScheduleAsync));
        scheduleMethod.Should().NotBeNull();
        scheduleMethod!.ReturnType.Should().Be(typeof(Task));
        scheduleMethod.GetParameters().Select(parameter => parameter.ParameterType)
            .Should().Equal(typeof(PasswordRecoveryEmailRequest), typeof(CancellationToken));
        scheduler.Payload.Should().NotBeNull();
        scheduler.Payload!.UserId.Should().Be(request.UserId);
        scheduler.Payload.TokenId.Should().Be(request.TokenId);
        scheduler.Payload.UserName.Should().Be(request.UserName);
        scheduler.Payload.RecipientEmail.Should().Be(request.RecipientEmail);
        scheduler.Payload.ResetToken.Should().Be(request.ResetToken);
        scheduler.Payload.ResetUrl.Should().Be("https://identity-sentinel.example.test/reset");
        scheduler.Payload.CultureName.Should().Be(request.CultureName);
        scheduler.Payload.CorrelationId.GetValue().Should().Be(request.TokenId.GetValue());
        scheduler.Payload.NotificationType.Value.Should().Be("user.password.recovery");
        scheduler.Payload.Culture.Name.Should().Be("en-US");
        scheduler.CancellationToken.Should().Be(cancellationToken);
    }

    [Test]
    public async Task ScheduleAsync_ForwardsCancellationToWorkerScheduler()
    {
        var scheduler = new CapturingEmailScheduler();
        var adapter = new PasswordRecoveryEmailSchedulerAdapter(scheduler);
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();
        var request = new PasswordRecoveryEmailRequest(
            ParseId<User>("56565656-5656-5656-5656-565656565656"),
            ParseId<PasswordResetToken>("78787878-7878-7878-7878-787878787878"),
            "Cancelled Identity User",
            "cancelled.adapter@example.test",
            "cancelled-token-380",
            "https://cancelled-sentinel.example.test/reset",
            "en-US");

        var action = () => adapter.ScheduleAsync(request, cancellationSource.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
        scheduler.CancellationToken.Should().Be(cancellationSource.Token);
    }

    private static Id<TEntity> ParseId<TEntity>(string value)
    {
        Id<TEntity>.TryParse(value, out var id).Should().BeTrue();
        return id;
    }

    private sealed class CapturingEmailScheduler : IEmailSchedulingPort<PasswordRecoveryEmailPayload>
    {
        public PasswordRecoveryEmailPayload? Payload { get; private set; }
        public CancellationToken CancellationToken { get; private set; }

        public Task ScheduleAsync(PasswordRecoveryEmailPayload payload, CancellationToken cancellationToken = default)
        {
            CancellationToken = cancellationToken;
            cancellationToken.ThrowIfCancellationRequested();
            Payload = payload;
            return Task.CompletedTask;
        }
    }
}
