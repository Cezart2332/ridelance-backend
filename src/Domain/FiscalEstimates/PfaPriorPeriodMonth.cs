namespace Domain.FiscalEstimates;

/// <summary>
/// Venitul și cheltuielile unei luni de dinainte ca PFA-ul să intre în RIDElance, trecute de
/// contabil (din registrul de încasări și plăți sau din extrase). Fără ele, taxele anului se
/// estimează din media lunilor pe care le avem; cu ele, anul e acoperit.
/// </summary>
/// <remarks>
/// E totalul lunii: înlocuiește, pentru luna respectivă, ce avem din platforme și din
/// cheltuielile încărcate — nu se adună peste ele, ca aceeași cursă să nu intre de două ori.
/// O lună trecută cu 0 lei e o lună fără activitate, nu una lipsă.
/// </remarks>
public sealed class PfaPriorPeriodMonth
{
    public Guid Id { get; set; }
    public Guid PfaRegistrationId { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }

    /// <summary>Venitul brut încasat în lună, lei.</summary>
    public decimal Income { get; set; }

    /// <summary>Cheltuielile deductibile ale lunii, lei.</summary>
    public decimal Expenses { get; set; }

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public Guid UpdatedByUserId { get; set; }
}
