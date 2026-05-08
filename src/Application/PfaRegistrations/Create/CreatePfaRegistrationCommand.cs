using Application.Abstractions.Messaging;
using Domain.PfaRegistrations;

namespace Application.PfaRegistrations.Create;

public sealed record CreatePfaRegistrationCommand(
    Guid UserId,
    RegistrationType RegistrationType,
    string? FullName,
    string? Phone,
    int? ContractDuration,
    string? Street,
    string? Number,
    string? City,
    string? County,
    bool IsOwner) : ICommand<Guid>;
