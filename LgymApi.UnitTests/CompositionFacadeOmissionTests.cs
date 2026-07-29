using FluentAssertions;
using LgymApi.Application.Features.Reporting;
using LgymApi.Application.Identity.ApiCompatibility;
using LgymApi.Application.Notifications;
using LgymApi.Application.Platform.ReferenceData.AppConfig;
using LgymApi.Application.Services;
using LgymApi.Application.Task7ApiCompatibility;
using LgymApi.Application.TrainingPlanning.Contracts.PlanDay;
using LgymApi.BackgroundWorker.Runtime;
using LgymApi.Identity.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class CompositionFacadeOmissionTests
{
    private static readonly FacadeOmissionExpectation[] OmissionExpectations =
    [
        new(CompositionFacade.Platform, typeof(IAppConfigService)),
        new(CompositionFacade.Identity, typeof(ITokenService)),
        new(CompositionFacade.TrainingPlanning, typeof(IPlanDayService)),
        new(CompositionFacade.Notifications, typeof(IInAppNotificationService)),
        new(CompositionFacade.Application, typeof(IReportingService)),
        new(CompositionFacade.Infrastructure, typeof(LgymApi.Application.Repositories.IUnitOfWork)),
        new(CompositionFacade.ApplicationApiAdapters, typeof(IAppConfigApiCompatibilityAdapter)),
        new(CompositionFacade.NotificationsApiAdapters, typeof(IAccountPushInstallationApiAdapter)),
        new(CompositionFacade.Worker, typeof(CommandContractRegistry))
    ];

    [TestCaseSource(nameof(OmissionExpectations))]
    public void OmittedFacade_FailsWithItsTargetedMissingService(FacadeOmissionExpectation expectation)
    {
        var services = CompositionRootTestHost.Create(expectation.Facade);
        using var provider = services.BuildServiceProvider();

        var action = () => provider.GetRequiredService(expectation.MissingServiceType);

        action.Should().Throw<InvalidOperationException>()
            .WithMessage($"*{expectation.MissingServiceType.FullName}*");
    }

    public sealed record FacadeOmissionExpectation(
        CompositionFacade Facade,
        Type MissingServiceType)
    {
        public override string ToString() => Facade.ToString();
    }
}
