using FluentAssertions;
using LgymApi.Application.Coaching.Contracts.Notifications;
using LgymApi.Application.Coaching.Notifications;
using LgymApi.Application.Coaching.Persistence;
using LgymApi.Application.Mapping;
using LgymApi.Application.Mapping.Core;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class CoachingNotificationReadMappingProfileTests
{
    [Test]
    public void CoachingInvitationFact_MapsOnlyTheNotificationFactsNeededByTheIntentAdapters()
    {
        var invitationId = Id<TrainerInvitation>.New();
        var trainerId = Id<User>.New();
        var traineeId = Id<User>.New();
        var expiresAt = new DateTimeOffset(2026, 7, 23, 8, 30, 0, TimeSpan.Zero);
        var source = new CoachingInvitationFact(
            invitationId,
            trainerId,
            "trainee@example.test",
            traineeId,
            "CODE00000001",
            TrainerInvitationStatus.Pending,
            expiresAt,
            null,
            expiresAt.AddDays(-1),
            expiresAt.AddDays(-1));
        var services = new ServiceCollection();
        services.AddApplicationMapping(LgymApi.Api.Mapping.MappingAssemblyMarkers.All);

        using var provider = services.BuildServiceProvider();
        var mapper = provider.GetRequiredService<IMapper>();

        var result = mapper.Map<CoachingInvitationFact, CoachingInvitationNotificationFact>(source, mapper.CreateContext());

        result.Should().Be(new CoachingInvitationNotificationFact(
            invitationId,
            trainerId,
            traineeId,
            "trainee@example.test",
            "CODE00000001",
            expiresAt));
    }

    [Test]
    public async Task GetInvitationAsync_MapsThePersistenceFactToThePublicNotificationFact()
    {
        var invitationId = Id<TrainerInvitation>.New();
        var trainerId = Id<User>.New();
        var traineeId = Id<User>.New();
        var expiresAt = new DateTimeOffset(2026, 7, 23, 8, 30, 0, TimeSpan.Zero);
        var persistence = Substitute.For<ICoachingInvitationPersistence>();
        persistence.FindByIdAsync(invitationId, Arg.Any<CancellationToken>()).Returns(Task.FromResult<CoachingInvitationFact?>(new CoachingInvitationFact(
            invitationId,
            trainerId,
            "trainee@example.test",
            traineeId,
            "CODE00000001",
            TrainerInvitationStatus.Pending,
            expiresAt,
            null,
            expiresAt.AddDays(-1),
            expiresAt.AddDays(-1))));
        var services = new ServiceCollection();
        services.AddApplicationMapping(LgymApi.Api.Mapping.MappingAssemblyMarkers.All);

        using var provider = services.BuildServiceProvider();
        var service = new CoachingNotificationReadService(
            persistence,
            provider.GetRequiredService<IMapper>());

        var result = await service.GetInvitationAsync(invitationId);

        result.Should().Be(new CoachingInvitationNotificationFact(
            invitationId,
            trainerId,
            traineeId,
            "trainee@example.test",
            "CODE00000001",
            expiresAt));
    }
}
