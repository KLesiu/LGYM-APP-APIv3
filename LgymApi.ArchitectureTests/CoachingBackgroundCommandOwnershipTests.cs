using FluentAssertions;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LgymApi.ArchitectureTests;

[TestFixture]
public sealed class CoachingBackgroundCommandOwnershipTests
{
    private const string CommandsDirectory = "LgymApi.Application/Coaching/Contracts/BackgroundCommands";
    private const string CommandsNamespace = "LgymApi.Application.Coaching.Contracts.BackgroundCommands";

    [Test]
    public void Coaching_Background_Commands_Have_Exact_Owner_Path_Namespace_And_Declaration_Shape()
    {
        var repoRoot = ArchitectureTestHelpers.ResolveRepositoryRoot();
        var commandsDirectory = Path.Combine(repoRoot, CommandsDirectory);
        var expected = ExpectedCommands;

        Directory.Exists(commandsDirectory).Should().BeTrue();
        Directory.GetFiles(commandsDirectory, "*.cs")
            .Select(Path.GetFileName)
            .OrderBy(fileName => fileName, StringComparer.Ordinal)
            .Should().Equal(expected.Select(command => command.FileName).OrderBy(fileName => fileName, StringComparer.Ordinal));

        foreach (var command in expected)
        {
            var sourcePath = Path.Combine(commandsDirectory, command.FileName);
            File.Exists(sourcePath).Should().BeTrue();

            var root = CSharpSyntaxTree.ParseText(File.ReadAllText(sourcePath)).GetCompilationUnitRoot();
            root.Members.OfType<BaseNamespaceDeclarationSyntax>().Single().Name.ToString().Should().Be(CommandsNamespace);

            var declaration = root.DescendantNodes().OfType<ClassDeclarationSyntax>().Single();
            declaration.Identifier.ValueText.Should().Be(command.TypeName);
            declaration.Modifiers.Select(modifier => modifier.ValueText).Should().Equal("public", "sealed");
            declaration.BaseList!.Types.Select(baseType => baseType.Type.ToString()).Should().Equal("IActionCommand");
            declaration.AttributeLists.Should().BeEmpty();
            declaration.Members.OfType<ConstructorDeclarationSyntax>().Should().BeEmpty();
            var properties = declaration.Members.OfType<PropertyDeclarationSyntax>().ToArray();
            properties.Select(property => property.Identifier.ValueText)
                .Should().Equal(command.Properties.Select(property => property.Name));
            properties.Select(property => property.Type.ToString())
                .Should().Equal(command.Properties.Select(property => property.Type));
            properties.Select(property => property.AccessorList!.Accessors.Select(accessor => accessor.Keyword.ValueText))
                .Should().AllSatisfy(accessors => accessors.Should().Equal("get", "init"));
        }
    }

    private static readonly ExpectedCommand[] ExpectedCommands =
    [
        new("InvitationCreatedCommand.cs", "InvitationCreatedCommand", [new("InvitationId", "Id<TrainerInvitation>")]),
        new("InvitationAcceptedCommand.cs", "InvitationAcceptedCommand", [new("InvitationId", "Id<TrainerInvitation>")]),
        new("InvitationRevokedCommand.cs", "InvitationRevokedCommand", [new("InvitationId", "Id<TrainerInvitation>")]),
        new("TrainerInvitationCreatedInAppNotificationCommand.cs", "TrainerInvitationCreatedInAppNotificationCommand", [new("InvitationId", "Id<TrainerInvitation>"), new("TraineeId", "Id<User>"), new("TrainerId", "Id<User>")]),
        new("TrainerInvitationAcceptedInAppNotificationCommand.cs", "TrainerInvitationAcceptedInAppNotificationCommand", [new("InvitationId", "Id<TrainerInvitation>"), new("TrainerId", "Id<User>"), new("TraineeId", "Id<User>")]),
        new("TrainerInvitationRejectedInAppNotificationCommand.cs", "TrainerInvitationRejectedInAppNotificationCommand", [new("InvitationId", "Id<TrainerInvitation>"), new("TrainerId", "Id<User>"), new("TraineeId", "Id<User>")]),
        new("TrainerRelationshipEndedInAppNotificationCommand.cs", "TrainerRelationshipEndedInAppNotificationCommand", [new("TrainerId", "Id<User>"), new("TraineeId", "Id<User>")]),
        new("TraineeNoteUpdatedInAppNotificationCommand.cs", "TraineeNoteUpdatedInAppNotificationCommand", [new("TraineeNoteId", "Id<TraineeNote>"), new("TraineeId", "Id<User>"), new("TrainerId", "Id<User>"), new("NoteTitle", "string?"), new("TriggeredAt", "DateTimeOffset")])
    ];

    private sealed record ExpectedCommand(string FileName, string TypeName, ExpectedProperty[] Properties);

    private sealed record ExpectedProperty(string Name, string Type);
}
