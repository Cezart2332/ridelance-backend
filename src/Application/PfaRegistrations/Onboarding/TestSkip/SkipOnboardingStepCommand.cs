using Application.Abstractions.Messaging;

namespace Application.PfaRegistrations.Onboarding.TestSkip;

/// <summary>
/// DOAR PENTRU TESTARE — avansează onboardingul cu un pas. Activ doar cu
/// Onboarding:EnableTestSkip=true. De șters împreună cu handlerul, endpointul
/// și butonul din OnboardingHubPage.
/// </summary>
public sealed record SkipOnboardingStepCommand(Guid UserId) : ICommand;
