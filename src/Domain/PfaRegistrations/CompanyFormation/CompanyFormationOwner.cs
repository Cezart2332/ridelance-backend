using SharedKernel;

namespace Domain.PfaRegistrations.CompanyFormation;

/// <summary>
/// Proprietarul imobilului declarat ca sediu social, când nu e solicitantul însuși.
/// Pot fi mai mulți (coproprietate), toți cu aceleași date ca solicitantul.
/// </summary>
public sealed class CompanyFormationOwner : Entity
{
    public Guid Id { get; set; }
    public Guid CompanyFormationRequestId { get; set; }

    /// <summary>Ordinea de afișare, ca lista să nu se rearanjeze între încărcări.</summary>
    public int Position { get; set; }

    public PersoanaFizica Persoana { get; set; } = new();

    /// <summary>Unde trimitem acordul de semnat. Proprietarul n-are cont RIDElance.</summary>
    public string? Email { get; set; }

    // --- Acordul proprietarului, semnat din linkul primit pe email ---

    /// <summary>SHA-256 peste tokenul din link; tokenul în clar există doar în email.</summary>
    public string? ConsentTokenHash { get; set; }
    public DateTime? ConsentSentAtUtc { get; set; }
    public DateTime? ConsentExpiresAtUtc { get; set; }
    public DateTime? ConsentSignedAtUtc { get; set; }

    /// <summary>Imaginea semnăturii, stocată criptat ca orice document din dosar.</summary>
    public Guid? ConsentSignatureDocumentId { get; set; }

    /// <summary>Textul exact pe care l-a semnat proprietarul.</summary>
    public string? ConsentTextSnapshot { get; set; }

    // Probatoriu, completat exclusiv de server.
    public string? ConsentIpAddress { get; set; }
    public string? ConsentUserAgent { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    // Navigation
    public CompanyFormationRequest CompanyFormationRequest { get; set; } = null!;

    /// <summary>O adresă la care se poate trimite acordul: ceva@domeniu.tld, fără spații.</summary>
    public static bool IsValidEmail(string? email) =>
        !string.IsNullOrWhiteSpace(email)
        && email.Length <= 256
        && System.Text.RegularExpressions.Regex.IsMatch(email.Trim(), @"^[^@\s]+@[^@\s]+\.[^@\s]+$");
}
