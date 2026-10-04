using System.Globalization;
using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Accounting.Ledger;
using Application.Accounting.Tax;
using Domain.Accounting;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Accounting.Declarations;

/// <summary>Starea unei declarații pentru PFA, fără termeni tehnici (spec declarații §7).</summary>
[System.Text.Json.Serialization.JsonConverter(typeof(UpperSnakeCaseEnumConverter<ClientDeclarationState>))]
public enum ClientDeclarationState
{
    /// <summary>Generată, în validare sau la semnare.</summary>
    InPreparation = 0,

    /// <summary>Depusă la ANAF; recipisa nu a venit încă.</summary>
    Submitted = 1,

    /// <summary>Recipisă validă de la ANAF.</summary>
    ConfirmedByAnaf = 2,

    /// <summary>Validare picată sau recipisă cu erori: contabilul o reface.</summary>
    WithAccountant = 3,
}

/// <param name="PdfDocumentId">PDF-ul declarației, când există.</param>
/// <param name="ReceiptDocumentId">Recipisa ANAF, doar cu recipisă asociată.</param>
public sealed record ClientDeclarationDto(
    Guid DeclarationId,
    DeclarationType Type,
    string Period,
    decimal Amount,
    DateOnly? DueDate,
    ClientDeclarationState State,
    Guid? PdfDocumentId,
    Guid? ReceiptDocumentId);

/// <summary>
/// <c>GET /pfa/declarations?year=</c> — taxele lunare de plată ale PFA-ului, din declarațiile generate
/// (QA 11): suma, termenul din regula fiscală și starea. O singură sursă: <see cref="DeclarationVersion"/>.
/// </summary>
public sealed record GetClientDeclarationsQuery(int Year) : IQuery<IReadOnlyList<ClientDeclarationDto>>;

internal sealed class GetClientDeclarationsQueryHandler(IApplicationDbContext db, IUserContext userContext)
    : IQueryHandler<GetClientDeclarationsQuery, IReadOnlyList<ClientDeclarationDto>>
{
    public async Task<Result<IReadOnlyList<ClientDeclarationDto>>> Handle(GetClientDeclarationsQuery query, CancellationToken cancellationToken)
    {
        if (await ClientLedger.PfaIdAsync(db, userContext.UserId, cancellationToken) is not { } pfaId)
        {
            return Result.Failure<IReadOnlyList<ClientDeclarationDto>>(ClientLedger.NoPfa);
        }

        string prefix = query.Year.ToString(CultureInfo.InvariantCulture);
        var versions = await db.DeclarationVersions.AsNoTracking()
            .Where(v => v.Declaration.PfaRegistrationId == pfaId && v.Declaration.Period.StartsWith(prefix) &&
                        v.VersionNo == v.Declaration.Versions.Max(other => other.VersionNo) &&
                        v.Status != DeclarationStatus.Draft)
            .Select(v => new { v.DeclarationId, v.Declaration.Type, v.Declaration.Period, v.Amount, v.Status, v.PdfDocumentId, v.ReceiptDocumentId })
            .ToListAsync(cancellationToken);
        TaxRuleSet rules = await TaxRuleSet.LoadAsync(db, cancellationToken);

        return versions
            .OrderByDescending(v => v.Period, StringComparer.Ordinal)
            .ThenBy(v => v.Type)
            .Select(v => new ClientDeclarationDto(
                v.DeclarationId,
                v.Type,
                v.Period,
                v.Amount,
                DueDate(rules, v.Type, v.Period),
                StateOf(v.Status, v.ReceiptDocumentId),
                v.PdfDocumentId,
                v.ReceiptDocumentId))
            .ToList();
    }

    /// <summary>„Confirmată ANAF” doar cu recipisa asociată (QA 5).</summary>
    internal static ClientDeclarationState StateOf(DeclarationStatus status, Guid? receipt) => status switch
    {
        DeclarationStatus.Accepted when receipt is not null => ClientDeclarationState.ConfirmedByAnaf,
        DeclarationStatus.Accepted or DeclarationStatus.Submitted or DeclarationStatus.IndexReceived => ClientDeclarationState.Submitted,
        DeclarationStatus.ValidationFailed or DeclarationStatus.Rejected => ClientDeclarationState.WithAccountant,
        _ => ClientDeclarationState.InPreparation,
    };

    private static DateOnly? DueDate(TaxRuleSet rules, DeclarationType type, string period)
    {
        DateOnly end = period.Length == 4
            ? new DateOnly(int.Parse(period, CultureInfo.InvariantCulture), 12, 31)
            : DateOnly.ParseExact(period + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture).AddMonths(1).AddDays(-1);
        return rules.Find(TaxRuleTypes.Deadline, "RO", end, new TaxRuleContext(type.ToString())) is { Formula: { } formula }
            ? DeclarationDeadline.Of(formula, period)
            : null;
    }
}
