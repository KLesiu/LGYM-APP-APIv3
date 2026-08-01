using FluentAssertions;
using LgymApi.Application.Platform.Contracts.BackgroundCommands;
using LgymApi.BackgroundWorker;
using LgymApi.BackgroundWorker.Actions.Contracts;
using LgymApi.BackgroundWorker.Runtime;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class ActionScopeIsolationTests
{
    [Test]
    public void BackgroundActionResolver_ReturnsExactHandlerNames()
    {
        using var provider = BuildProvider();
        var resolver = provider.GetRequiredService<IBackgroundActionResolver>();

        resolver.GetHandlerTypeNames(typeof(TestCommand))
            .Should()
            .ContainSingle()
            .Which.Should().Be(typeof(ScopedHandler).FullName);
    }

    [Test]
    public void BackgroundActionResolver_CreatesIsolatedResolutionScopes()
    {
        using var provider = BuildProvider();
        var resolver = provider.GetRequiredService<IBackgroundActionResolver>();

        using var firstScope = resolver.CreateScope(typeof(TestCommand));
        using var secondScope = resolver.CreateScope(typeof(TestCommand));
        var firstHandler = (ScopedHandler)firstScope.ResolveHandler(typeof(ScopedHandler).FullName!);
        var secondHandler = (ScopedHandler)secondScope.ResolveHandler(typeof(ScopedHandler).FullName!);

        firstHandler.Tracker.Should().NotBeSameAs(secondHandler.Tracker);
    }

    [Test]
    public void BackgroundActionResolver_RejectsUnknownHandlerName()
    {
        using var provider = BuildProvider();
        var resolver = provider.GetRequiredService<IBackgroundActionResolver>();
        using var scope = resolver.CreateScope(typeof(TestCommand));

        var action = () => scope.ResolveHandler("Missing.Handler");

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*Missing.Handler*");
    }

    [Test]
    public void BackgroundActionResolver_ExposesNoServiceProvider()
    {
        typeof(IBackgroundActionResolver).GetProperties()
            .Should().NotContain(property => property.PropertyType == typeof(IServiceProvider));
        typeof(IBackgroundActionResolutionScope).GetProperties()
            .Should().NotContain(property => property.PropertyType == typeof(IServiceProvider));
    }

    private static Microsoft.Extensions.DependencyInjection.ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddScoped<ScopedTracker>();
        services.AddScoped<IBackgroundAction<TestCommand>, ScopedHandler>();
        services.AddScoped<IBackgroundActionResolver, BackgroundActionResolver>();
        return services.BuildServiceProvider();
    }

    private sealed class TestCommand : IActionCommand
    {
    }

    private sealed class ScopedTracker
    {
    }

    private sealed class ScopedHandler : IBackgroundAction<TestCommand>
    {
        public ScopedHandler(ScopedTracker tracker)
        {
            Tracker = tracker;
        }

        public ScopedTracker Tracker { get; }

        public Task ExecuteAsync(TestCommand command, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
