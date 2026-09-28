using System.Globalization;
using Application.Abstractions.Messaging;
using Application.Accounting.Spv;
using Infrastructure.Authorization;
using Microsoft.AspNetCore.Mvc;
using SharedKernel;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Accounting;

/// <summary>
/// SPV prin aplicația desktop RIDElance SPV. Aplicația se autentifică cu cheia ei
/// (<c>X-Agent-Key</c>), nu cu sesiunea din browser; cheile le emite adminul din web.
/// </summary>
internal sealed class SpvEndpoints : IEndpoint
{
    private const string KeyHeader = "X-Agent-Key";

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder agent = app.MapGroup("spv/agent").AllowAnonymous().WithTags(Tags.Accounting);

        agent.MapGet("status", async (HttpRequest http, IQueryHandler<GetSpvAgentStatusQuery, SpvAgentStatus> handler, CancellationToken cancellationToken) =>
        {
            Result<SpvAgentStatus> result = await handler.Handle(new GetSpvAgentStatusQuery(Key(http)), cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        });

        agent.MapPost("runs", async (HttpRequest http, StartRunRequest request, ICommandHandler<StartSpvRunCommand, SpvRunStart> handler, CancellationToken cancellationToken) =>
        {
            Result<SpvRunStart> result = await handler.Handle(new StartSpvRunCommand(Key(http), request.Machine, request.AgentVersion), cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        });

        agent.MapPost("runs/{runId:guid}/new-ids", async (
            HttpRequest http,
            Guid runId,
            NewIdsRequest request,
            ICommandHandler<FilterNewSpvMessagesCommand, IReadOnlyList<string>> handler,
            CancellationToken cancellationToken) =>
        {
            Result<IReadOnlyList<string>> result = await handler.Handle(new FilterNewSpvMessagesCommand(Key(http), runId, request.Ids ?? []), cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        });

        agent.MapPost("runs/{runId:guid}/messages", async (
            HttpRequest http,
            Guid runId,
            [FromForm] string id,
            [FromForm] string cif,
            [FromForm] string tip,
            [FromForm] string dataCreare,
            [FromForm] string? idSolicitare,
            [FromForm] string? detalii,
            IFormFile? file,
            ICommandHandler<ReceiveSpvMessageCommand> handler,
            CancellationToken cancellationToken) =>
        {
            DateTime created = DateTime.TryParse(dataCreare, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime parsed)
                ? parsed
                : DateTime.UtcNow;
            SpvIncomingFile? incoming = null;
            if (file is { Length: > 0 })
            {
                using var buffer = new MemoryStream();
                await file.CopyToAsync(buffer, cancellationToken);
                incoming = new SpvIncomingFile(file.FileName, file.ContentType, buffer.ToArray());
            }

            Result result = await handler.Handle(
                new ReceiveSpvMessageCommand(Key(http), runId, new SpvIncomingMessage(id, cif, tip, created, idSolicitare, detalii), incoming),
                cancellationToken);
            return result.Match(Results.NoContent, CustomResults.Problem);
        })
        .DisableAntiforgery();

        agent.MapPost("runs/{runId:guid}/requests/{requestId:guid}", async (
            HttpRequest http,
            Guid runId,
            Guid requestId,
            RequestResult request,
            ICommandHandler<ReportSpvRequestCommand> handler,
            CancellationToken cancellationToken) =>
        {
            Result result = await handler.Handle(new ReportSpvRequestCommand(Key(http), runId, requestId, request.AnafRequestId, request.Error), cancellationToken);
            return result.Match(Results.NoContent, CustomResults.Problem);
        });

        agent.MapPost("runs/{runId:guid}/finish", async (HttpRequest http, Guid runId, FinishRequest request, ICommandHandler<FinishSpvRunCommand> handler, CancellationToken cancellationToken) =>
        {
            Result result = await handler.Handle(new FinishSpvRunCommand(Key(http), runId, request.Error), cancellationToken);
            return result.Match(Results.NoContent, CustomResults.Problem);
        });

        RouteGroupBuilder view = app.MapGroup("accounting").RequireAuthorization(Permissions.ManageAccounting).WithTags(Tags.Accounting);

        view.MapGet("pfas/{pfaId:guid}/spv", async (Guid pfaId, IQueryHandler<GetPfaSpvQuery, PfaSpvDto> handler, CancellationToken cancellationToken) =>
        {
            Result<PfaSpvDto> result = await handler.Handle(new GetPfaSpvQuery(pfaId), cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        });

        view.MapPost("pfas/{pfaId:guid}/spv/requests", async (Guid pfaId, QueueRequest request, ICommandHandler<QueueSpvRequestCommand> handler, CancellationToken cancellationToken) =>
        {
            Result result = await handler.Handle(new QueueSpvRequestCommand(pfaId, request.Type, request.Parameters), cancellationToken);
            return result.Match(Results.NoContent, CustomResults.Problem);
        });

        view.MapPost("spv/messages/{id:guid}/read", async (Guid id, ICommandHandler<MarkSpvMessageReadCommand> handler, CancellationToken cancellationToken) =>
        {
            Result result = await handler.Handle(new MarkSpvMessageReadCommand(id), cancellationToken);
            return result.Match(Results.NoContent, CustomResults.Problem);
        });

        view.MapGet("spv/messages/{id:guid}/file", async (Guid id, IQueryHandler<GetSpvMessageFileQuery, SpvFile> handler, CancellationToken cancellationToken) =>
        {
            Result<SpvFile> result = await handler.Handle(new GetSpvMessageFileQuery(id), cancellationToken);
            return result.Match(file => Results.File(file.Content, file.ContentType, file.FileName), CustomResults.Problem);
        });

        RouteGroupBuilder admin = app.MapGroup("anaf/spv").RequireAuthorization(Permissions.ManageAnaf).WithTags(Tags.Accounting);

        admin.MapGet("", async (IQueryHandler<GetSpvOverviewQuery, SpvOverviewDto> handler, CancellationToken cancellationToken) =>
        {
            Result<SpvOverviewDto> result = await handler.Handle(new GetSpvOverviewQuery(), cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        });

        admin.MapPost("keys", async (CreateKeyRequest request, ICommandHandler<CreateSpvAgentKeyCommand, CreatedSpvAgentKey> handler, CancellationToken cancellationToken) =>
        {
            Result<CreatedSpvAgentKey> result = await handler.Handle(new CreateSpvAgentKeyCommand(request.Name), cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        });

        admin.MapDelete("keys/{keyId:guid}", async (Guid keyId, ICommandHandler<RevokeSpvAgentKeyCommand> handler, CancellationToken cancellationToken) =>
        {
            Result result = await handler.Handle(new RevokeSpvAgentKeyCommand(keyId), cancellationToken);
            return result.Match(Results.NoContent, CustomResults.Problem);
        });
    }

    private static string? Key(HttpRequest http) => http.Headers[KeyHeader].FirstOrDefault();

    internal sealed record StartRunRequest(string? Machine, string? AgentVersion);

    internal sealed record NewIdsRequest(IReadOnlyList<string>? Ids);

    internal sealed record RequestResult(string? AnafRequestId, string? Error);

    internal sealed record FinishRequest(string? Error);

    internal sealed record QueueRequest(string Type, IReadOnlyDictionary<string, string>? Parameters);

    internal sealed record CreateKeyRequest(string? Name);
}
