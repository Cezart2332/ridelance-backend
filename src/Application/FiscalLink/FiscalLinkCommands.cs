using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Abstractions.Services;
using Domain.FiscalLink;
using Domain.Payments;
using Domain.PfaRegistrations;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.FiscalLink;

/// <summary>
/// Ce vede PFA-ul pe pagina FiscalLink. Codul și casele vin live de la FiscalLink; dacă FiscalLink
/// nu răspunde, conexiunea rămâne arătată, cu <paramref name="Error"/> în loc de date.
/// </summary>
public sealed record FiscalLinkConnectionDto(
    bool Connected,
    string? ActivationCode,
    string? ActivationLink,
    IReadOnlyList<FiscalLinkRegister> Registers,
    string? Error);

public sealed record GetFiscalLinkConnectionQuery : IQuery<FiscalLinkConnectionDto>;

/// <summary>PFA-ul devine client FiscalLink și primește codul de activare pentru casele lui.</summary>
public sealed record ConnectFiscalLinkCommand : ICommand<FiscalLinkConnectionDto>;

internal static class FiscalLinkErrors
{
    public static readonly Error NoPfa = Error.NotFound("FiscalLink.NoPfa", "Contul nu are un PFA înregistrat.");

    public static readonly Error NoSubscription = Error.Problem(
        "FiscalLink.NoSubscription", "Casa de marcat prin FiscalLink e inclusă în abonamentul RIDElance. Activează un abonament ca să o conectezi.");

    public static readonly FiscalLinkConnectionDto Disconnected = new(false, null, null, [], null);

    /// <summary>Starea curentă, citită de la FiscalLink pentru clientul salvat.</summary>
    public static async Task<FiscalLinkConnectionDto> ReadAsync(IFiscalLinkService fiscalLink, Guid clientId, CancellationToken cancellationToken)
    {
        Result<FiscalLinkActivation> activation = await fiscalLink.GetActivationAsync(clientId, cancellationToken);
        if (activation.IsFailure)
        {
            return new FiscalLinkConnectionDto(true, null, null, [], activation.Error.Description);
        }

        Result<IReadOnlyList<FiscalLinkRegister>> registers = await fiscalLink.ListRegistersAsync(clientId, cancellationToken);
        return new FiscalLinkConnectionDto(
            true,
            activation.Value.ActivationCode,
            activation.Value.ActivationLink,
            registers.IsSuccess ? registers.Value : [],
            registers.IsFailure ? registers.Error.Description : null);
    }
}

internal sealed class GetFiscalLinkConnectionQueryHandler(
    IApplicationDbContext context,
    IUserContext userContext,
    IFiscalLinkService fiscalLink)
    : IQueryHandler<GetFiscalLinkConnectionQuery, FiscalLinkConnectionDto>
{
    public async Task<Result<FiscalLinkConnectionDto>> Handle(GetFiscalLinkConnectionQuery query, CancellationToken cancellationToken)
    {
        FiscalLinkClient? client = await context.FiscalLinkClients.AsNoTracking()
            .Where(c => c.UserId == userContext.UserId)
            .OrderByDescending(c => c.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        return client is null
            ? FiscalLinkErrors.Disconnected
            : await FiscalLinkErrors.ReadAsync(fiscalLink, client.FiscalLinkClientId, cancellationToken);
    }
}

internal sealed class ConnectFiscalLinkCommandHandler(
    IApplicationDbContext context,
    IUserContext userContext,
    IFiscalLinkService fiscalLink)
    : ICommandHandler<ConnectFiscalLinkCommand, FiscalLinkConnectionDto>
{
    public async Task<Result<FiscalLinkConnectionDto>> Handle(ConnectFiscalLinkCommand command, CancellationToken cancellationToken)
    {
        Guid userId = userContext.UserId;

        PfaRegistration? pfa = await context.PfaRegistrations
            .Include(p => p.User)
            .Where(p => p.UserId == userId)
            .OrderByDescending(p => p.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (pfa is null)
        {
            return Result.Failure<FiscalLinkConnectionDto>(FiscalLinkErrors.NoPfa);
        }

        // Deja conectat: se întoarce starea, nu se creează un al doilea comerciant.
        FiscalLinkClient? existing = await context.FiscalLinkClients.AsNoTracking()
            .SingleOrDefaultAsync(c => c.PfaRegistrationId == pfa.Id, cancellationToken);
        if (existing is not null)
        {
            return await FiscalLinkErrors.ReadAsync(fiscalLink, existing.FiscalLinkClientId, cancellationToken);
        }

        SubscriptionStatus? subscription = await context.UserSubscriptions.AsNoTracking()
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.CreatedAtUtc)
            .Select(s => (SubscriptionStatus?)s.Status)
            .FirstOrDefaultAsync(cancellationToken);

        if (subscription is not (SubscriptionStatus.Active or SubscriptionStatus.ActivePendingBilling))
        {
            return Result.Failure<FiscalLinkConnectionDto>(FiscalLinkErrors.NoSubscription);
        }

        Result<Guid> created = await fiscalLink.CreateClientAsync(Describe(pfa), cancellationToken);
        if (created.IsFailure)
        {
            return Result.Failure<FiscalLinkConnectionDto>(created.Error);
        }

        context.FiscalLinkClients.Add(new FiscalLinkClient
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            PfaRegistrationId = pfa.Id,
            FiscalLinkClientId = created.Value,
            CreatedAtUtc = DateTime.UtcNow,
        });
        await context.SaveChangesAsync(cancellationToken);

        return await FiscalLinkErrors.ReadAsync(fiscalLink, created.Value, cancellationToken);
    }

    /// <summary>Comerciantul, cum îl știe FiscalLink: denumirea PFA-ului, CUI-ul, titularul, sediul.</summary>
    internal static FiscalLinkNewClient Describe(PfaRegistration pfa)
    {
        string holder = FirstNonEmpty(pfa.HolderName, pfa.FullName, $"{pfa.User.FirstName} {pfa.User.LastName}") ?? "PFA";
        string name = FirstNonEmpty(pfa.LegalName) ?? $"{holder} PFA";
        string? street = FirstNonEmpty(string.Join(' ', new[] { pfa.Street, pfa.Number }.Where(p => !string.IsNullOrWhiteSpace(p))));
        string? address = FirstNonEmpty(string.Join(", ", new[] { street, pfa.City, pfa.County }.Where(p => !string.IsNullOrWhiteSpace(p))));

        return new FiscalLinkNewClient(
            name,
            FirstNonEmpty(pfa.Cui),
            holder,
            FirstNonEmpty(pfa.User.Email),
            FirstNonEmpty(pfa.Phone, pfa.User.PhoneNumber),
            address);
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.Select(v => v?.Trim()).FirstOrDefault(v => !string.IsNullOrEmpty(v));
}
