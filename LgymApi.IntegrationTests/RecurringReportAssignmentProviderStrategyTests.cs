using FluentAssertions;
using LgymApi.Infrastructure.Repositories.Reporting;

namespace LgymApi.IntegrationTests;

[TestFixture]
public sealed class RecurringReportAssignmentProviderStrategyTests
{
    [Test]
    public void SelectLockProvider_ForUnknownRelationalProvider_FailsClosed()
    {
        var action = () => RecurringReportAssignmentPersistenceRepository.SelectLockProvider(
            isRelational: true,
            providerName: "Example.Relational.Provider");

        action.Should().Throw<NotSupportedException>();
    }
}
