using FluentAssertions;
using LgymApi.Domain.ValueObjects;
using LgymApi.Domain.Entities;
using System.Reflection;
using System.Text.Json;
using LgymApi.Api.Features.Trainer.Contracts;
using LgymApi.Api.Mapping.Profiles;
using LgymApi.Application.Features.Reporting.Models;
using LgymApi.Application.Mapping.Core;
using LgymApi.Domain.Enums;
using LgymApi.Identity.Contracts;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class ReportingProfileTests
{
    [Test]
    public void ReportingProfile_MapsTemplateRequestAndSubmission_WithExpectedValues()
    {
        var mapper = CreateMapper(new ReportingProfile());

        var templateResult = new ReportTemplateResult
        {
            Id = Id<LgymApi.Domain.Entities.ReportTemplate>.New(),
            TrainerId = Id<AccountReference>.New(),
            Name = "Weekly",
            Fields =
            [
                new ReportTemplateFieldResult { Key = "weight", Label = "Weight", Type = ReportFieldType.Number, IsRequired = true, Order = 0 }
            ]
        };

        var templateDto = mapper.Map<ReportTemplateResult, ReportTemplateDto>(templateResult);
        templateDto.Fields.Should().HaveCount(1);

        var requestResult = new ReportRequestResult
        {
            Id = Id<LgymApi.Domain.Entities.ReportRequest>.New(),
            TrainerId = Id<AccountReference>.New(),
            TraineeId = Id<AccountReference>.New(),
            TemplateId = Id<LgymApi.Domain.Entities.ReportTemplate>.New(),
            Status = ReportRequestStatus.Pending,
            Template = templateResult
        };

        var requestDto = mapper.Map<ReportRequestResult, ReportRequestDto>(requestResult);
        requestDto.Template.Should().NotBeNull();

        var submissionResult = new ReportSubmissionResult
        {
            Id = Id<LgymApi.Domain.Entities.ReportSubmission>.New(),
            ReportRequestId = Id<LgymApi.Domain.Entities.ReportRequest>.New(),
            TraineeId = Id<AccountReference>.New(),
            Answers = new Dictionary<string, JsonElement>(),
            TrainerOverallComment = "Overall coach feedback",
            TrainerFieldComments = new Dictionary<string, string> { ["weight"] = "Add more context" },
            Request = requestResult
        };

        var submissionDto = mapper.Map<ReportSubmissionResult, ReportSubmissionDto>(submissionResult);
        submissionDto.Answers.Should().BeEmpty();
        submissionDto.Request.Should().NotBeNull();
        submissionDto.TrainerOverallComment.Should().Be("Overall coach feedback");
        submissionDto.TrainerFieldComments["weight"].Should().Be("Add more context");
    }

    [Test]
    public void ReportingProfile_MapsSubmissionAnswers_AsProvidedDictionary()
    {
        var mapper = CreateMapper(new ReportingProfile());

        var submissionResult = new ReportSubmissionResult
        {
            Id = Id<LgymApi.Domain.Entities.ReportSubmission>.New(),
            ReportRequestId = Id<LgymApi.Domain.Entities.ReportRequest>.New(),
            TraineeId = Id<AccountReference>.New(),
            Answers = new Dictionary<string, JsonElement>
            {
                ["Weight"] = JsonSerializer.SerializeToElement(82)
            },
            Request = new ReportRequestResult
            {
                Id = Id<LgymApi.Domain.Entities.ReportRequest>.New(),
                TrainerId = Id<AccountReference>.New(),
                TraineeId = Id<AccountReference>.New(),
                TemplateId = Id<LgymApi.Domain.Entities.ReportTemplate>.New(),
                Status = ReportRequestStatus.Submitted,
                Template = new ReportTemplateResult()
            }
        };

        var dto = mapper.Map<ReportSubmissionResult, ReportSubmissionDto>(submissionResult);
        dto.Answers.ContainsKey("Weight").Should().BeTrue();
    }

    [Test]
    public void ReportingProfile_MapsPhotoDtosAndNullSources_WithSafeDefaults()
    {
        var mapper = CreateMapper(new ReportingProfile());

        var uploadDto = mapper.Map<InitiatePhotoUploadResult, InitiatePhotoUploadResponse>(new InitiatePhotoUploadResult
        {
            UploadUrl = "https://upload.example.com",
            StorageKey = "photos/key.jpg",
            ExpiresAt = DateTimeOffset.UtcNow
        });
        uploadDto.UploadUrl.Should().Be("https://upload.example.com");

        var readDto = mapper.Map<SignedReadUrlResult, GetSignedReadUrlResponse>(new SignedReadUrlResult
        {
            ReadUrl = "https://read.example.com",
            ExpiresAt = DateTimeOffset.UtcNow
        });
        readDto.ReadUrl.Should().Be("https://read.example.com");

        var photoHistoryDto = mapper.Map<PhotoHistoryItemResult, PhotoHistoryItemResponse>(new PhotoHistoryItemResult
        {
            Id = Id<Photo>.New(),
            ViewType = "Front",
            SizeBytes = 123,
            ThumbnailUrl = "thumb",
            ReadUrl = "read",
            ReportRequestId = Id<ReportRequest>.New(),
            UploadedAt = DateTimeOffset.UtcNow
        });
        photoHistoryDto.ViewType.Should().Be("Front");

        mapper.Map<ReportTemplateResult, ReportTemplateDto>(new ReportTemplateResult()).Should().NotBeNull();
        mapper.Map<ReportRequestResult, ReportRequestDto>(new ReportRequestResult()).Should().NotBeNull();
        mapper.Map<ReportSubmissionResult, ReportSubmissionDto>(new ReportSubmissionResult()).Should().NotBeNull();
    }

    private static IMapper CreateMapper(params IMappingProfile[] profiles)
    {
        return (IMapper)Activator.CreateInstance(
            typeof(Mapper),
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            args: [profiles],
            culture: null)!;
    }
}
