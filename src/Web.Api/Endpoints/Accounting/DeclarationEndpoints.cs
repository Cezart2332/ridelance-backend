using Application.Abstractions.Messaging;
using Application.Accounting.Contracts;
using Application.Accounting.Declarations;
using Infrastructure.Authorization;
using SharedKernel;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Accounting;

/// <summary>Declarațiile și versiunile lor: detaliu, calcul, fișiere, validare (spec contabilitate §4.4, B4).</summary>
internal sealed class DeclarationEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("accounting")
            .RequireAuthorization(Permissions.ManageAccounting)
            .WithTags(Tags.Accounting);

        group.MapGet("declarations/{declarationId:guid}", async (
            Guid declarationId,
            IQueryHandler<GetDeclarationQuery, DeclarationDetail> handler,
            CancellationToken cancellationToken) =>
        {
            Result<DeclarationDetail> result = await handler.Handle(new GetDeclarationQuery(declarationId), cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        });

        group.MapGet("declaration-versions/{versionId:guid}/breakdown", async (
            Guid versionId,
            IQueryHandler<GetDeclarationBreakdownQuery, DeclarationBreakdown> handler,
            CancellationToken cancellationToken) =>
        {
            Result<DeclarationBreakdown> result = await handler.Handle(new GetDeclarationBreakdownQuery(versionId), cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        });

        group.MapGet("declaration-versions/{versionId:guid}/validation", async (
            Guid versionId,
            IQueryHandler<GetDeclarationValidationQuery, ValidationResult?> handler,
            CancellationToken cancellationToken) =>
        {
            Result<ValidationResult?> result = await handler.Handle(new GetDeclarationValidationQuery(versionId), cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        });

        group.MapGet("declaration-versions/{versionId:guid}/xml", (
            Guid versionId,
            IQueryHandler<GetDeclarationFileQuery, DeclarationFile> handler,
            CancellationToken cancellationToken) => File(versionId, DeclarationFileKind.Xml, handler, cancellationToken));

        group.MapGet("declaration-versions/{versionId:guid}/pdf", (
            Guid versionId,
            IQueryHandler<GetDeclarationFileQuery, DeclarationFile> handler,
            CancellationToken cancellationToken) => File(versionId, DeclarationFileKind.Pdf, handler, cancellationToken));

        group.MapPost("declaration-versions/{versionId:guid}/transitions", async (
            Guid versionId,
            TransitionRequest request,
            ICommandHandler<TransitionDeclarationVersionCommand, DeclarationVersionDto> handler,
            CancellationToken cancellationToken) =>
        {
            Result<DeclarationVersionDto> result = await handler.Handle(
                new TransitionDeclarationVersionCommand(versionId, request.Action, request.Note), cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        });
    }

    private static async Task<IResult> File(
        Guid versionId,
        DeclarationFileKind kind,
        IQueryHandler<GetDeclarationFileQuery, DeclarationFile> handler,
        CancellationToken cancellationToken)
    {
        Result<DeclarationFile> result = await handler.Handle(new GetDeclarationFileQuery(versionId, kind), cancellationToken);
        return result.Match(file => Results.File(file.Content, file.ContentType, file.FileName), CustomResults.Problem);
    }
}
