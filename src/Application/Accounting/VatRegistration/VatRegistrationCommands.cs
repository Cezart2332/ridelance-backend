using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Accounting.Declarations;
using Application.Accounting.Pfas;
using Domain.Accounting;
using Domain.Users;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Accounting.VatRegistration;

/// <summary>O cerere D700, cum o văd contabilul și adminul.</summary>
public sealed record VatRegistrationDto(
    Guid Id,
    Guid PfaId,
    Guid UserId,
    string ClientName,
    string? Cui,
    VatRegistrationStatus Status,
    string Period,
    string? MissingData,
    string? RejectionReason,
    string? VatCode,
    DateOnly? VatCodeValidFrom,
    bool HasXml,
    bool HasPdf,
    bool HasCertificate,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public enum VatRegistrationFileKind
{
    Xml,
    Pdf,
    Certificate,
}

public sealed record VatRegistrationFile(byte[] Content, string ContentType, string FileName);

internal static class VatRegistrationDtos
{
    public static async Task<List<VatRegistrationDto>> LoadAsync(IApplicationDbContext db, IQueryable<VatRegistrationRequest> requests, CancellationToken cancellationToken)
    {
        var rows = await requests
            .AsNoTracking()
            .Select(r => new
            {
                Request = r,
                r.PfaRegistration.UserId,
                r.PfaRegistration.Cui,
                r.PfaRegistration.LegalName,
                r.PfaRegistration.HolderName,
                r.PfaRegistration.FullName,
                r.PfaRegistration.User.FirstName,
                r.PfaRegistration.User.LastName,
            })
            .ToListAsync(cancellationToken);

        return [.. rows
            .Select(row =>
            {
                VatRegistrationRequest r = row.Request;
                D700Validation? validation = AccountingJson.Deserialize<D700Validation?>(r.ValidationJson, null);
                return new VatRegistrationDto(
                    r.Id,
                    r.PfaRegistrationId,
                    row.UserId,
                    PfaNames.Of(row.LegalName, row.HolderName, row.FullName, row.FirstName, row.LastName),
                    row.Cui,
                    r.Status,
                    r.Period,
                    r.MissingData,
                    r.RejectionReason,
                    r.VatCode,
                    r.VatCodeValidFrom,
                    r.XmlDocumentId is not null,
                    r.PdfDocumentId is not null,
                    r.CertificateDocumentId is not null,
                    validation?.Errors ?? [],
                    validation?.Warnings ?? [],
                    r.CreatedAtUtc,
                    r.UpdatedAtUtc);
            })
            .OrderBy(dto => dto.ClientName, StringComparer.Create(new System.Globalization.CultureInfo("ro-RO"), ignoreCase: true))];
    }

    public static async Task<Result<VatRegistrationDto>> OneAsync(IApplicationDbContext db, Result<VatRegistrationRequest> result, CancellationToken cancellationToken)
    {
        if (result.IsFailure)
        {
            return Result.Failure<VatRegistrationDto>(result.Error);
        }

        Guid id = result.Value.Id;
        List<VatRegistrationDto> rows = await LoadAsync(db, db.VatRegistrationRequests.Where(r => r.Id == id), cancellationToken);
        return rows[0];
    }
}

/// <summary>Toate cererile D700; contabilul își vede doar clienții lui.</summary>
public sealed record ListVatRegistrationsQuery : IQuery<IReadOnlyList<VatRegistrationDto>>;

internal sealed class ListVatRegistrationsQueryHandler(IApplicationDbContext db, IUserContext userContext)
    : IQueryHandler<ListVatRegistrationsQuery, IReadOnlyList<VatRegistrationDto>>
{
    public async Task<Result<IReadOnlyList<VatRegistrationDto>>> Handle(ListVatRegistrationsQuery query, CancellationToken cancellationToken)
    {
        Guid me = userContext.UserId;
        bool contabil = await db.Users.AnyAsync(u => u.Id == me && u.Role == UserRole.Contabil, cancellationToken);

        // Doar ultima cerere a fiecărui PFA; cele vechi rămân în istoric.
        IQueryable<VatRegistrationRequest> latest = db.VatRegistrationRequests
            .Where(r => r.PfaRegistration.User.DeletedAtUtc == null && (!contabil || r.PfaRegistration.AssignedContabilId == me))
            .Where(r => r.CreatedAtUtc == db.VatRegistrationRequests.Where(o => o.PfaRegistrationId == r.PfaRegistrationId).Max(o => o.CreatedAtUtc));
        return await VatRegistrationDtos.LoadAsync(db, latest, cancellationToken);
    }
}

/// <summary>Cererea D700 a unui PFA (profilul de onboarding din admin); <c>null</c> dacă nu există.</summary>
public sealed record GetPfaVatRegistrationQuery(Guid PfaId) : IQuery<VatRegistrationDto?>;

internal sealed class GetPfaVatRegistrationQueryHandler(IApplicationDbContext db) : IQueryHandler<GetPfaVatRegistrationQuery, VatRegistrationDto?>
{
    public async Task<Result<VatRegistrationDto?>> Handle(GetPfaVatRegistrationQuery query, CancellationToken cancellationToken)
    {
        List<VatRegistrationDto> rows = await VatRegistrationDtos.LoadAsync(
            db,
            db.VatRegistrationRequests.Where(r => r.PfaRegistrationId == query.PfaId).OrderByDescending(r => r.CreatedAtUtc).Take(1),
            cancellationToken);
        return Result.Success<VatRegistrationDto?>(rows.Count > 0 ? rows[0] : null);
    }
}

public sealed record GetVatRegistrationFileQuery(Guid Id, VatRegistrationFileKind Kind) : IQuery<VatRegistrationFile>;

internal sealed class GetVatRegistrationFileQueryHandler(IApplicationDbContext db, DeclarationFiles files)
    : IQueryHandler<GetVatRegistrationFileQuery, VatRegistrationFile>
{
    public async Task<Result<VatRegistrationFile>> Handle(GetVatRegistrationFileQuery query, CancellationToken cancellationToken)
    {
        VatRegistrationRequest? request = await db.VatRegistrationRequests.AsNoTracking().SingleOrDefaultAsync(r => r.Id == query.Id, cancellationToken);
        if (request is null)
        {
            return Result.Failure<VatRegistrationFile>(VatRegistrationErrors.NotFound);
        }

        Guid? documentId = query.Kind switch
        {
            VatRegistrationFileKind.Xml => request.XmlDocumentId,
            VatRegistrationFileKind.Pdf => request.PdfDocumentId,
            _ => request.CertificateDocumentId,
        };
        (byte[] Content, Domain.Documents.Document Document)? stored = await files.ReadAsync(documentId, cancellationToken);
        return stored is { } file
            ? new VatRegistrationFile(file.Content, file.Document.ContentType, file.Document.OriginalFileName)
            : Result.Failure<VatRegistrationFile>(AccountingErrors.DocumentNotFound);
    }
}

/// <summary>Generează sau regenerează D700 din datele actuale ale PFA-ului.</summary>
public sealed record GenerateVatRegistrationCommand(Guid PfaId) : ICommand<VatRegistrationDto>;

internal sealed class GenerateVatRegistrationCommandHandler(IApplicationDbContext db, VatRegistrationService service, IUserContext userContext)
    : ICommandHandler<GenerateVatRegistrationCommand, VatRegistrationDto>
{
    public async Task<Result<VatRegistrationDto>> Handle(GenerateVatRegistrationCommand command, CancellationToken cancellationToken) =>
        await VatRegistrationDtos.OneAsync(db, await service.GenerateAsync(command.PfaId, userContext.UserId, cancellationToken), cancellationToken);
}

public sealed record ValidateVatRegistrationCommand(Guid Id) : ICommand<VatRegistrationDto>;

internal sealed class ValidateVatRegistrationCommandHandler(IApplicationDbContext db, VatRegistrationService service, IUserContext userContext)
    : ICommandHandler<ValidateVatRegistrationCommand, VatRegistrationDto>
{
    public async Task<Result<VatRegistrationDto>> Handle(ValidateVatRegistrationCommand command, CancellationToken cancellationToken) =>
        await VatRegistrationDtos.OneAsync(db, await service.ValidateAsync(command.Id, userContext.UserId, cancellationToken), cancellationToken);
}

/// <summary>Aprobare (<c>APPROVED</c>), respingere cu motiv (<c>REJECTED</c>), depunere (<c>SUBMITTED</c>).</summary>
public sealed record TransitionVatRegistrationCommand(Guid Id, VatRegistrationStatus To, string? Note) : ICommand<VatRegistrationDto>;

internal sealed class TransitionVatRegistrationCommandHandler(IApplicationDbContext db, VatRegistrationService service, IUserContext userContext)
    : ICommandHandler<TransitionVatRegistrationCommand, VatRegistrationDto>
{
    public async Task<Result<VatRegistrationDto>> Handle(TransitionVatRegistrationCommand command, CancellationToken cancellationToken) =>
        await VatRegistrationDtos.OneAsync(db, await service.TransitionAsync(command.Id, command.To, command.Note, userContext.UserId, cancellationToken), cancellationToken);
}

public sealed record RegisterVatCodeCommand(Guid Id, string VatCode, DateOnly ValidFrom, VatCertificateFile? Certificate) : ICommand<VatRegistrationDto>;

internal sealed class RegisterVatCodeCommandHandler(IApplicationDbContext db, VatRegistrationService service, IUserContext userContext)
    : ICommandHandler<RegisterVatCodeCommand, VatRegistrationDto>
{
    public async Task<Result<VatRegistrationDto>> Handle(RegisterVatCodeCommand command, CancellationToken cancellationToken) =>
        await VatRegistrationDtos.OneAsync(
            db,
            await service.RegisterAsync(command.Id, command.VatCode, command.ValidFrom, command.Certificate, userContext.UserId, cancellationToken),
            cancellationToken);
}
