using LgymApi.Application.Abstractions.Storage;
using LgymApi.Application.Features.Reporting;
using LgymApi.Application.Options;
using LgymApi.Application.Platform.Contracts.BackgroundCommands;
using LgymApi.Application.Reporting.Persistence;
using LgymApi.Application.Repositories;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using LgymApi.Identity.Contracts.Accounts;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace LgymApi.UnitTests;

internal static class PhotoServiceTestFactory
{
    public static AuthenticatedAccountContext CreateUser(Id<User> id, string email)
        => ReportingTestData.Account(id, email.Contains("trainer", StringComparison.OrdinalIgnoreCase));

    public static ReportRequest CreateReportRequest(Id<ReportRequest> id, Id<User> traineeId)
    {
        var template = new ReportTemplate
        {
            Id = Id<ReportTemplate>.New(),
            TrainerId = Id<User>.New(),
            Name = "Photo report",
            Fields = []
        };
        return new ReportRequest
        {
            Id = id,
            TraineeId = traineeId,
            TrainerId = template.TrainerId,
            TemplateId = template.Id,
            Template = template,
            Status = ReportRequestStatus.Pending,
            IsDeleted = false
        };
    }

    public static PendingPhotoUpload CreatePendingUpload(
        string storageKey,
        Id<User> initiatedBy,
        Id<User> owner,
        Id<ReportRequest> requestId,
        string viewType,
        string contentType,
        long sizeBytes)
        => new(
            Id<PhotoUploadSession>.New(),
            storageKey,
            ReportingTestData.AccountId(initiatedBy),
            ReportingTestData.AccountId(owner),
            requestId,
            viewType,
            contentType,
            sizeBytes,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddMinutes(10),
            null,
            null,
            PhotoUploadSessionStatus.Pending,
            null);

    public static IReportingService CreateService(
        Func<Id<ReportRequest>, CancellationToken, Task<ReportRequest?>>? findRequestById = null,
        Func<Id<User>, Id<User>, CancellationToken, Task<bool>>? relationshipAccess = null,
        IPhotoStorageProvider? photoStorageProvider = null,
        IReportPhotoPersistence? reportingRepository = null,
        PendingPhotoUpload? pendingUpload = null,
        IUnitOfWork? unitOfWork = null,
        IReportPhotoPersistence? photoUploadInitTracker = null,
        PhotoStorageOptions? photoStorageOptions = null)
    {
        var requestPersistence = Substitute.For<IReportRequestSubmissionPersistence>();
        if (findRequestById != null)
        {
            requestPersistence.FindRequestByIdAsync(Arg.Any<Id<ReportRequest>>(), Arg.Any<CancellationToken>())
                .Returns(async callInfo =>
                {
                    var request = await findRequestById(callInfo.ArgAt<Id<ReportRequest>>(0), callInfo.ArgAt<CancellationToken>(1));
                    return request is null ? null : ReportingTestData.Request(request);
                });
        }

        var relationshipPersistence = Substitute.For<IReportingRelationshipAccessPersistence>();
        relationshipPersistence.GetAccessAsync(
                Arg.Any<Id<AccountReference>>(),
                Arg.Any<Id<AccountReference>>(),
                Arg.Any<CancellationToken>())
            .Returns(async callInfo =>
            {
                var hasAccess = relationshipAccess is not null
                    && await relationshipAccess(
                        callInfo.ArgAt<Id<AccountReference>>(0).Rebind<User>(),
                        callInfo.ArgAt<Id<AccountReference>>(1).Rebind<User>(),
                        callInfo.ArgAt<CancellationToken>(2));
                return new ReportingRelationshipAccessFact(hasAccess);
            });

        var photoPersistence = reportingRepository ?? photoUploadInitTracker ?? Substitute.For<IReportPhotoPersistence>();
        photoPersistence.CountRecentUploadInitsAsync(
                Arg.Any<Id<AccountReference>>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns(0);
        photoPersistence.FindUploadSessionAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => pendingUpload != null
                && string.Equals(pendingUpload.StorageKey, callInfo.ArgAt<string>(0), StringComparison.Ordinal)
                    ? pendingUpload
                    : null);

        var dependencies = Substitute.For<IReportingServiceDependencies>();
        dependencies.TemplatePersistence.Returns(Substitute.For<IReportTemplatePersistence>());
        dependencies.RequestSubmissionPersistence.Returns(requestPersistence);
        dependencies.RecurringAssignmentPersistence.Returns(Substitute.For<IRecurringReportAssignmentPersistence>());
        dependencies.PhotoPersistence.Returns(photoPersistence);
        dependencies.RelationshipAccessPersistence.Returns(relationshipPersistence);
        dependencies.ReportSubmissionAcceptedProgressCommandFactory.Returns(new ReportSubmissionAcceptedProgressCommandFactory());
        dependencies.CommandDispatcher.Returns(Substitute.For<ICommandDispatcher>());
        dependencies.CommandOutboxWriter.Returns(Substitute.For<ICommandOutboxWriter>());
        dependencies.UnitOfWork.Returns(unitOfWork ?? Substitute.For<IUnitOfWork>());
        dependencies.PhotoStorageProvider.Returns(photoStorageProvider ?? Substitute.For<IPhotoStorageProvider>());
        dependencies.Mapper.Returns(ReportingTestData.Mapper());
        dependencies.Logger.Returns(Substitute.For<ILogger<ReportingService>>());
        dependencies.PhotoStorageOptions.Returns(photoStorageOptions ?? new PhotoStorageOptions());

        return new ReportingService(dependencies);
    }
}
