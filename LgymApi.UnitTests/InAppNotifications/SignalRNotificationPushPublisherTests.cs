using FluentAssertions;
using LgymApi.Api.Features.InAppNotification;
using LgymApi.Api.Features.InAppNotification.Contracts;
using LgymApi.Api.Hubs;
using LgymApi.Application.Notifications.Models;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Notifications;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using LgymApi.Identity.Contracts.Accounts;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NUnit.Framework;
using System.Reflection;

namespace LgymApi.UnitTests.InAppNotifications;

[TestFixture]
public sealed class SignalRNotificationPushPublisherTests
{
    [Test]
    public async Task PushAsync_Success_SendsNotificationDtoToValidatedConnection()
    {
        var notification = CreateNotification();
        var (publisher, clientProxy, logger) = CreatePublisher(notification.RecipientId, throwOnSend: false);

        await InvokePushAsync(publisher, notification);

        clientProxy.SendCalls.Should().Be(1);
        clientProxy.LastConnectionId.Should().Be("connection-1");
        clientProxy.LastMethod.Should().Be("ReceiveNotification");
        clientProxy.LastArgs.Should().NotBeNull();
        clientProxy.LastArgs![0].Should().BeOfType<InAppNotificationResultDto>();

        var payload = (InAppNotificationResultDto)clientProxy.LastArgs[0]!;
        payload.Id.Should().Be(notification.Id.ToString());
        payload.Message.Should().Be(notification.Message);
        payload.RedirectUrl.Should().Be(notification.RedirectUrl);
        payload.IsRead.Should().Be(notification.IsRead);
        payload.Type.Should().Be(notification.Type.Value);
        payload.IsSystemNotification.Should().Be(notification.IsSystemNotification);
        payload.SenderUserId.Should().BeNull();
        payload.CreatedAt.Should().Be(notification.CreatedAt);
        logger.WarningCalls.Should().Be(0);
    }

    [Test]
    public async Task PushAsync_Failure_LogsWarning()
    {
        var notification = CreateNotification();
        var (publisher, _, logger) = CreatePublisher(notification.RecipientId, throwOnSend: true);

        await InvokePushAsync(publisher, notification);

        logger.WarningCalls.Should().Be(1);
    }

    private static (object Publisher, FakeClientProxy ClientProxy, FakeLogger Logger) CreatePublisher(
        Id<User> recipientId,
        bool throwOnSend)
    {
        var clientProxy = new FakeClientProxy(throwOnSend);
        var hubClients = new FakeHubClients(clientProxy);
        var hubContext = new FakeHubContext(hubClients);
        var logger = new FakeLogger();
        var registry = new AccountSessionConnectionRegistry();
        var accountId = recipientId.Rebind<AccountReference>();
        var sessionId = Id<AccountSessionReference>.New();
        registry.Register(accountId, sessionId, "connection-1");
        var resolver = Substitute.For<IAuthenticatedAccountContextResolver>();
        resolver.ResolveAsync(Arg.Any<Id<AccountReference>>(), Arg.Any<Id<AccountSessionReference>>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(new AuthenticatedAccountResolution(
                AuthenticatedAccountResolutionStatus.Active,
                new AuthenticatedAccountContext(call.ArgAt<Id<AccountReference>>(0), sessionId, [], [], false, false))));
        var type = typeof(NotificationHub).Assembly.GetType("LgymApi.Api.Features.InAppNotification.SignalRNotificationPushPublisher", throwOnError: true)!;
        var publisher = Activator.CreateInstance(
            type,
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
            null,
            new object[] { hubContext, registry, resolver, logger },
            null)!;

        return (publisher, clientProxy, logger);
    }

    private static InAppNotificationResult CreateNotification()
        => new(
            Id<InAppNotification>.New(),
            Id<User>.New(),
            "Hello",
            "/trainers/dashboard",
            false,
            InAppNotificationTypes.InvitationSent,
            false,
            null,
            DateTimeOffset.UtcNow);

    private static async Task InvokePushAsync(object publisher, InAppNotificationResult notification)
    {
        var method = publisher.GetType().GetMethod("PushAsync", BindingFlags.Instance | BindingFlags.Public)!;
        await (Task)method.Invoke(publisher, new object[] { notification, CancellationToken.None })!;
    }

    private sealed class FakeClientProxy : IClientProxy
    {
        private readonly bool _throwOnSend;

        public FakeClientProxy(bool throwOnSend)
        {
            _throwOnSend = throwOnSend;
        }

        public int SendCalls { get; private set; }
        public string? LastMethod { get; private set; }
        public object?[]? LastArgs { get; private set; }
        public string? LastConnectionId { get; set; }

        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
        {
            SendCalls++;
            LastMethod = method;
            LastArgs = args;

            if (_throwOnSend)
            {
                throw new InvalidOperationException("send failed");
            }

            return Task.CompletedTask;
        }
    }

    private sealed class FakeHubClients : IHubClients
    {
        private readonly FakeClientProxy _proxy;

        public FakeHubClients(FakeClientProxy proxy)
        {
            _proxy = proxy;
        }

        public IClientProxy All => _proxy;
        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => _proxy;
        public IClientProxy Client(string connectionId)
        {
            _proxy.LastConnectionId = connectionId;
            return _proxy;
        }
        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => _proxy;

        public IClientProxy Group(string groupName)
        {
            return _proxy;
        }

        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => Group(groupName);
        public IClientProxy Groups(IReadOnlyList<string> groupNames) => _proxy;
        public IClientProxy User(string userId) => _proxy;
        public IClientProxy Users(IReadOnlyList<string> userIds) => _proxy;
    }

    private sealed class FakeHubContext : IHubContext<NotificationHub>
    {
        public FakeHubContext(FakeHubClients clients)
        {
            Clients = clients;
        }

        public IHubClients Clients { get; }
        public IGroupManager Groups { get; } = new FakeGroupManager();
    }

    private sealed class FakeGroupManager : IGroupManager
    {
        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeLogger : ILogger<SignalRNotificationPushPublisher>
    {
        public int WarningCalls { get; private set; }

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull
            => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
            {
                WarningCalls++;
            }
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose()
            {
            }
        }
    }
}
