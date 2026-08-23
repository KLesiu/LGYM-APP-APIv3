namespace LgymApi.Identity.Contracts.AdultConfirmation;

public sealed class AgeGateOptions
{
    public const string SectionName = "AgeGate";

    public bool Enabled { get; set; }
    public string ConfirmationVersion { get; set; } = "18plus-v1";
}
