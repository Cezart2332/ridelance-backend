using SharedKernel;

namespace Domain.PfaRegistrations.CompanyFormation;

/// <summary>
/// O adresă de sediu social pusă la dispoziție de Consulto. Lista se administrează din
/// admin; șoferul alege una singură, fără să completeze nimic altceva.
/// </summary>
public sealed class ConsultoOffice : Entity
{
    public Guid Id { get; set; }

    public Adresa Adresa { get; set; } = new();

    /// <summary>Costul găzduirii, în bani. 0 = inclus.</summary>
    public int MonthlyFeeBani { get; set; }
    public int YearlyFeeBani { get; set; }

    /// <summary>Adresele dezactivate nu mai apar în listă, dar rămân legate de dosarele vechi.</summary>
    public bool IsActive { get; set; } = true;

    public int Position { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Adresa pe un rând, pentru listă și pentru actele generate.</summary>
    public string ToDisplayString()
    {
        string[] parts =
        [
            string.IsNullOrWhiteSpace(Adresa.Strada) ? string.Empty : $"Str. {Adresa.Strada}",
            string.IsNullOrWhiteSpace(Adresa.Numar) ? string.Empty : $"nr. {Adresa.Numar}",
            string.IsNullOrWhiteSpace(Adresa.Bloc) ? string.Empty : $"bl. {Adresa.Bloc}",
            string.IsNullOrWhiteSpace(Adresa.Scara) ? string.Empty : $"sc. {Adresa.Scara}",
            string.IsNullOrWhiteSpace(Adresa.Etaj) ? string.Empty : $"et. {Adresa.Etaj}",
            string.IsNullOrWhiteSpace(Adresa.Apartament) ? string.Empty : $"ap. {Adresa.Apartament}",
            Adresa.Localitate ?? string.Empty,
            string.IsNullOrWhiteSpace(Adresa.Judet) ? string.Empty : $"jud. {Adresa.Judet}",
        ];

        return string.Join(", ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
    }
}
