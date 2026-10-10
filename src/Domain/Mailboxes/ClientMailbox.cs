using Domain.PfaRegistrations;
using SharedKernel;

namespace Domain.Mailboxes;

public enum ClientMailboxStatus
{
    NotCreated = 0,

    /// <summary>În coadă sau în lucru: jobul de creare îl ia de aici.</summary>
    Creating = 1,
    Active = 2,
    Failed = 3,

    /// <summary>Predat clientului la încheierea colaborării. Mailbox-ul rămâne, identitatea RIDElance nu.</summary>
    Transferred = 4,
}

public enum ClientMailboxAuditAction
{
    Created = 0,
    IdentityCreated = 1,
    CredentialsRevealed = 2,
    Retried = 3,
    Transferred = 4,
    Failed = 5,
}

/// <summary>
/// Adresa operațională a clientului (<c>prenume.nume@pfa.ridelance.ro</c>), folosită de agent pentru
/// ARR și conturile de flotă Uber/Bolt.
///
/// RIDElance intră în mailbox printr-o identitate separată, cu parola ei; parola mailbox-ului e a
/// clientului și îi este predată la plecare. Mailbox-ul nu se șterge niciodată din aplicație.
///
/// Partea locală și parola se salvează ÎNAINTE de apelul la furnizor: o reluare după un eșec parțial
/// regăsește aceeași adresă, în loc să creeze un al doilea mailbox.
/// </summary>
public sealed class ClientMailbox : Entity
{
    public Guid Id { get; set; }

    /// <summary>Clientul. Un singur mailbox per client.</summary>
    public Guid UserId { get; set; }
    public Guid PfaRegistrationId { get; set; }

    public string? Address { get; set; }
    public string? LocalPart { get; set; }

    /// <summary>Parola clientului, criptată. Ștearsă după predare.</summary>
    public string? MailboxPasswordEncrypted { get; set; }

    /// <summary>Mailbox-ul există la furnizor. Până atunci <see cref="LocalPart"/> e doar rezervată.</summary>
    public DateTime? MailboxCreatedAtUtc { get; set; }

    public string? OpsIdentityAddress { get; set; }
    public string? OpsIdentityLocalPart { get; set; }

    /// <summary>Parola identității RIDElance, criptată. Ștearsă după predare.</summary>
    public string? OpsIdentityPasswordEncrypted { get; set; }
    public DateTime? OpsIdentityCreatedAtUtc { get; set; }

    public ClientMailboxStatus Status { get; set; } = ClientMailboxStatus.NotCreated;
    public string? LastError { get; set; }

    /// <summary>Documentul „Predare email”, generat la offboarding; intră în pachetul de predare PFA.</summary>
    public Guid? HandoverDocumentId { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ActivatedAtUtc { get; set; }
    public DateTime? TransferredAtUtc { get; set; }

    public PfaRegistration PfaRegistration { get; set; } = null!;
    public List<ClientMailboxAuditLog> AuditLogs { get; set; } = [];
}

/// <summary>Cine a făcut ce pe un mailbox. Fiecare afișare a credențialelor lasă un rând aici.</summary>
public sealed class ClientMailboxAuditLog : Entity
{
    public Guid Id { get; set; }
    public Guid ClientMailboxId { get; set; }
    public ClientMailboxAuditAction Action { get; set; }

    /// <summary><c>null</c> = sistemul (jobul de creare).</summary>
    public Guid? PerformedByUserId { get; set; }
    public DateTime PerformedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Niciodată parole.</summary>
    public string? Details { get; set; }

    public ClientMailbox Mailbox { get; set; } = null!;
}
