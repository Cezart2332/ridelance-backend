using SharedKernel;

namespace Application.Abstractions.Ai;

/// <summary>Un bon / o factură de cheltuială sau un raport Z de citit (PDF sau imagine).</summary>
public sealed record ReceiptExtractionRequest(byte[] FileBytes, string ContentType, string FileName);

/// <summary>Ce s-a citit de pe un document de cheltuială (spec contabilitate B6).</summary>
public sealed record ExpenseReceiptReading(string? Merchant, string? MerchantCui, DateOnly? Date, decimal? Total, IReadOnlyList<string> Items);

/// <summary>Ce s-a citit de pe un raport Z al casei de marcat.</summary>
public sealed record ZReportReading(DateOnly? Date, string? ZNumber, decimal? Total);

/// <summary>
/// Citirea documentelor de cheltuială și a rapoartelor Z. Ca la documentele platformelor, AI-ul doar
/// citește (spec §0 pct. 5): categoria, deductibilitatea și potrivirea le decide codul.
/// </summary>
public interface IReceiptExtractor
{
    Task<Result<ExpenseReceiptReading>> ReadExpenseAsync(ReceiptExtractionRequest request, CancellationToken cancellationToken);

    Task<Result<ZReportReading>> ReadZReportAsync(ReceiptExtractionRequest request, CancellationToken cancellationToken);
}
