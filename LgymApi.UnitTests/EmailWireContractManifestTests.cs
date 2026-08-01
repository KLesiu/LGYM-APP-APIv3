using System.Reflection;
using FluentAssertions;
using LgymApi.BackgroundWorker.Common;
using LgymApi.BackgroundWorker.Common.Notifications;
using LgymApi.BackgroundWorker.Common.Notifications.Models;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class EmailWireContractManifestTests
{
    private static readonly EmailPayloadContract[] ExpectedPayloads =
    [
        Contract<WelcomeEmailPayload>(
            ("UserId", typeof(Id<User>)),
            ("UserName", typeof(string)),
            ("RecipientEmail", typeof(string)),
            ("CultureName", typeof(string))),
        Contract<InvitationEmailPayload>(
            ("InvitationId", typeof(Id<TrainerInvitation>)),
            ("InvitationCode", typeof(string)),
            ("ExpiresAt", typeof(DateTimeOffset)),
            ("TrainerName", typeof(string)),
            ("RecipientEmail", typeof(string)),
            ("CultureName", typeof(string)),
            ("PreferredTimeZone", typeof(string))),
        Contract<InvitationAcceptedEmailPayload>(
            ("InvitationId", typeof(Id<TrainerInvitation>)),
            ("TrainerName", typeof(string)),
            ("TraineeName", typeof(string)),
            ("RecipientEmail", typeof(string)),
            ("CultureName", typeof(string)),
            ("PreferredTimeZone", typeof(string))),
        Contract<InvitationRevokedEmailPayload>(
            ("InvitationId", typeof(Id<TrainerInvitation>)),
            ("TrainerName", typeof(string)),
            ("RecipientEmail", typeof(string)),
            ("CultureName", typeof(string)),
            ("PreferredTimeZone", typeof(string))),
        Contract<TrainingCompletedEmailPayload>(
            ("UserId", typeof(Id<User>)),
            ("TrainingId", typeof(Id<Training>)),
            ("RecipientEmail", typeof(string)),
            ("CultureName", typeof(string)),
            ("PreferredTimeZone", typeof(string)),
            ("PlanDayName", typeof(string)),
            ("TrainingDate", typeof(DateTimeOffset)),
            ("Exercises", typeof(IReadOnlyList<TrainingExerciseSummary>))),
        Contract<PasswordRecoveryEmailPayload>(
            ("UserId", typeof(Id<User>)),
            ("TokenId", typeof(Id<PasswordResetToken>)),
            ("UserName", typeof(string)),
            ("RecipientEmail", typeof(string)),
            ("ResetToken", typeof(string)),
            ("ResetUrl", typeof(string)),
            ("CultureName", typeof(string)))
    ];

    [Test]
    public void CommonEmailWire_ContainsExactSixPayloadTypesAndSchemas()
    {
        var action = () => Validate(ExpectedPayloads);

        action.Should().NotThrow();
        typeof(IEmailPayload).Assembly.GetName().Name.Should().Be("LgymApi.BackgroundWorker.Common");
        foreach (var contract in ExpectedPayloads)
        {
            contract.PayloadType.Assembly.Should().BeSameAs(typeof(IEmailPayload).Assembly);
            contract.PayloadType.Namespace.Should().Be("LgymApi.BackgroundWorker.Common.Notifications.Models");
            contract.PayloadType.IsPublic.Should().BeTrue();
            contract.PayloadType.IsSealed.Should().BeTrue();
            contract.PayloadType.Should().Implement<IEmailPayload>();
        }
    }

    [Test]
    public void CommonEmailWire_RejectsMissingAlteredTypeAndAlteredSchemaFixtures()
    {
        var missing = () => Validate(ExpectedPayloads[..^1]);
        var alteredType = () => Validate([
            .. ExpectedPayloads[..^1],
            ExpectedPayloads[^1] with { PayloadType = typeof(EmailMessage) }
        ]);
        var alteredSchema = () => Validate([
            ExpectedPayloads[0] with
            {
                WritableProperties =
                [
                    .. ExpectedPayloads[0].WritableProperties,
                    new PropertyContract("UnexpectedField", typeof(string))
                ]
            },
            .. ExpectedPayloads[1..]
        ]);

        missing.Should().Throw<InvalidOperationException>()
            .WithMessage("The Common email wire must contain exactly six payload types.");
        alteredType.Should().Throw<InvalidOperationException>()
            .WithMessage("The Common email payload type manifest was altered.");
        alteredSchema.Should().Throw<InvalidOperationException>()
            .WithMessage("Common email payload schema mismatch for '*'.");
    }

    [Test]
    public void CommonEmailMessage_AndIdempotencyKeyRemainStable()
    {
        GetWritableProperties(typeof(EmailMessage)).Should().BeEquivalentTo(
        [
            new PropertyContract("To", typeof(string)),
            new PropertyContract("Subject", typeof(string)),
            new PropertyContract("Body", typeof(string)),
            new PropertyContract("IsHtml", typeof(bool))
        ]);
        Id<CorrelationScope>.TryParse("11111111-1111-1111-1111-111111111111", out var correlationId)
            .Should().BeTrue();
        IdempotencyKeyPolicy.CalculateKey(correlationId.ToString())
            .Should().Be("11111111-1111-1111-1111-111111111111");
        IdempotencyKeyPolicy.AreKeysEqual(
            "11111111-1111-1111-1111-111111111111",
            "11111111-1111-1111-1111-111111111111").Should().BeTrue();
        IdempotencyKeyPolicy.AreKeysEqual(
            "11111111-1111-1111-1111-111111111111",
            "11111111-1111-1111-1111-11111111111A").Should().BeFalse();
    }

    private static void Validate(IReadOnlyList<EmailPayloadContract> contracts)
    {
        if (contracts.Count != 6)
        {
            throw new InvalidOperationException("The Common email wire must contain exactly six payload types.");
        }

        if (!contracts.Select(contract => contract.PayloadType).ToHashSet()
            .SetEquals(ExpectedPayloads.Select(contract => contract.PayloadType)))
        {
            throw new InvalidOperationException("The Common email payload type manifest was altered.");
        }

        foreach (var contract in contracts)
        {
            if (!GetWritableProperties(contract.PayloadType).ToHashSet()
                .SetEquals(contract.WritableProperties))
            {
                throw new InvalidOperationException(
                    $"Common email payload schema mismatch for '{contract.PayloadType.FullName}'.");
            }
        }
    }

    private static EmailPayloadContract Contract<TPayload>(
        params (string Name, Type Type)[] properties) =>
        new(typeof(TPayload), properties.Select(property =>
            new PropertyContract(property.Name, property.Type)).ToArray());

    private static PropertyContract[] GetWritableProperties(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(property => property.SetMethod != null)
            .Select(property => new PropertyContract(property.Name, property.PropertyType))
            .ToArray();

    private sealed record EmailPayloadContract(
        Type PayloadType,
        IReadOnlyList<PropertyContract> WritableProperties);

    private sealed record PropertyContract(string Name, Type Type);
}
