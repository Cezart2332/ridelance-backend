using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Accounting.Contracts;
using Application.Accounting.Documents;
using Application.Accounting.Tax;
using Domain.Accounting;
using Domain.Documents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SharedKernel;

namespace Application.Accounting.Declarations;

/// <summary>
/// Acțiunile pe declarații (spec contabilitate B5): tranzițiile din §3.2, recipisa și rectificativa.
/// Fiecare lucrează doar pe versiunea curentă a unui dosar activ și lasă o intrare de audit.
/// </summary>
internal sealed class DeclarationActions(
    IApplicationDbContext db,
    DeclarationFiles files,
    DeclarationValidator validator,
    IOptions<AccountingOptions> options,
    Annual.AnnualDeclarationService? annual = null)
{
    public async Task<Result> TransitionAsync(Guid versionId, DeclarationAction action, string? note, Guid? userId, CancellationToken cancellationToken)
    {
        Result<DeclarationVersion> found = await CurrentVersionAsync(versionId, cancellationToken);
        if (found.IsFailure)
        {
            return found;
        }

        DeclarationVersion version = found.Value;
        if (!DeclarationTypes.IsAnnual(version.Declaration.Type) && !Months.MonthlyDeclarationPeriod.IsCompleted(version.Declaration.Period, DateTime.UtcNow))
        {
            return Result.Failure(Error.Problem("Accounting.MonthInProgress", Months.MonthlyDeclarationPeriod.Message));
        }
        if (!DeclarationStateMachine.IsAllowed(version.Status, action))
        {
            return Result.Failure(DeclarationErrors.InvalidTransition(version.Status, action));
        }

        string? text = string.IsNullOrWhiteSpace(note) ? null : note.Trim();

        // QA 5: tranzițiile manuale de status (semnat, depus, respins, index) doar de Admin, cu motiv.
        // Sistemul (SPV, fără utilizator) poate marca o recipisă cu erori.
        if (action is DeclarationAction.MarkSigned or DeclarationAction.MarkSubmitted or DeclarationAction.MarkRejected or DeclarationAction.RecordIndex)
        {
            if (userId is { } user && !await db.Users.AnyAsync(u => u.Id == user && u.Role == Domain.Users.UserRole.Admin, cancellationToken))
            {
                return Result.Failure(DeclarationErrors.ManualTransitionAdminOnly);
            }

            if (text is null && action is DeclarationAction.MarkSigned or DeclarationAction.MarkSubmitted)
            {
                return Result.Failure(DeclarationErrors.TransitionReasonRequired);
            }
        }

        switch (action)
        {
            // Anualele (D207, D205, D212) se depun manual: validarea recalculează și compară cu snapshot-ul.
            case DeclarationAction.Validate when DeclarationTypes.IsAnnual(version.Declaration.Type):
                return annual is null ? Result.Failure(DeclarationErrors.AnnualUnavailable) : await annual.ValidateAsync(version, userId, cancellationToken);

            case DeclarationAction.Validate:
                Result<ValidationRun> run = await validator.ValidateAsync(version.Id, userId, cancellationToken);
                return run.IsSuccess ? Result.Success() : Result.Failure(run.Error);

            case DeclarationAction.Regenerate:
                return await RegenerateAsync(version, text, userId, cancellationToken);

            case DeclarationAction.MarkRejected when text is null:
                return Result.Failure(DeclarationErrors.RejectionReasonRequired);

            case DeclarationAction.RecordIndex when text is null:
                return Result.Failure(DeclarationErrors.IndexRequired);

            case DeclarationAction.RecordIndex:
                version.AnafIndex = text;
                DeclarationStateMachine.Move(version, DeclarationStatus.IndexReceived, userId, $"Index ANAF {text}");
                Audit(version, "INDEX", new { status = DeclarationStatus.Submitted }, new { status = DeclarationStatus.IndexReceived, anafIndex = text }, null, userId);
                await db.SaveChangesAsync(cancellationToken);
                return Result.Success();

            default:
                DeclarationStatus from = version.Status;
                DeclarationStatus to = action switch
                {
                    DeclarationAction.MarkSigned => DeclarationStatus.Signed,
                    DeclarationAction.MarkSubmitted => DeclarationStatus.Submitted,
                    _ => DeclarationStatus.Rejected,
                };
                DeclarationStateMachine.Move(version, to, userId, text);
                Audit(version, AccountingJson.Serialize(action).Trim('"'), new { status = from }, new { status = to }, text, userId);
                await db.SaveChangesAsync(cancellationToken);
                return Result.Success();
        }
    }

    /// <summary>Recipisa ANAF: fișierul și numărul opțional; versiunea depusă devine <c>ACCEPTED</c>.</summary>
    public async Task<Result> UploadReceiptAsync(Guid versionId, ReceiptFile file, string? receiptNumber, Guid? userId, CancellationToken cancellationToken)
    {
        Result<DeclarationVersion> found = await CurrentVersionAsync(versionId, cancellationToken);
        if (found.IsFailure)
        {
            return found;
        }

        DeclarationVersion version = found.Value;
        if (version.Status is not (DeclarationStatus.Submitted or DeclarationStatus.IndexReceived))
        {
            return Result.Failure(DeclarationErrors.ReceiptOnlyWhenSubmitted);
        }

        if (!ReceiptFile.IsAllowed(file.FileName, file.ContentType))
        {
            return Result.Failure(DeclarationErrors.ReceiptFileType);
        }

        if (file.Content.Length > options.Value.MaxUploadBytes)
        {
            return Result.Failure(AccountingErrors.FileTooLarge);
        }

        string? number = string.IsNullOrWhiteSpace(receiptNumber) ? null : receiptNumber.Trim();
        Document document = await files.StoreAsync(
            version.Declaration.PfaRegistrationId, file.Content, file.FileName, file.ContentType, cancellationToken, DocumentOrigin.AccountingUpload);
        version.ReceiptDocumentId = document.Id;
        version.ReceiptNumber = number;
        DeclarationStatus before = version.Status;
        DeclarationStateMachine.Move(version, DeclarationStatus.Accepted, userId, number is null ? null : $"Recipisa nr. {number}");
        Audit(version, "RECEIPT", new { status = before }, new { status = DeclarationStatus.Accepted, receiptNumber = number, file = file.FileName }, null, userId);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    /// <summary>
    /// Rectificativa: doar dintr-o versiune curentă <c>ACCEPTED</c>. Versiunea nouă e recalculată din
    /// datele de acum și deblochează documentele pe care le include; cea veche rămâne neschimbată.
    /// </summary>
    public async Task<Result<Guid>> CreateRectificationAsync(Guid declarationId, string? reason, Guid? userId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result.Failure<Guid>(DeclarationErrors.RectificationReasonRequired);
        }

        Declaration? declaration = await db.Declarations.Include(d => d.Versions).SingleOrDefaultAsync(d => d.Id == declarationId, cancellationToken);
        if (declaration is null)
        {
            return Result.Failure<Guid>(DeclarationErrors.DeclarationNotFound);
        }

        Result writable = await PlatformDocumentSupport.EnsureWritableAsync(db, declaration.PfaRegistrationId, declaration.Period, cancellationToken);
        if (writable.IsFailure)
        {
            return Result.Failure<Guid>(writable.Error);
        }

        DeclarationVersion current = declaration.Versions.MaxBy(v => v.VersionNo)!;
        if (current.Status != DeclarationStatus.Accepted)
        {
            return Result.Failure<Guid>(DeclarationErrors.RectificationOnlyWhenAccepted);
        }

        if (DeclarationTypes.IsAnnual(declaration.Type))
        {
            return await CreateAnnualRectificationAsync(declaration, current, reason.Trim(), userId, cancellationToken);
        }

        Result<DeclarationDraft> draft = await DeclarationContent.CalculateAsync(
            db, declaration.PfaRegistrationId, declaration.Period, declaration.Type,
            TaxEngineSettings.ForPeriod(await TaxRuleSet.LoadAsync(db, cancellationToken), declaration.Period), cancellationToken);
        if (draft.IsFailure)
        {
            return Result.Failure<Guid>(draft.Error);
        }

        string text = reason.Trim();
        var version = new DeclarationVersion
        {
            Id = Guid.NewGuid(),
            DeclarationId = declaration.Id,
            VersionNo = current.VersionNo + 1,
            Kind = DeclarationVersionKind.Rectificative,
            Status = DeclarationStatus.Generated,
            RectificationReason = text,
            StatusHistoryJson = AccountingJson.Serialize(new[] { new Months.StatusHistoryRecord(null, DeclarationStatus.Generated, DateTime.UtcNow, userId, text) }),
            CreatedByUserId = userId,
            CreatedAtUtc = DateTime.UtcNow,
        };
        db.DeclarationVersions.Add(version);
        await DeclarationContent.ApplyAsync(
            db,
            files,
            declaration,
            version,
            draft.Value,
            await files.TaxpayerAsync(declaration.PfaRegistrationId, cancellationToken, declaration.Period),
            await db.AnafDeclarationSchemas.AsNoTracking().ToListAsync(cancellationToken),
            DeclarationContent.XmlBlocker(declaration.Type, version.Kind, options.Value.D100CorrectionProcedure),
            cancellationToken);

        AccountingAudit.Record(
            db, declaration.PfaRegistrationId, nameof(DeclarationVersion), version.Id, "RECTIFICATION",
            new { versionNo = current.VersionNo, amount = current.Amount },
            new { type = declaration.Type, versionNo = version.VersionNo, amount = version.Amount },
            text,
            userId);
        await RectificationTasks.ResolveAsync(db, declaration.Id, version.Id, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return version.Id;
    }

    /// <summary>Rectificativa anuală: versiune nouă din datele de acum; cea depusă rămâne neschimbată.</summary>
    private async Task<Result<Guid>> CreateAnnualRectificationAsync(Declaration declaration, DeclarationVersion current, string text, Guid? userId, CancellationToken cancellationToken)
    {
        if (annual is null)
        {
            return Result.Failure<Guid>(DeclarationErrors.AnnualUnavailable);
        }

        Result<Annual.AnnualData> draft = await annual.DraftAsync(declaration, cancellationToken);
        if (draft.IsFailure)
        {
            return Result.Failure<Guid>(draft.Error);
        }

        var version = new DeclarationVersion
        {
            Id = Guid.NewGuid(),
            DeclarationId = declaration.Id,
            VersionNo = current.VersionNo + 1,
            Kind = DeclarationVersionKind.Rectificative,
            Status = DeclarationStatus.Generated,
            RectificationReason = text,
            StatusHistoryJson = AccountingJson.Serialize(new[] { new Months.StatusHistoryRecord(null, DeclarationStatus.Generated, DateTime.UtcNow, userId, text) }),
            CreatedByUserId = userId,
            CreatedAtUtc = DateTime.UtcNow,
        };
        Annual.AnnualDeclarationService.Apply(version, draft.Value, declaration.Type);
        db.DeclarationVersions.Add(version);
        AccountingAudit.Record(
            db, declaration.PfaRegistrationId, nameof(DeclarationVersion), version.Id, "RECTIFICATION",
            new { versionNo = current.VersionNo, amount = current.Amount },
            new { type = declaration.Type, versionNo = version.VersionNo, amount = version.Amount },
            text,
            userId);
        await RectificationTasks.ResolveAsync(db, declaration.Id, version.Id, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return version.Id;
    }

    /// <summary>Regenerarea, pe aceeași versiune: recalcul din datele de acum, XML nou, validarea de la capăt.</summary>
    private async Task<Result> RegenerateAsync(DeclarationVersion version, string? note, Guid? userId, CancellationToken cancellationToken)
    {
        Declaration declaration = version.Declaration;
        if (DeclarationTypes.IsAnnual(declaration.Type))
        {
            if (annual is null)
            {
                return Result.Failure(DeclarationErrors.AnnualUnavailable);
            }

            Result<Annual.AnnualData> annualDraft = await annual.DraftAsync(declaration, cancellationToken);
            if (annualDraft.IsFailure)
            {
                return annualDraft;
            }

            decimal previous = version.Amount;
            DeclarationStatus source = version.Status;
            Annual.AnnualDeclarationService.Apply(version, annualDraft.Value, declaration.Type);
            DeclarationStateMachine.Move(version, DeclarationStatus.Generated, userId, note);
            Audit(version, "REGENERATE", new { status = source, amount = previous }, new { status = DeclarationStatus.Generated, amount = version.Amount }, note, userId);
            await db.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }
        Result<DeclarationDraft> draft = await DeclarationContent.CalculateAsync(
            db, declaration.PfaRegistrationId, declaration.Period, declaration.Type,
            TaxEngineSettings.ForPeriod(await TaxRuleSet.LoadAsync(db, cancellationToken), declaration.Period), cancellationToken);
        if (draft.IsFailure)
        {
            return draft;
        }

        decimal before = version.Amount;
        DeclarationStatus from = version.Status;
        await DeclarationContent.ApplyAsync(
            db,
            files,
            declaration,
            version,
            draft.Value,
            await files.TaxpayerAsync(declaration.PfaRegistrationId, cancellationToken, declaration.Period),
            await db.AnafDeclarationSchemas.AsNoTracking().ToListAsync(cancellationToken),
            DeclarationContent.XmlBlocker(declaration.Type, version.Kind, options.Value.D100CorrectionProcedure),
            cancellationToken);
        DeclarationStateMachine.Move(version, DeclarationStatus.Generated, userId, note);
        Audit(version, "REGENERATE", new { status = from, amount = before }, new { status = DeclarationStatus.Generated, amount = version.Amount }, note, userId);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    /// <summary>Versiunea, dacă e cea curentă a declarației și dosarul e activ.</summary>
    private async Task<Result<DeclarationVersion>> CurrentVersionAsync(Guid versionId, CancellationToken cancellationToken)
    {
        DeclarationVersion? version = await db.DeclarationVersions.Include(v => v.Declaration).SingleOrDefaultAsync(v => v.Id == versionId, cancellationToken);
        if (version is null)
        {
            return Result.Failure<DeclarationVersion>(DeclarationErrors.VersionNotFound);
        }

        Result writable = await PlatformDocumentSupport.EnsureWritableAsync(db, version.Declaration.PfaRegistrationId, version.Declaration.Period, cancellationToken);
        if (writable.IsFailure)
        {
            return Result.Failure<DeclarationVersion>(writable.Error);
        }

        int latest = await db.DeclarationVersions.Where(v => v.DeclarationId == version.DeclarationId).MaxAsync(v => v.VersionNo, cancellationToken);
        return version.VersionNo == latest ? version : Result.Failure<DeclarationVersion>(DeclarationErrors.NotCurrentVersion);
    }

    private void Audit(DeclarationVersion version, string action, object before, object after, string? reason, Guid? userId) =>
        AccountingAudit.Record(db, version.Declaration.PfaRegistrationId, nameof(DeclarationVersion), version.Id, action, before, after, reason, userId);
}

/// <summary>Fișierul recipisei, citit din cerere.</summary>
public sealed record ReceiptFile(string FileName, string ContentType, byte[] Content)
{
    private static readonly string[] Extensions = [".pdf", ".png", ".jpg", ".jpeg"];

    public static bool IsAllowed(string fileName, string contentType) =>
        Extensions.Any(extension => fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) ||
        contentType is "application/pdf" or "image/png" or "image/jpeg";
}

/// <summary><c>POST /accounting/declaration-versions/{id}/transitions</c></summary>
public sealed record TransitionDeclarationVersionCommand(Guid VersionId, DeclarationAction Action, string? Note) : ICommand<DeclarationVersionDto>;

internal sealed class TransitionDeclarationVersionCommandHandler(IApplicationDbContext db, IUserContext userContext, DeclarationActions actions)
    : ICommandHandler<TransitionDeclarationVersionCommand, DeclarationVersionDto>
{
    public async Task<Result<DeclarationVersionDto>> Handle(TransitionDeclarationVersionCommand command, CancellationToken cancellationToken)
    {
        Result result = await actions.TransitionAsync(command.VersionId, command.Action, command.Note, userContext.UserId, cancellationToken);
        return result.IsFailure
            ? Result.Failure<DeclarationVersionDto>(result.Error)
            : (await DeclarationDtos.VersionAsync(db, command.VersionId, cancellationToken))!;
    }
}

/// <summary><c>POST /accounting/declaration-versions/{id}/receipt</c> — multipart(file, receiptNumber?).</summary>
public sealed record UploadDeclarationReceiptCommand(Guid VersionId, ReceiptFile File, string? ReceiptNumber) : ICommand<DeclarationVersionDto>;

internal sealed class UploadDeclarationReceiptCommandHandler(IApplicationDbContext db, IUserContext userContext, DeclarationActions actions)
    : ICommandHandler<UploadDeclarationReceiptCommand, DeclarationVersionDto>
{
    public async Task<Result<DeclarationVersionDto>> Handle(UploadDeclarationReceiptCommand command, CancellationToken cancellationToken)
    {
        Result result = await actions.UploadReceiptAsync(command.VersionId, command.File, command.ReceiptNumber, userContext.UserId, cancellationToken);
        return result.IsFailure
            ? Result.Failure<DeclarationVersionDto>(result.Error)
            : (await DeclarationDtos.VersionAsync(db, command.VersionId, cancellationToken))!;
    }
}

/// <summary><c>POST /accounting/declarations/{id}/rectification</c> — <c>{ reason }</c>.</summary>
public sealed record CreateRectificationCommand(Guid DeclarationId, string? Reason) : ICommand<DeclarationVersionDto>;

internal sealed class CreateRectificationCommandHandler(IApplicationDbContext db, IUserContext userContext, DeclarationActions actions)
    : ICommandHandler<CreateRectificationCommand, DeclarationVersionDto>
{
    public async Task<Result<DeclarationVersionDto>> Handle(CreateRectificationCommand command, CancellationToken cancellationToken)
    {
        Result<Guid> created = await actions.CreateRectificationAsync(command.DeclarationId, command.Reason, userContext.UserId, cancellationToken);
        return created.IsFailure
            ? Result.Failure<DeclarationVersionDto>(created.Error)
            : (await DeclarationDtos.VersionAsync(db, created.Value, cancellationToken))!;
    }
}
