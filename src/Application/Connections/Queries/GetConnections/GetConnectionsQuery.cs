using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.Banking;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Connections.Queries.GetConnections;

/// <summary>
/// Starea integrărilor contului curent (spec §3.4).
/// </summary>
/// <remarks>
/// E o **proiecție** peste structurile existente, nu o tabelă nouă. Spec-ul §7.1 cere explicit
/// reutilizarea lui <c>BankConnection</c>, iar o tabelă generică de integrări care ar fi dublat-o
/// ar fi produs două surse de adevăr pentru aceeași conexiune bancară.
/// </remarks>
public sealed record GetConnectionsQuery : IQuery<List<IntegrationDto>>;

/// <param name="Available">
/// Dacă integrarea poate fi conectată azi. Oblio e legat de dosarul PFA, iar eldrive nu are încă
/// integrare deloc: preferăm să spunem asta pe card decât să oferim un buton care n-ar face nimic.
/// </param>
public sealed record IntegrationDto(
    string Provider,
    string Status,
    DateTime? ConnectedAtUtc,
    DateTime? ExpiresAtUtc,
    DateTime? LastSyncAtUtc,
    string? ErrorMessage,
    bool Available,
    List<IntegrationDetailDto> Details);

public sealed record IntegrationDetailDto(string Label, string Value);

internal sealed class GetConnectionsQueryHandler(
    IApplicationDbContext context,
    IUserContext userContext)
    : IQueryHandler<GetConnectionsQuery, List<IntegrationDto>>
{
    /// <summary>Cu câte zile înainte de expirare cardul trece pe „expiră curând".</summary>
    private const int ExpiringWithinDays = 14;

    public async Task<Result<List<IntegrationDto>>> Handle(
        GetConnectionsQuery query,
        CancellationToken cancellationToken)
    {
        BankConnection? bank = await context.BankConnections
            .AsNoTracking()
            .Where(c => c.UserId == userContext.UserId)
            .OrderByDescending(c => c.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        return Result.Success(new List<IntegrationDto>
        {
            // Oblio există în platformă doar legat de dosarul PFA (`PfaOblioAccount`), deci un SRL
            // nu are încă unde să se conecteze. Cardul o spune, în loc să pară doar neconectat.
            new("Oblio", "disconnected", null, null, null, null, Available: false, []),
            MapBank(bank),
            new("Eldrive", "disconnected", null, null, null, null, Available: false, []),
        });
    }

    private static IntegrationDto MapBank(BankConnection? connection)
    {
        if (connection is null)
        {
            return new IntegrationDto("Bank", "disconnected", null, null, null, null, Available: true, []);
        }

        var details = new List<IntegrationDetailDto>();
        if (!string.IsNullOrWhiteSpace(connection.InstitutionName))
        {
            details.Add(new IntegrationDetailDto("Bancă", connection.InstitutionName));
        }

        return new IntegrationDto(
            "Bank",
            MapStatus(connection),
            connection.LinkedAtUtc,
            connection.ConsentExpiresAtUtc,
            connection.LastSyncedAtUtc,
            connection.ErrorMessage,
            Available: true,
            details);
    }

    /// <summary>
    /// Cele patru stări din §3.4. Pragul „expiră curând" se decide **aici**, nu în interfață: cu
    /// un prag în frontend și altul pe server, cardul și notificarea de expirare s-ar contrazice.
    /// </summary>
    private static string MapStatus(BankConnection connection)
    {
        if (connection.Status is BankConnectionStatus.Error)
        {
            return "error";
        }

        if (connection.Status is BankConnectionStatus.Expired or BankConnectionStatus.Revoked)
        {
            return "disconnected";
        }

        if (connection.Status is not BankConnectionStatus.Linked)
        {
            return "disconnected";
        }

        bool expiringSoon = connection.ConsentExpiresAtUtc.HasValue
            && connection.ConsentExpiresAtUtc.Value <= DateTime.UtcNow.AddDays(ExpiringWithinDays);

        return expiringSoon ? "expiring" : "connected";
    }
}
