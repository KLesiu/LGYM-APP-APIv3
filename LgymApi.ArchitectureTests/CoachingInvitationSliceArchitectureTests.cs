using System.Reflection;
using FluentAssertions;
using LgymApi.Application.Coaching;
using LgymApi.Application.Coaching.Invitations.Accept;
using LgymApi.Application.Coaching.Invitations.Create;
using LgymApi.Application.Coaching.Invitations.CreateByEmail;
using LgymApi.Application.Coaching.Invitations.List;
using LgymApi.Application.Coaching.Invitations.ListPaginated;
using LgymApi.Application.Coaching.Invitations.PublicStatus;
using LgymApi.Application.Coaching.Invitations.Reject;
using LgymApi.Application.Coaching.Invitations.Revoke;
using Microsoft.Extensions.DependencyInjection;

namespace LgymApi.ArchitectureTests;

[TestFixture]
public sealed class CoachingInvitationSliceArchitectureTests
{
    private static readonly (Type Contract, string ImplementationName)[] Slices =
    [
        (typeof(ICreateInvitationUseCase), "LgymApi.Application.Coaching.Invitations.Create.CreateInvitationUseCase"),
        (typeof(ICreateInvitationByEmailUseCase), "LgymApi.Application.Coaching.Invitations.CreateByEmail.CreateInvitationByEmailUseCase"),
        (typeof(IListInvitationsUseCase), "LgymApi.Application.Coaching.Invitations.List.ListInvitationsUseCase"),
        (typeof(IListPaginatedInvitationsUseCase), "LgymApi.Application.Coaching.Invitations.ListPaginated.ListPaginatedInvitationsUseCase"),
        (typeof(IPublicInvitationStatusUseCase), "LgymApi.Application.Coaching.Invitations.PublicStatus.PublicInvitationStatusUseCase"),
        (typeof(IAcceptInvitationUseCase), "LgymApi.Application.Coaching.Invitations.Accept.AcceptInvitationUseCase"),
        (typeof(IRejectInvitationUseCase), "LgymApi.Application.Coaching.Invitations.Reject.RejectInvitationUseCase"),
        (typeof(IRevokeInvitationUseCase), "LgymApi.Application.Coaching.Invitations.Revoke.RevokeInvitationUseCase")
    ];

    [Test]
    public void InvitationSlices_ExposeOnlyPublicContractsWithInternalImplementations()
    {
        var assembly = typeof(ICreateInvitationUseCase).Assembly;

        foreach (var slice in Slices)
        {
            slice.Contract.IsPublic.Should().BeTrue();
            slice.Contract.GetMethods(BindingFlags.Public | BindingFlags.Instance).Should().ContainSingle();
            assembly.GetType(slice.ImplementationName)!.IsNotPublic.Should().BeTrue();
        }
    }

    [Test]
    public void InvitationSlices_AreRegisteredExactlyOnceByTheCoachingModule()
    {
        var services = new ServiceCollection();
        services.AddCoachingModule();

        foreach (var slice in Slices)
        {
            services.Count(descriptor => descriptor.ServiceType == slice.Contract).Should().Be(1);
        }
    }

    [Test]
    public void InvitationSlices_DoNotReintroduceLegacyUserOrRelationshipRepositories()
    {
        var root = ArchitectureTestHelpers.ResolveRepositoryRoot();
        var invitationDirectory = Path.Combine(root, "LgymApi.Application", "Coaching", "Invitations");
        var source = Directory.GetFiles(invitationDirectory, "*.cs", SearchOption.AllDirectories)
            .Select(File.ReadAllText)
            .ToArray();

        source.Should().NotContain(text => text.Contains("IUserRepository", StringComparison.Ordinal));
        source.Should().NotContain(text => text.Contains("ITrainerRelationshipRepository", StringComparison.Ordinal));
    }

    [Test]
    public void InvitationCreateSlices_DelegateWriteModelTransformationToTheRegisteredMapper()
    {
        var root = ArchitectureTestHelpers.ResolveRepositoryRoot();
        var createUseCases = new[]
        {
            Path.Combine(root, "LgymApi.Application", "Coaching", "Invitations", "Create", "CreateInvitationUseCase.cs"),
            Path.Combine(root, "LgymApi.Application", "Coaching", "Invitations", "CreateByEmail", "CreateInvitationByEmailUseCase.cs")
        };
        var mappingProfile = Path.Combine(root, "LgymApi.Application", "Coaching", "Invitations", "InvitationMappingProfile.cs");

        foreach (var useCase in createUseCases)
        {
            var source = File.ReadAllText(useCase);
            source.Should().NotContain("new CoachingInvitationWriteModel(");
            source.Should().Contain("_mapper.Map<InvitationCreationSource, CoachingInvitationWriteModel>(");
            source.Should().Contain("_mapper.CreateContext()");
        }

        File.ReadAllText(mappingProfile).Should().Contain("CreateMap<InvitationCreationSource, CoachingInvitationWriteModel>");
    }

    [Test]
    public void InvitationLifecycleSlices_DelegateResponseUpdatesToTheRegisteredMapper()
    {
        var root = ArchitectureTestHelpers.ResolveRepositoryRoot();
        var lifecycleUseCases = new[]
        {
            Path.Combine(root, "LgymApi.Application", "Coaching", "Invitations", "Accept", "AcceptInvitationUseCase.cs"),
            Path.Combine(root, "LgymApi.Application", "Coaching", "Invitations", "Reject", "RejectInvitationUseCase.cs"),
            Path.Combine(root, "LgymApi.Application", "Coaching", "Invitations", "Revoke", "RevokeInvitationUseCase.cs")
        };
        var mappingProfile = Path.Combine(root, "LgymApi.Application", "Coaching", "Invitations", "InvitationMappingProfile.cs");

        foreach (var useCase in lifecycleUseCases)
        {
            var source = File.ReadAllText(useCase);
            source.Should().NotContain("new CoachingInvitationResponseUpdateModel(");
            source.Should().Contain("_mapper.Map<InvitationResponseSource, CoachingInvitationResponseUpdateModel>(");
            source.Should().Contain("_mapper.CreateContext()");
        }

        File.ReadAllText(mappingProfile).Should().Contain("CreateMap<InvitationResponseSource, CoachingInvitationResponseUpdateModel>");
    }

    [Test]
    public void InvitationAcceptSlice_DelegatesActiveLinkWriteTransformationToTheRegisteredMapper()
    {
        var root = ArchitectureTestHelpers.ResolveRepositoryRoot();
        var useCase = Path.Combine(root, "LgymApi.Application", "Coaching", "Invitations", "Accept", "AcceptInvitationUseCase.cs");
        var mappingProfile = Path.Combine(root, "LgymApi.Application", "Coaching", "Invitations", "InvitationMappingProfile.cs");
        var source = File.ReadAllText(useCase);

        source.Should().NotContain("new CoachingActiveLinkWriteModel(");
        source.Should().Contain("_mapper.Map<InvitationActiveLinkSource, CoachingActiveLinkWriteModel>(");
        source.Should().Contain("_mapper.CreateContext()");
        File.ReadAllText(mappingProfile).Should().Contain("CreateMap<InvitationActiveLinkSource, CoachingActiveLinkWriteModel>");
    }
}
