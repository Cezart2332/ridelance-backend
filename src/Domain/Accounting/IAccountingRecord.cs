namespace Domain.Accounting;

/// <summary>
/// O înregistrare fiscală sau contabilă. Nu se șterge fizic niciodată (spec contabilitate §0
/// pct. 7): corecțiile se fac prin versiuni noi, închideri de valabilitate sau corecții
/// controlate, cu audit. <c>ApplicationDbContext</c> refuză salvarea unei ștergeri.
/// </summary>
#pragma warning disable CA1040 // Interfață-marker, citită prin reflecție la salvare.
public interface IAccountingRecord;
#pragma warning restore CA1040
