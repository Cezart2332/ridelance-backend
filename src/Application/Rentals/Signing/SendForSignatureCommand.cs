using Application.Abstractions;
using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Notifications;
using Domain.Rentals;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SharedKernel;

namespace Application.Rentals.Signing;

/// <summary>Trimite documentul spre semnare pe email. Retrimiterea creează un token nou.</summary>
public sealed record SendForSignatureCommand(Guid GeneratedDocumentId, string Email) : ICommand;

internal sealed class SendForSignatureCommandHandler(
    IApplicationDbContext context,
    IUserContext userContext,
    IEmailService emailService,
    IMjmlRenderer mjml,
    IConfiguration configuration)
    : ICommandHandler<SendForSignatureCommand>
{
    public async Task<Result> Handle(SendForSignatureCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.Email) || !command.Email.Contains('@', StringComparison.Ordinal))
        {
            return Result.Failure(Error.Problem("Signature.InvalidEmail", "Adresa de email nu e validă."));
        }

        GeneratedDocument? document = await context.GeneratedDocuments
            .Include(d => d.Rental)
            .ThenInclude(r => r.Tenant)
            .FirstOrDefaultAsync(d => d.Id == command.GeneratedDocumentId, cancellationToken);

        if (document is null || document.Rental.OwnerUserId != userContext.UserId)
        {
            return Result.Failure(Error.NotFound("Document.NotFound", "Documentul nu a fost găsit."));
        }

        if (document.Status == GeneratedDocumentStatus.Signed)
        {
            return Result.Failure(Error.Problem(
                "Signature.AlreadySigned",
                "Documentul e deja semnat. Regenerează-l dacă vrei o versiune nouă."));
        }

        // Retrimiterea invalidează linkurile vechi: două linkuri valabile pentru același document
        // înseamnă că nu se mai poate spune care a fost folosit.
        List<SignatureRequest> previous = await context.SignatureRequests
            .Where(r => r.GeneratedDocumentId == document.Id && r.UsedAtUtc == null)
            .ToListAsync(cancellationToken);

        foreach (SignatureRequest old in previous)
        {
            old.ExpiresAtUtc = DateTime.UtcNow;
        }

        string token = SignatureToken.Create();

        context.SignatureRequests.Add(new SignatureRequest
        {
            Id = Guid.NewGuid(),
            GeneratedDocumentId = document.Id,
            TokenHash = SignatureToken.Hash(token),
            Email = command.Email.Trim(),
            ExpiresAtUtc = DateTime.UtcNow.Add(SignatureToken.Lifetime),
        });

        document.Status = GeneratedDocumentStatus.SentForSignature;
        document.SentAtUtc = DateTime.UtcNow;
        document.SentToEmail = command.Email.Trim();

        await context.SaveChangesAsync(cancellationToken);

        string baseUrl = configuration["App:BaseUrl"]?.TrimEnd('/') ?? string.Empty;
        var link = new Uri($"{baseUrl}/semneaza/{token}");

        string html = mjml.Render(EmailTemplates.Notice(
            "Un document te așteaptă la semnat",
            document.Rental.Tenant.Name,
            [
                $"{DocumentLabel(document.Type)} pentru închirierea {document.Rental.PublicCode} e gata de semnat.",
                "Linkul e valabil șapte zile și poate fi folosit o singură dată. Nu ai nevoie de cont.",
            ],
            highlight: null,
            "Deschide documentul",
            link));

        await emailService.SendEmailAsync(command.Email.Trim(), "Document de semnat — RIDElance", html, cancellationToken);

        return Result.Success();
    }

    private static string DocumentLabel(string type) => type switch
    {
        "RentalContract" => "Contractul de închiriere",
        "HandoverProtocol" => "Procesul-verbal de predare",
        _ => "Procesul-verbal de primire",
    };
}
