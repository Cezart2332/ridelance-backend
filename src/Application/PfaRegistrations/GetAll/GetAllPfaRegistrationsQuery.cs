using Application.Abstractions.Messaging;

namespace Application.PfaRegistrations.GetAll;

public sealed record GetAllPfaRegistrationsQuery(int Page = 1, int PageSize = 20)
    : IQuery<PfaRegistrationListResponse>;

public sealed record PfaRegistrationListResponse(
    List<PfaRegistrationSummary> Items,
    int TotalCount);

public sealed record PfaRegistrationSummary(
    Guid Id,
    Guid UserId,
    string UserEmail,
    string UserName,
    string RegistrationType,
    string Status,
    string AccountStatus,
    string? SubscriptionStatus,
    string? SubscriptionPlan,
    string? FullName,
    string? Phone,
    int? ContractDuration,
    string? Street,
    string? Number,
    string? City,
    string? County,
    bool IsOwner,
    /// <summary>Completat de OCR din certificatul de înregistrare; adminul îl confirmă la aprobare.</summary>
    string? Cui,
    int DocumentCount,
    /// <summary>Dosarul așteaptă o acțiune de admin (dosar PFA, secțiune sau pachet de semnături).</summary>
    bool AwaitingAdminAction,
    DateTime CreatedAtUtc,
    DateTime? LastActivityAtUtc,
    /// <summary>
    /// Când s-a înrolat efectiv: toți pașii de onboarding validați. NU e același lucru cu dosarul
    /// PFA aprobat — un dosar aprobat poate avea încă patru pași de parcurs.
    /// </summary>
    DateTime? OnboardingCompletedAtUtc = null);
