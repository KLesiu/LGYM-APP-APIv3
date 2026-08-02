using System.Globalization;
using FluentAssertions;
using LgymApi.Resources;

namespace LgymApi.UnitTests;

[TestFixture]
[NonParallelizable]
public sealed class RecurringReportResourceTests
{
    [TestCase(
        "en-US",
        "The current recurring report request must be completed before another request can be created.",
        "This recurring report cycle is not active at this time.",
        "The report template for this recurring report cycle is unavailable.")]
    [TestCase(
        "pl-PL",
        "Bieżąca prośba o raport okresowy musi zostać zakończona przed utworzeniem kolejnej.",
        "Ten cykl raportu okresowego nie jest teraz aktywny.",
        "Szablon raportu dla tego cyklu raportu okresowego jest niedostępny.")]
    public void GeneratedAccessors_ReturnExactLocalizedRecurringReportMessages(
        string cultureName,
        string requestInProgress,
        string assignmentUnavailable,
        string templateUnavailable)
    {
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(cultureName);

            Messages.RecurringReportRequestInProgress.Should().Be(requestInProgress);
            Messages.RecurringReportAssignmentUnavailable.Should().Be(assignmentUnavailable);
            Messages.RecurringReportTemplateUnavailable.Should().Be(templateUnavailable);
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }
}
