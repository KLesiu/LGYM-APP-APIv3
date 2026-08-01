using System.Reflection;

namespace LgymApi.ArchitectureTests;

[TestFixture]
public sealed class ServiceDependencyAggregateGuardTests
{
    [TestCase("IReportingServiceDependencies")]
    [TestCase("UserSessionTerminationServiceDependencies")]
    [TestCase("IPushNotificationDeliveryServiceDependencies")]
    [TestCase("CheckoutDependencies")]
    [TestCase("CheckoutDependencyBag")]
    [TestCase("CheckoutDependencyAggregate")]
    public void Regular_Constructors_Should_Reject_Dependency_Aggregates(string aggregateTypeName)
    {
        var source = $$"""
            namespace Example;

            public interface {{aggregateTypeName}}
            {
            }

            public sealed class CheckoutService
            {
                public CheckoutService({{aggregateTypeName}} dependencies)
                {
                }
            }
            """;

        var violations = BusinessServiceDependencyFixture.Analyze(source);

        AssertAggregateViolation(violations, "CheckoutService", aggregateTypeName);
    }

    [Test]
    public void Primary_Constructor_Should_Reject_Renamed_Dependency_Bag()
    {
        const string source = """
            namespace Example;

            public interface RenamedDependencyBag
            {
            }

            public sealed class CheckoutUseCase(RenamedDependencyBag dependencies)
            {
            }
            """;

        var violations = BusinessServiceDependencyFixture.Analyze(source);

        AssertAggregateViolation(violations, "CheckoutUseCase", "RenamedDependencyBag");
    }

    [Test]
    public void Partial_Primary_Constructor_Should_Reject_Dependency_Aggregate()
    {
        const string primaryPart = """
            namespace Example;

            public interface CheckoutDependencyAggregate
            {
            }

            public sealed partial class CheckoutService(CheckoutDependencyAggregate dependencies)
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

        AssertAggregateViolation(violations, "CheckoutService", "CheckoutDependencyAggregate");
    }

    [Test]
    public void High_Arity_Direct_Injection_Should_Be_Allowed_Without_A_Numeric_Limit()
    {
        const string source = """
            namespace Example;

            public interface IFirst { }
            public interface ISecond { }
            public interface IThird { }
            public interface IFourth { }
            public interface IFifth { }
            public interface ISixth { }
            public interface ISeventh { }
            public interface IEighth { }

            public sealed class CheckoutService(
                IFirst first,
                ISecond second,
                IThird third,
                IFourth fourth,
                IFifth fifth,
                ISixth sixth,
                ISeventh seventh,
                IEighth eighth)
            {
            }
            """;

        var violations = BusinessServiceDependencyFixture.Analyze(source);

        Assert.That(violations, Is.Empty);
    }

    [Test]
    public void Guard_Should_Not_Define_A_Constructor_Parameter_Count_Constant()
    {
        var guardTypes = new[]
        {
            typeof(ServiceDependencyAggregateGuardTests),
            typeof(BusinessServiceDependencyAnalyzer)
        };
        var numericConstants = guardTypes
            .SelectMany(type => type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            .Where(field => field.IsLiteral && field.FieldType == typeof(int))
            .ToList();

        Assert.That(numericConstants, Is.Empty);
    }

    private static void AssertAggregateViolation(
        IReadOnlyList<BusinessServiceDependencyViolation> violations,
        string serviceName,
        string aggregateTypeName)
    {
        Assert.That(violations, Has.Count.EqualTo(1));
        var violation = violations.Single();

        Assert.Multiple(() =>
        {
            Assert.That(violation.Kind, Is.EqualTo(BusinessServiceDependencyViolationKind.DependencyAggregate));
            Assert.That(violation.ServiceName, Is.EqualTo(serviceName));
            Assert.That(violation.Dependency, Is.EqualTo(aggregateTypeName));
            Assert.That(violation.Reason, Does.Contain("expose each dependency directly"));
        });
    }
}
