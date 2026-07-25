using Application.Abstractions.Authentication;
using Application.Abstractions.Messaging;
using Application.Documents.ExtractedFields;
using SharedKernel;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Documents;

/// <summary>Câmpurile extrase prin OCR: vizualizare, confirmare (client) și corectare (admin).</summary>
internal sealed class ExtractedFields : IEndpoint
{
    public sealed record ConfirmFieldItem(string FieldKey, string? Value);

    public sealed record ConfirmRequest(IReadOnlyList<ConfirmFieldItem> Fields);

    public sealed record CorrectRequest(string? Value, string ChangeReason);

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("documents/{id:guid}/extracted-fields", async (
            Guid id,
            IUserContext userContext,
            IQueryHandler<GetExtractedFieldsQuery, ExtractedFieldsResponse> handler,
            CancellationToken cancellationToken) =>
        {
            Result<ExtractedFieldsResponse> result =
                await handler.Handle(new GetExtractedFieldsQuery(userContext.UserId, id), cancellationToken);

            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .RequireAuthorization()
        .WithTags(Tags.Documents);

        app.MapPost("documents/{id:guid}/extracted-fields/confirm", async (
            Guid id,
            ConfirmRequest request,
            IUserContext userContext,
            ICommandHandler<ConfirmExtractedFieldsCommand, ExtractedFieldsResponse> handler,
            CancellationToken cancellationToken) =>
        {
            var fields = (request.Fields ?? [])
                .Select(f => new ConfirmedFieldInput(f.FieldKey, f.Value))
                .ToList();

            Result<ExtractedFieldsResponse> result = await handler.Handle(
                new ConfirmExtractedFieldsCommand(userContext.UserId, id, fields), cancellationToken);

            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .RequireAuthorization()
        .WithTags(Tags.Documents);

        // Admin — vede câmpurile extrase ale oricărui document.
        app.MapGet("admin/documents/{id:guid}/extracted-fields", async (
            Guid id,
            IQueryHandler<GetExtractedFieldsAdminQuery, ExtractedFieldsResponse> handler,
            CancellationToken cancellationToken) =>
        {
            Result<ExtractedFieldsResponse> result =
                await handler.Handle(new GetExtractedFieldsAdminQuery(id), cancellationToken);

            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .RequireAuthorization()
        .HasPermission("pfa:view")
        .WithTags(Tags.Documents);

        // Admin — corectează o valoare extrasă (motiv obligatoriu).
        app.MapPut("admin/extracted-fields/{fieldId:guid}", async (
            Guid fieldId,
            CorrectRequest request,
            IUserContext userContext,
            ICommandHandler<CorrectExtractedFieldCommand> handler,
            CancellationToken cancellationToken) =>
        {
            Result result = await handler.Handle(
                new CorrectExtractedFieldCommand(userContext.UserId, fieldId, request.Value, request.ChangeReason),
                cancellationToken);

            return result.Match(Results.NoContent, CustomResults.Problem);
        })
        .RequireAuthorization()
        .HasPermission("pfa:manage")
        .WithTags(Tags.Documents);
    }
}
