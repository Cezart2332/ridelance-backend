using SharedKernel;

namespace Domain.Rentals;

/// <summary>
/// Cui i se închiriază. Aparține flotei, nu platformei — un chiriaș nu primește cont RIDElance.
/// </summary>
/// <remarks>
/// Entitate proprie, nu patru coloane pe închiriere. Al doilea contract cu același om însemna până
/// acum retastarea numelui, a CNP-ului și a telefonului, iar o greșeală de tastare făcea două
/// persoane din una — pe documente, pe care nu le mai poți corecta după semnare.
/// </remarks>
public sealed class Tenant : Entity
{
    public Guid Id { get; set; }

    /// <summary>Flota căreia îi aparține. Chiriașii nu se văd între firme.</summary>
    public Guid OwnerUserId { get; set; }

    public TenantType Type { get; set; } = TenantType.Individual;

    /// <summary>Numele de pe contract: al persoanei, sau denumirea firmei.</summary>
    public string Name { get; set; } = string.Empty;

    // Persoană fizică
    public string? Cnp { get; set; }
    public string? IdSeries { get; set; }
    public string? IdNumber { get; set; }

    // PFA sau SRL
    public string? Cui { get; set; }
    public string? RegCom { get; set; }

    public string? Address { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? DriverLicenseNumber { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
