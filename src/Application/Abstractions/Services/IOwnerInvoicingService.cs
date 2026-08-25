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

    /// <summary>Anulează o factură. Rămâne în serie, marcată ca anulată.</summary>
    Task CancelAsync(
        OwnerOblioCredentials credentials,
        string seriesName,
        string number,
        CancellationToken cancellationToken = default);
}

/// <param name="ClientSecret">Secretul în clar. Apelantul îl decriptează, serviciul nu-l stochează.</param>
public sealed record OwnerOblioCredentials(string ClientId, string ClientSecret, string Cif);

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
