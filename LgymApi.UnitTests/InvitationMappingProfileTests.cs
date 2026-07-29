using FluentAssertions;
using LgymApi.Application.Coaching.Invitations;
using LgymApi.Application.Coaching.Persistence;
using LgymApi.Application.Mapping;
using LgymApi.Application.Mapping.Core;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using Microsoft.Extensions.DependencyInjection;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class InvitationMappingProfileTests
{
    [Test]
    public void InvitationCreationSource_MapsGeneratedValuesToThePendingWriteModel()
    {
        var trainerId = Id<User>.New();
        var traineeId = Id<User>.New();
        var invitationId = Id<TrainerInvitation>.New();
        var createdAt = new DateTimeOffset(2026, 7, 23, 8, 30, 0, TimeSpan.Zero);
        var source = new InvitationCreationSource(
            invitationId,
            trainerId,
            "trainee@example.test",
            traineeId,
            "CODE00000001",
            createdAt.AddDays(7),
            createdAt);
        var services = new ServiceCollection();
        services.AddApplicationMapping(LgymApi.Api.Mapping.MappingAssemblyMarkers.All);

        using var provider = services.BuildServiceProvider();
        var mapper = provider.GetRequiredService<IMapper>();

        var result = mapper.Map<InvitationCreationSource, CoachingInvitationWriteModel>(source, mapper.CreateContext());

        result.Should().Be(new CoachingInvitationWriteModel(
            invitationId,
            trainerId,
            "trainee@example.test",
            traineeId,
            "CODE00000001",
            TrainerInvitationStatus.Pending,
            createdAt.AddDays(7),
            createdAt,
            null));
    }

    [Test]
    public void InvitationResponseSource_MapsLifecycleStateAndOptionalTraineeBinding()
    {
        var invitationId = Id<TrainerInvitation>.New();
        var traineeId = Id<User>.New();
        var respondedAt = new DateTimeOffset(2026, 7, 23, 9, 15, 0, TimeSpan.Zero);
        var source = new InvitationResponseSource(
            invitationId,
            traineeId,
            TrainerInvitationStatus.Accepted,
            respondedAt);
        var services = new ServiceCollection();
        services.AddApplicationMapping(LgymApi.Api.Mapping.MappingAssemblyMarkers.All);

        using var provider = services.BuildServiceProvider();
        var mapper = provider.GetRequiredService<IMapper>();

        var result = mapper.Map<InvitationResponseSource, CoachingInvitationResponseUpdateModel>(source, mapper.CreateContext());

        result.Should().Be(new CoachingInvitationResponseUpdateModel(
            invitationId,
            traineeId,
            TrainerInvitationStatus.Accepted,
            respondedAt));
    }

    [Test]
    public void InvitationActiveLinkSource_MapsGeneratedRelationshipIdsToTheWriteModel()
    {
        var linkId = Id<TrainerTraineeLink>.New();
        var trainerId = Id<User>.New();
        var traineeId = Id<User>.New();
        var source = new InvitationActiveLinkSource(linkId, trainerId, traineeId);
        var services = new ServiceCollection();
        services.AddApplicationMapping(LgymApi.Api.Mapping.MappingAssemblyMarkers.All);

        using var provider = services.BuildServiceProvider();
        var mapper = provider.GetRequiredService<IMapper>();

        var result = mapper.Map<InvitationActiveLinkSource, CoachingActiveLinkWriteModel>(source, mapper.CreateContext());

        result.Should().Be(new CoachingActiveLinkWriteModel(linkId, trainerId, traineeId));
    }
}
