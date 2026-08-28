using System.Security.Cryptography;
using SharedKernel;

namespace Domain.Rentals;

/// <summary>
/// O invitație de semnare trimisă pe email chiriașului.
/// </summary>
/// <remarks>
/// Chiriașul nu are cont RIDElance și nu trebuie să-și facă unul ca să semneze (spec §7). Linkul
/// din email **este** autentificarea, ceea ce face tokenul un secret: se păstrează hash-uit, ca o
/// parolă. O bază de date citită de cineva nu trebuie să ofere și cheile de semnare.
///
/// Se consumă o singură dată. Fără asta, un email retrimis mai departe ar fi lăsat pe oricine să
/// semneze în locul chiriașului, oricând.
/// </remarks>
public sealed class SignatureRequest : Entity
{
    public Guid Id { get; set; }

    public Guid GeneratedDocumentId { get; set; }
    public GeneratedDocument GeneratedDocument { get; set; } = null!;

    /// <summary>SHA-256 peste tokenul din link. Tokenul în clar există doar în email.</summary>
    public string TokenHash { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAtUtc { get; set; }

    /// <summary>Când a fost folosit. Non-null înseamnă consumat: nu se mai poate semna cu el.</summary>
    public DateTime? UsedAtUtc { get; set; }

    // --- Probatoriu, completat exclusiv de server. Un client nu-și poate proba singur semnătura.
    public Guid? SignatureImageDocumentId { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }

    /// <summary>
    /// SHA-256 peste imaginea semnăturii plus fișierul semnat. Dovada că semnătura aparține
    /// <em>acestui</em> document exact: dacă documentul se regenerează, hash-ul nu mai corespunde.
    /// </summary>
    public string? PayloadHash { get; set; }
}

/// <summary>
/// Tokenul din linkul de semnare: cel trimis pe email și amprenta lui păstrată la noi.
/// </summary>
/// <remarks>
/// Fără port și fără injecție: sunt două funcții pure de criptografie, care n-au ce varia. Un
/// furnizor de semnătură calificată n-ar înlocui bucata asta, ci tot fluxul.
/// </remarks>
public static class SignatureToken
{
    /// <summary>Cât timp rămâne valabil un link. O săptămână acoperă un concediu, nu o uitare.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);

    /// <summary>Tokenul în clar, pentru email. 32 de octeți: neghicibil prin forță brută.</summary>
    public static string Create() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

    public static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)));
}
