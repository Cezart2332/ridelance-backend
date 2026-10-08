namespace Application.PfaRegistrations.Onboarding.AnafMandate;

/// <summary>
/// Mandatarul din împuternicirea ANAF: omul RIDElance care semnează declarațiile clienților.
/// Stă în configurarea serverului (<c>AnafMandatar__*</c>), nu în cod: CNP-ul și actul lui de
/// identitate sunt date personale, iar mandatarul se poate schimba fără deploy.
/// </summary>
public sealed class AnafMandatarOptions
{
    public const string SectionName = "AnafMandatar";

    public string? FullName { get; set; }
    public string? Cnp { get; set; }
    public string? Domicile { get; set; }
    public string? IdSeries { get; set; }
    public string? IdNumber { get; set; }
    public string? Email { get; set; }
}
