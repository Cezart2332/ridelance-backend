using SharedKernel;

namespace Application.Abstractions.Anaf;

/// <summary>Tokenurile emise de <c>logincert.anaf.ro</c> (JWT: acces 90 de zile, refresh 365).</summary>
public sealed record AnafTokens(string AccessToken, string RefreshToken, DateTime AccessExpiresAtUtc, DateTime RefreshExpiresAtUtc);

/// <summary>Un mesaj din lista e-Factura (<c>listaMesajePaginatieFactura</c>).</summary>
/// <param name="Type">Ca la ANAF: <c>FACTURA PRIMITA</c>, <c>FACTURA TRIMISA</c>, <c>ERORI FACTURA</c>…</param>
public sealed record EFacturaListItem(string Id, string Type, DateTime CreatedAtUtc, string? UploadId, string? Cif, string? Details);

/// <summary>O pagină din listă. <see cref="Error"/> e mesajul ANAF când cererea n-a întors mesaje din alt motiv decât „nu există”.</summary>
public sealed record EFacturaPage(IReadOnlyList<EFacturaListItem> Messages, int TotalPages, string? Error);

/// <summary>
/// ANAF prin OAuth (<c>logincert.anaf.ro</c>) și serviciile e-Factura (<c>api.anaf.ro/prod/FCTEL</c>).
/// Autentificarea cu certificatul calificat se face o singură dată, în browserul adminului; de
/// acolo încolo serverul lucrează cu tokenurile.
/// </summary>
public interface IAnafEFacturaClient
{
    /// <summary>Există ClientId, ClientSecret și adresa de callback.</summary>
    bool IsConfigured { get; }

    /// <summary>Adresa <c>logincert.anaf.ro/…/authorize</c> la care se trimite adminul.</summary>
    Uri AuthorizeUrl(string state);

    Task<Result<AnafTokens>> ExchangeCodeAsync(string code, CancellationToken cancellationToken);

    Task<Result<AnafTokens>> RefreshAsync(string refreshToken, CancellationToken cancellationToken);

    /// <param name="cif">CUI-ul PFA-ului, fără „RO”.</param>
    Task<Result<EFacturaPage>> ListMessagesAsync(string accessToken, string cif, DateTime fromUtc, DateTime toUtc, int page, CancellationToken cancellationToken);

    /// <summary>Arhiva ZIP a unui mesaj: factura XML și semnătura Ministerului Finanțelor.</summary>
    Task<Result<byte[]>> DownloadAsync(string accessToken, string messageId, CancellationToken cancellationToken);

    /// <summary>PDF-ul facturii, generat de ANAF din XML (serviciul public de transformare).</summary>
    Task<Result<byte[]>> ToPdfAsync(byte[] invoiceXml, bool creditNote, CancellationToken cancellationToken);
}
