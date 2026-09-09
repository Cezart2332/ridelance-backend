using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Abstractions.Services;
using Domain.Banking;
using Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SharedKernel;

namespace Application.Banking.Commands;

/// <param name="BankCode">Banca aleasă, cu codul din lista furnizorului.</param>
/// <param name="PsuId">Numele de utilizator la bancă, la băncile care îl cer înainte de acord.</param>
/// <param name="PsuIdType">„PF" sau „PJ", la băncile care fac diferența.</param>
/// <param name="PsuCorporateId">Codul de client firmă, unde e nevoie și de el, și de utilizator.</param>
/// <param name="TcAccepted">Acordul cu termenii furnizorului; fără el nu se deschide consimțământul.</param>
/// <param name="PsuIpAddress">
/// IP-ul de la care pornește conectarea. Îl pune endpointul, nu clientul: e o cerință a
/// furnizorului, iar fără el integrarea e limitată la patru apeluri pe zi.
/// </param>
public sealed record InitiateBankConnectionCommand(
    string BankCode,
    string? PsuId,
    string? PsuIdType,
    string? PsuCorporateId,
    string? Iban,
    bool TcAccepted,
    string? PsuIpAddress)
    : ICommand<InitiateBankConnectionResponse>;

/// <param name="ExpiresAtUtc">Adresa băncii expiră — pagina oprește așteptarea la ea.</param>
public sealed record InitiateBankConnectionResponse(string Link, DateTime? ExpiresAtUtc);

/// <summary>
/// Deschide consimțământul și întoarce adresa băncii unde trimitem utilizatorul.
///
/// Consimțământul e al nostru din prima secundă: îl creăm noi, cu identificatorul lui, înainte ca
/// utilizatorul să atingă banca. De aceea la întoarcere nu mai e nimic de ghicit — spre deosebire
/// de furnizorul anterior, unde proprietatea se deducea din ce apăruse nou de la ultimul snapshot.
/// </summary>
internal sealed class InitiateBankConnectionCommandHandler(
    IApplicationDbContext context,
    IUserContext userContext,
    IBankDataProvider provider,
    IBankConsentTokenStore tokenStore,
    IConfiguration configuration)
    : ICommandHandler<InitiateBankConnectionCommand, InitiateBankConnectionResponse>
{
    public async Task<Result<InitiateBankConnectionResponse>> Handle(
        InitiateBankConnectionCommand command,
        CancellationToken cancellationToken)
    {
        if (!provider.IsConfigured)
        {
            return Result.Failure<InitiateBankConnectionResponse>(BankErrors.NotConfigured);
        }

        if (!command.TcAccepted)
        {
            return Result.Failure<InitiateBankConnectionResponse>(BankErrors.TermsNotAccepted);
        }

        Guid userId = userContext.UserId;

        BankConnection? connection = await context.BankConnections
            .FirstOrDefaultAsync(bc => bc.UserId == userId, cancellationToken);

        if (connection?.Status == BankConnectionStatus.Linked)
        {
            return Result.Failure<InitiateBankConnectionResponse>(BankErrors.AlreadyLinked);
        }

        User? user = await context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user is null || string.IsNullOrWhiteSpace(user.Email))
        {
            return Result.Failure<InitiateBankConnectionResponse>(BankErrors.EmailRequired);
        }

        IReadOnlyList<BankInstitutionInfo> institutions;

        try
        {
            institutions = await provider.ListInstitutionsAsync(cancellationToken);
        }
        catch (BankDataProviderException ex)
        {
            return Result.Failure<InitiateBankConnectionResponse>(Error.Problem("Bank.ProviderError", ex.Message));
        }

        BankInstitutionInfo? bank = institutions.FirstOrDefault(
            i => i.Id.Equals(command.BankCode, StringComparison.OrdinalIgnoreCase));

        if (bank is null)
        {
            return Result.Failure<InitiateBankConnectionResponse>(BankErrors.UnknownInstitution);
        }

        // Ce cere banca înainte de acord ne spune tot ea, prin lista de mai sus. Verificăm aici ca
        // să dăm un mesaj în română, nu un „Bad request" al furnizorului, după ce omul a apăsat.
        if (bank.RequiresPsuId && string.IsNullOrWhiteSpace(command.PsuId))
        {
            return Result.Failure<InitiateBankConnectionResponse>(BankErrors.PsuIdRequired);
        }

        if (bank.RequiresPsuIdType && string.IsNullOrWhiteSpace(command.PsuIdType))
        {
            return Result.Failure<InitiateBankConnectionResponse>(BankErrors.PsuIdTypeRequired);
        }

        if (bank.RequiresIban && string.IsNullOrWhiteSpace(command.Iban))
        {
            return Result.Failure<InitiateBankConnectionResponse>(BankErrors.IbanRequired);
        }

        string reference = Guid.NewGuid().ToString("N");
        string baseUrl = (configuration["App:BaseUrl"] ?? string.Empty).TrimEnd('/');
        int validDays = int.TryParse(configuration["SmartAccounts:ConsentValidDays"], out int configured)
            ? configured
            : 180;

        BankConsentCreated consent;

        try
        {
            consent = await provider.CreateConsentAsync(
                new BankConsentRequest(
                    bank.Id,
                    user.Email,
                    userId.ToString(),
                    $"{baseUrl}/banca/retur?ref={reference}",
                    command.PsuIpAddress,
                    command.PsuId,
                    command.PsuIdType,
                    command.PsuCorporateId,
                    command.Iban,
                    validDays),
                cancellationToken);
        }
        catch (BankDataProviderException ex)
        {
            return Result.Failure<InitiateBankConnectionResponse>(Error.Problem("Bank.LinkFailed", ex.Message));
        }

        if (connection is null)
        {
            connection = new BankConnection
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                CreatedAtUtc = DateTime.UtcNow,
            };
            context.BankConnections.Add(connection);
        }

        connection.Provider = provider.ProviderName;
        connection.InstitutionId = bank.Id;
        connection.InstitutionName = bank.Name;
        connection.InstitutionLogoUrl = bank.Logo;
        connection.ProviderConsentId = consent.ConsentId;
        connection.ConsentStatus = consent.ConsentStatus;
        connection.Reference = reference;
        connection.Status = BankConnectionStatus.Created;
        // Autorizarea la bancă are termen. Fără unul presupus, o conectare abandonată ar lăsa
        // pagina în „se așteaptă confirmarea" pentru totdeauna.
        connection.LinkExpiresAtUtc = DateTime.UtcNow.AddMinutes(30);
        connection.MaxHistoricalDays = HistoryDaysFor(configuration, bank.Id);
        connection.ErrorMessage = null;
        connection.ConsecutiveFailures = 0;
        connection.LinkedAtUtc = null;

        // Tokenurile intră odată cu consimțământul: fără ele nu se mai poate face niciun apel pe el,
        // nici măcar întrebarea „a autorizat?".
        connection.AccessTokenEncrypted = null;
        connection.RefreshTokenEncrypted = null;

        await context.SaveChangesAsync(cancellationToken);

        await tokenStore.SaveAsync(consent.ConsentId, consent.Tokens, cancellationToken);

        return new InitiateBankConnectionResponse(consent.AuthorizationAddress, connection.LinkExpiresAtUtc);
    }

    /// <summary>
    /// Câte zile de istoric cerem la prima sincronizare.
    ///
    /// Băncile au limite proprii, iar o cerere peste limita lor e refuzată cu totul, nu trunchiată:
    /// BCR, Exim, Intesa și ING dau 90 de zile, BT 120, RZB și Vista 180, restul nelimitat.
    /// </summary>
    private static int HistoryDaysFor(IConfiguration configuration, string bankCode)
    {
        int configured = int.TryParse(configuration["SmartAccounts:InitialHistoryDays"], out int days)
            ? days
            : 365;

        int cap = bankCode.ToUpperInvariant() switch
        {
            "BCR" or "EXIM" or "ISP" or "ING" => 90,
            "BT" => 120,
            "RZB" or "VST" => 180,
            _ => configured,
        };

        return Math.Min(configured, cap);
    }
}
