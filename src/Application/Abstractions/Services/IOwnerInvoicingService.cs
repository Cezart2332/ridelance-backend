namespace Application.Abstractions.Services;

/// <summary>
/// Facturarea în numele unui proprietar, pe contul lui Oblio.
/// </summary>
/// <remarks>
/// Distinct de <see cref="IOblioService"/>, care emite facturile platformei către clienții ei,
/// pe contul RIDElance. Aici credențialele vin ca parametru, fiindcă fiecare PFA sau SRL are
/// contul lui.
///
/// Doar facturi **emise**: API-ul Oblio nu expune deloc facturile primite — ele există doar în
/// interfața lor, aduse din SPV. Pentru primite, calea e ANAF direct.
/// </remarks>
public interface IOwnerInvoicingService
{
    /// <summary>Verifică credențialele și citește denumirea firmei și seriile disponibile.</summary>
    Task<OblioConnectionInfo> TestConnectionAsync(
        OwnerOblioCredentials credentials,
        CancellationToken cancellationToken = default);

    /// <summary>Facturile emise în intervalul cerut, așa cum le știe Oblio.</summary>
    Task<IReadOnlyList<OwnerInvoice>> ListInvoicesAsync(
        OwnerOblioCredentials credentials,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default);

    /// <summary>Marchează o factură ca încasată.</summary>
    Task CollectAsync(
        OwnerOblioCredentials credentials,
        string seriesName,
        string number,
        decimal amountLei,
        string paymentMethod,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Emite o factură pe contul proprietarului.
    /// </summary>
    /// <remarks>
    /// Oblio o creează, o trimite pe email clientului dacă are adresă, iar dacă proprietarul cere,
    /// o depune și în SPV. Rezultatul e factura așa cum a numerotat-o Oblio — seria și numărul nu
    /// se aleg de aici, sunt ale seriei lui.
    /// </remarks>
    Task<OwnerInvoice> CreateInvoiceAsync(
        OwnerOblioCredentials credentials,
        NewOwnerInvoice invoice,
        CancellationToken cancellationToken = default);

    /// <summary>Anulează o factură. Rămâne în serie, marcată ca anulată.</summary>
    Task CancelAsync(
        OwnerOblioCredentials credentials,
        string seriesName,
        string number,
        CancellationToken cancellationToken = default);
}

/// <param name="ClientSecret">Secretul în clar. Apelantul îl decriptează, serviciul nu-l stochează.</param>
public sealed record OwnerOblioCredentials(string ClientId, string ClientSecret, string Cif);

/// <summary>O linie de pe factura de emis.</summary>
/// <param name="VatPercent">
/// Cota în procente (19, 11, 0). Merge împreună cu <paramref name="VatName" />, pe care Oblio îl
/// cere ca etichetă — o cotă fără nume e refuzată de API.
/// </param>
public sealed record NewInvoiceLine(
    string Name,
    decimal Quantity,
    decimal PriceLei,
    string MeasuringUnit,
    decimal VatPercent,
    string VatName,
    bool VatIncluded);

/// <summary>Datele din care se emite o factură nouă.</summary>
/// <param name="SeriesName">Seria proprietarului. Una dintre cele întoarse la conectare.</param>
/// <param name="SendToSpv">Depunerea în SPV o cere proprietarul, factură cu factură.</param>
public sealed record NewOwnerInvoice(
    string SeriesName,
    string ClientName,
    string? ClientCif,
    string? ClientEmail,
    string? ClientAddress,
    string? ClientCity,
    string? ClientState,
    DateOnly IssueDate,
    DateOnly? DueDate,
    IReadOnlyList<NewInvoiceLine> Lines,
    string? Note,
    bool SendToSpv);

/// <summary>Datele publice ale unei firme, citite după CUI.</summary>
public sealed record CompanyLookupResult(
    string Cui,
    string Name,
    string? Address,
    string? City,
    string? County,
    string? RegistrationNumber,
    bool VatPayer);

/// <summary>O factură emisă, citită din Oblio.</summary>
#pragma warning disable CA1054 // `Link` e URL-ul public al documentului, ca șir — îl afișăm, nu-l construim
public sealed record OwnerInvoice(
    string SeriesName,
    string Number,
    DateOnly IssueDate,
    DateOnly? DueDate,
    string ClientName,
    string? ClientCif,
    decimal TotalLei,
    decimal CollectedLei,
    string? Link,
    bool Canceled);
#pragma warning restore CA1054
