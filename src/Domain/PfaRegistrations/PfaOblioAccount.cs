using SharedKernel;

namespace Domain.PfaRegistrations;

/// <summary>Starea integrării contului Oblio (Pasul 2.4). Avans manual din admin.</summary>
public enum OblioIntegrationStatus
{
    Pending = 0,
    Requested = 1,
    Active = 2,
}

/// <summary>
/// Pasul 2.4 — contul Oblio de facturare. Cele 6 consimțăminte din specificație, modelate
/// ca <c>PfaFleetConsent</c>. Nu se cer parole prin formular.
/// </summary>
public sealed class PfaOblioAccount : Entity
{
    public Guid Id { get; set; }
    public Guid PfaRegistrationId { get; set; }

    public string? AccountEmail { get; set; }

    // Cele 6 consimțăminte
    public bool AccountCreationConsent { get; set; }
    public bool DataProcessingConsent { get; set; }
    public bool EInvoiceConsent { get; set; }
    public bool AutoInvoicingConsent { get; set; }
    public bool RidelanceManagementConsent { get; set; }
    public bool TermsAcceptedConsent { get; set; }

    public string ConsentTextVersion { get; set; } = "2026-07";
    public DateTime? ConsentsAcceptedAtUtc { get; set; }

    public OblioIntegrationStatus IntegrationStatus { get; set; } = OblioIntegrationStatus.Pending;
    public string? AdminNote { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Toate cele 6 consimțăminte sunt bifate.</summary>
    public bool AllConsentsAccepted =>
        AccountCreationConsent && DataProcessingConsent && EInvoiceConsent &&
        AutoInvoicingConsent && RidelanceManagementConsent && TermsAcceptedConsent;

    // Navigation
    public PfaRegistration PfaRegistration { get; set; } = null!;
}
