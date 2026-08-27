using System.Diagnostics.CodeAnalysis;
using SharedKernel;

namespace Domain.Rentals;

/// <summary>Starea unui document generat, de la creare până la semnare.</summary>
[SuppressMessage("Naming", "CA1720:Identifier contains type name",
    Justification = "Signed e termenul din domeniu pentru un document semnat; analizorul il confunda cu tipurile intregi cu semn.")]
public enum GeneratedDocumentStatus
{
    Generated,
    SentForSignature,
    Signed,
    Cancelled,
}

/// <summary>
/// Un document produs pentru o închiriere: contract sau proces-verbal.
/// </summary>
/// <remarks>
/// Versionat, nu suprascris. Dacă se corectează o dată în contract și se regenerează, versiunea
/// veche rămâne — s-ar putea să fi fost deja trimisă cuiva, iar „ce a semnat clientul" trebuie să
/// rămână un lucru pe care îl putem arăta.
/// </remarks>
public sealed class GeneratedDocument : Entity
{
    public Guid Id { get; set; }

    public Guid RentalId { get; set; }
    public Rental Rental { get; set; } = null!;

    /// <summary>`RentalContract`, `HandoverProtocol` sau `ReturnProtocol`.</summary>
    public string Type { get; set; } = string.Empty;

    public GeneratedDocumentStatus Status { get; set; } = GeneratedDocumentStatus.Generated;

    /// <summary>A câta generare a documentului ăstuia. Pornește de la 1.</summary>
    public int Version { get; set; } = 1;

    /// <summary>Documentul din stocare. Fișierele trec prin același `Document`, criptat.</summary>
    public Guid DocumentId { get; set; }

    /// <summary>Varianta semnată, când există. Separată: originalul rămâne consultabil.</summary>
    public Guid? SignedDocumentId { get; set; }

    public DateTime GeneratedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? SentAtUtc { get; set; }
    public string? SentToEmail { get; set; }
    public DateTime? SignedAtUtc { get; set; }

    /// <summary>Referința la cererea de semnare. Se completează în faza de semnare.</summary>
    public string? ExternalSignatureRef { get; set; }
}
