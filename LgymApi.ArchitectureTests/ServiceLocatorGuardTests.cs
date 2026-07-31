namespace LgymApi.ArchitectureTests;

[TestFixture]
public sealed class ServiceLocatorGuardTests
{
    [Test]
    public void Production_Business_Services_And_UseCases_Should_Not_Use_Service_Location()
    {
        var (repoRoot, compilation, syntaxTrees) = ArchitectureTestHelpers.PrepareCompilation(
            "LgymApi.Application",
            "LgymApi.Identity",
            "LgymApi.Notifications");

        var violations = BusinessServiceDependencyAnalyzer
            .Analyze(compilation, syntaxTrees, repoRoot)
            .Where(violation => violation.Kind != BusinessServiceDependencyViolationKind.DependencyAggregate)
            .ToList();

        Assert.That(
            violations,
            Is.Empty,
            "Application, Identity, and Notifications business services/use cases must not resolve services or create scopes." + Environment.NewLine +
            string.Join(Environment.NewLine, violations));
    }

    [TestCase("IServiceProvider")]
    [TestCase("IServiceScopeFactory")]
    public void Regular_Constructors_Should_Reject_Service_Locator_Types(string locatorType)
    {
        var source = $$"""
            using System;
            using Microsoft.Extensions.DependencyInjection;

            namespace Example;

            public sealed class CheckoutService
            {
                public CheckoutService({{locatorType}} locator)
                {
                }
            }
            """;

        var violations = BusinessServiceDependencyFixture.Analyze(source);

        AssertLocatorTypeViolation(violations, "CheckoutService", locatorType);
    }

    [Test]
    public void Primary_Constructor_Should_Reject_Service_Provider()
    {
        const string source = """
            using System;

            namespace Example;

            public sealed class CheckoutUseCase(IServiceProvider serviceProvider)
            {
            }
            """;

        var violations = BusinessServiceDependencyFixture.Analyze(source);

        AssertLocatorTypeViolation(violations, "CheckoutUseCase", "IServiceProvider");
    }

    [Test]
    public void Partial_Primary_Constructor_Should_Reject_Scope_Factory()
    {
        const string primaryPart = """
            using Microsoft.Extensions.DependencyInjection;

            namespace Example;

            public sealed partial class CheckoutService(IServiceScopeFactory scopeFactory)
            {
            }
            """;
        const string secondPart = """
            namespace Example;

            public sealed partial class CheckoutService
            {
                public void Execute()
                {
                }
            }
            """;

        var violations = BusinessServiceDependencyFixture.Analyze(primaryPart, secondPart);

        AssertLocatorTypeViolation(violations, "CheckoutService", "IServiceScopeFactory");
    }

    [TestCase("ProviderHolder.Current.GetService(typeof(IRepository))", "System.IServiceProvider.GetService(System.Type)")]
    [TestCase("ProviderHolder.Current.GetRequiredService<IRepository>()", "GetRequiredService")]
    [TestCase("ProviderHolder.Current.CreateScope()", "CreateScope")]
    [TestCase("ProviderHolder.Current.CreateAsyncScope()", "CreateAsyncScope")]
    [TestCase("ProviderHolder.ScopeFactory.CreateScope()", "CreateScope")]
    [TestCase("ActivatorUtilities.CreateInstance<Repository>(ProviderHolder.Current)", "ActivatorUtilities.CreateInstance")]
    public void Business_Service_Locator_Invocations_Should_Be_Rejected(
        string invocation,
        string expectedDependency)
    {
        var source = $$"""
            using System;
            using Microsoft.Extensions.DependencyInjection;

            namespace Example;

            public interface IRepository
            {
            }

            public sealed class Repository : IRepository
            {
            }

            public static class ProviderHolder
            {
                public static IServiceProvider Current => null!;
                public static IServiceScopeFactory ScopeFactory => null!;
            }

            public sealed class CheckoutService
            {
                public object Execute()
                {
                    return {{invocation}};
                }
            }
            """;

        var violations = BusinessServiceDependencyFixture.Analyze(source);
        var invocationViolations = violations
            .Where(violation => violation.Kind == BusinessServiceDependencyViolationKind.ServiceLocatorInvocation)
            .ToList();
        Assert.That(invocationViolations, Has.Count.EqualTo(1));
        var invocationViolation = invocationViolations.Single();

        Assert.Multiple(() =>
        {
            Assert.That(invocationViolation.ServiceName, Is.EqualTo("CheckoutService"));
            Assert.That(invocationViolation.Dependency, Does.Contain(expectedDependency));
        });
    }

    [Test]
    public void Custom_Methods_With_Locator_Like_Names_Should_Not_Be_Rejected()
    {
        const string source = """
            namespace Example;

            public interface IRepository
            {
            }

            public interface IResolver
            {
                IRepository GetRequiredService();
                object CreateScope();
            }

            public sealed class CheckoutService(IResolver resolver)
            {
                public IRepository Execute()
                {
                    _ = resolver.CreateScope();
                    return resolver.GetRequiredService();
                }
            }
            """;

        var violations = BusinessServiceDependencyFixture.Analyze(source);

        Assert.That(violations, Is.Empty);
    }

    [Test]
    public void Composition_Factory_Delegate_Should_Be_Allowed_By_Containing_Type_Location()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;

            namespace Example;

            public interface IRepository
            {
            }

            public sealed class CheckoutService(IRepository repository)
            {
            }

            public static class CompositionRoot
            {
                public static IServiceCollection AddCheckout(this IServiceCollection services)
                {
                    return services.AddScoped<CheckoutService>(provider =>
                        new CheckoutService(provider.GetRequiredService<IRepository>()));
                }
            }
            """;

        var violations = BusinessServiceDependencyFixture.Analyze(source);

        Assert.That(violations, Is.Empty);
    }

    private static void AssertLocatorTypeViolation(
        IReadOnlyList<BusinessServiceDependencyViolation> violations,
        string serviceName,
        string locatorType)
    {
        var typeViolations = violations
            .Where(violation => violation.Kind == BusinessServiceDependencyViolationKind.ServiceLocatorType)
            .ToList();
        Assert.That(typeViolations, Has.Count.EqualTo(1));
        var typeViolation = typeViolations.Single();

        Assert.Multiple(() =>
        {
            Assert.That(typeViolation.ServiceName, Is.EqualTo(serviceName));
            Assert.That(typeViolation.Dependency, Does.Contain(locatorType));
        });
    }
}
