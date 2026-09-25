using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Application.Abstractions;
using Application.Abstractions.Data;
using Application.Abstractions.Dossiers;
using Application.Abstractions.Services;
using Application.PfaRegistrations.Onboarding.CompanyFormation;
using Application.PfaRegistrations.Onboarding.Notifications;
using Domain.Payments;
using Domain.PfaRegistrations.CompanyFormation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Payments.ServiceOrders;

/// <summary>
/// După plată, dosarul unei comenzi de serviciu pleacă singur spre Consulto — pe același drum
/// ca dosarul din onboarding (<see cref="OnboardingOpsNotifier"/>), cu aceeași arhivă: fișa
/// solicitantului, dovada acordurilor, semnătura și actul de identitate.
///
/// Idempotent: <see cref="ServiceOrder.SentToConsultoAtUtc"/> se scrie o singură dată, deci un
/// webhook reîncercat de Stripe nu trimite a doua oară. Nu aruncă — un email căzut nu are voie
/// să pice procesarea plății.
/// </summary>
public sealed class ServiceOrderDossierSender(
    IApplicationDbContext context,
    IFileEncryptionService fileEncryptionService,
    ICompanyFormationPdfGenerator pdfGenerator,
    OnboardingOpsNotifier opsNotifier,
    ILogger<ServiceOrderDossierSender> logger)
{
    public async Task SendIfReadyAsync(Guid serviceOrderId, CancellationToken cancellationToken)
    {
        try
        {
            ServiceOrder? order = await context.ServiceOrders
                .FirstOrDefaultAsync(o => o.Id == serviceOrderId, cancellationToken);

            if (order is null || order.Status != ServiceOrderStatus.Paid || order.SentToConsultoAtUtc is not null)
            {
                return;
            }

            var dossier = ServiceOrderDossier.Parse(order.DossierJson);
            if (dossier is null)
            {
                // Comenzile de dinaintea formularului nu au dosar: echipa le preia de mână.
                return;
            }

            ConsultoOffice? office = dossier.ConsultoOfficeId is Guid officeId
                ? await context.ConsultoOffices.AsNoTracking().FirstOrDefaultAsync(o => o.Id == officeId, cancellationToken)
                : null;

            (string fileName, byte[] archive) = await BuildArchiveAsync(order, dossier, office, cancellationToken);

            string applicant = $"{dossier.Solicitant.Nume} {dossier.Solicitant.Prenume}".Trim();

            await opsNotifier.ServiceDossierReadyAsync(
                order.ServiceTitle,
                string.IsNullOrWhiteSpace(applicant) ? order.CustomerName : applicant,
                order.CustomerEmail,
                order.CustomerPhone,
                order.AmountBani ?? 0,
                new EmailAttachmentContent(fileName, "application/zip", archive),
                cancellationToken);

            order.SentToConsultoAtUtc = DateTime.UtcNow;
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Dosarul comenzii {ServiceOrderId} nu a putut pleca spre Consulto.", serviceOrderId);
        }
    }

    private async Task<(string FileName, byte[] Content)> BuildArchiveAsync(
        ServiceOrder order,
        ServiceOrderDossier dossier,
        ConsultoOffice? office,
        CancellationToken cancellationToken)
    {
        CompanyFormationRequest request = dossier.ToFormationRequest(order.Id, office);
        string applicant = $"{dossier.Solicitant.Nume} {dossier.Solicitant.Prenume}".Trim();
        string folder = ExportCompanyFormationQueryHandler.CnpHash(dossier.Solicitant);
        DateTime generatedAtUtc = DateTime.UtcNow;

        CompanyFormationSheetData sheet = ExportCompanyFormationQueryHandler.BuildSheet(request, applicant, generatedAtUtc);
        sheet = sheet with { Office = [.. sheet.Office, .. OrderFields(order, dossier)] };

        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            await ExportCompanyFormationQueryHandler.WriteAsync(
                archive, $"{folder}/date-solicitant.pdf", pdfGenerator.GenerateApplicantSheet(sheet), cancellationToken);

            if (request.Signature is not null)
            {
                await ExportCompanyFormationQueryHandler.WriteAsync(
                    archive,
                    $"{folder}/dovada-consimtamant.pdf",
                    pdfGenerator.GenerateConsentProof(ExportCompanyFormationQueryHandler.BuildProof(request, applicant, generatedAtUtc)),
                    cancellationToken);
            }

            if (dossier.Signature is not null && await ReadAsync(dossier.Signature.Image, cancellationToken) is byte[] signature)
            {
                await ExportCompanyFormationQueryHandler.WriteAsync(archive, $"{folder}/semnatura.png", signature, cancellationToken);
            }

            if (dossier.IdentityDocument is not null && await ReadAsync(dossier.IdentityDocument, cancellationToken) is byte[] identity)
            {
                string extension = Path.GetExtension(dossier.IdentityDocument.OriginalFileName);
                await ExportCompanyFormationQueryHandler.WriteAsync(
                    archive, $"{folder}/act-identitate{extension}", identity, cancellationToken);
            }

            await ExportCompanyFormationQueryHandler.WriteAsync(
                archive,
                $"{folder}/metadata.json",
                JsonSerializer.SerializeToUtf8Bytes(new
                {
                    serviceOrderId = order.Id,
                    service = order.ServiceKey,
                    serviceTitle = order.ServiceTitle,
                    applicant,
                    customerEmail = order.CustomerEmail,
                    customerPhone = order.CustomerPhone,
                    paidAtUtc = order.PaidAtUtc,
                    amountBani = order.AmountBani,
                    includesVatIntracom = dossier.IncludesVatIntracom,
                    signedAtUtc = dossier.Signature?.SignedAtUtc,
                    payloadHash = dossier.Signature?.PayloadHash,
                    consentVersion = dossier.Consents.FirstOrDefault()?.Version,
                    ownerCount = dossier.Owners.Count,
                    officeType = dossier.OfficeType?.ToString(),
                    generatedAtUtc,
                }),
                cancellationToken);
        }

        string prefix = order.ServiceKey.Replace('_', '-');
        return ($"{prefix}-{folder}.zip", buffer.ToArray());
    }

    /// <summary>Ce spune comanda în plus față de fișa din onboarding: serviciul, contactul, TVA-ul, firma.</summary>
    private static IEnumerable<CompanyFormationField> OrderFields(ServiceOrder order, ServiceOrderDossier dossier)
    {
        yield return new CompanyFormationField("Serviciu comandat", order.ServiceTitle);
        yield return new CompanyFormationField("Contact", $"{order.CustomerName} · {order.CustomerEmail} · {order.CustomerPhone}");

        if (dossier.IncludesVatIntracom)
        {
            yield return new CompanyFormationField("TVA intracomunitar", "Inclus — se cere înregistrarea în scopuri de TVA intracomunitar");
        }

        if (dossier.CompanyName is not null || dossier.CompanyCui is not null)
        {
            yield return new CompanyFormationField(
                "PFA existent",
                string.Join(" · ", new[] { dossier.CompanyName, dossier.CompanyCui is null ? null : $"CUI {dossier.CompanyCui}" }
                    .Where(v => v is not null)));
        }
    }

    private async Task<byte[]?> ReadAsync(ServiceOrderFile file, CancellationToken cancellationToken)
    {
        try
        {
            using Stream decrypted = await fileEncryptionService.DecryptAndReadAsync(
                file.EncryptedFilePath, file.EncryptionIv, cancellationToken);

            using var memory = new MemoryStream();
            await decrypted.CopyToAsync(memory, cancellationToken);
            return memory.ToArray();
        }
        catch (Exception exception) when (exception is IOException or CryptographicException)
        {
            // Un fișier lipsă nu are voie să blocheze tot pachetul.
            return null;
        }
    }
}
