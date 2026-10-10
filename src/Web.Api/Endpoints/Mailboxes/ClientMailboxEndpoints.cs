using Application.Abstractions.Messaging;
using Application.Mailboxes;
using SharedKernel;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Mailboxes;

/// <summary>
/// Emailul operațional al clientului (spec faza 2, Migadu). Adminul îl vede, îl creează, își ia
/// credențialele identității RIDElance și îl predă la offboarding; clientul își vede doar adresa.
/// Parolele ies exclusiv prin <c>credentials</c>, auditat.
/// </summary>
internal sealed class ClientMailboxEndpoints : IEndpoint
{
    private const string Manage = "pfa:manage";

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("admin/onboarding/{id:guid}/mailbox", async (
            Guid id,
            IQueryHandler<GetClientMailboxQuery, ClientMailboxDto> handler,
            CancellationToken cancellationToken) =>
            (await handler.Handle(new GetClientMailboxQuery(id), cancellationToken)).Match(Results.Ok, CustomResults.Problem))
        .RequireAuthorization()
        .HasPermission(Manage)
        .WithTags(Tags.PfaRegistrations);

        // „Creează email operațional” și „Reîncearcă”: intră în coadă, îl face jobul.
        app.MapPost("admin/onboarding/{id:guid}/mailbox", async (
            Guid id,
            ICommandHandler<RequestClientMailboxCommand, ClientMailboxDto> handler,
            CancellationToken cancellationToken) =>
            (await handler.Handle(new RequestClientMailboxCommand(id), cancellationToken)).Match(Results.Ok, CustomResults.Problem))
        .RequireAuthorization()
        .HasPermission(Manage)
        .WithTags(Tags.PfaRegistrations);

        // POST: fiecare afișare a credențialelor scrie în jurnal.
        app.MapPost("admin/onboarding/{id:guid}/mailbox/credentials", async (
            Guid id,
            ICommandHandler<RevealOpsCredentialsCommand, OpsCredentialsDto> handler,
            CancellationToken cancellationToken) =>
            (await handler.Handle(new RevealOpsCredentialsCommand(id), cancellationToken)).Match(Results.Ok, CustomResults.Problem))
        .RequireAuthorization()
        .HasPermission(Manage)
        .WithTags(Tags.PfaRegistrations);

        app.MapPost("admin/onboarding/{id:guid}/mailbox/transfer", async (
            Guid id,
            ICommandHandler<TransferClientMailboxCommand, ClientMailboxDto> handler,
            CancellationToken cancellationToken) =>
            (await handler.Handle(new TransferClientMailboxCommand(id), cancellationToken)).Match(Results.Ok, CustomResults.Problem))
        .RequireAuthorization()
        .HasPermission(Manage)
        .WithTags(Tags.PfaRegistrations);

        app.MapGet("admin/mailboxes/usage", async (
            IQueryHandler<GetMailboxUsageQuery, MailboxUsageDto> handler,
            CancellationToken cancellationToken) =>
            (await handler.Handle(new GetMailboxUsageQuery(), cancellationToken)).Match(Results.Ok, CustomResults.Problem))
        .RequireAuthorization()
        .HasPermission(Manage)
        .WithTags(Tags.PfaRegistrations);

        app.MapGet("pfa/mailbox", async (
            IQueryHandler<GetOwnMailboxQuery, OwnMailboxDto?> handler,
            CancellationToken cancellationToken) =>
        {
            Result<OwnMailboxDto?> result = await handler.Handle(new GetOwnMailboxQuery(), cancellationToken);
            if (result.IsFailure)
            {
                return CustomResults.Problem(result);
            }

            return result.Value is null ? Results.NoContent() : Results.Ok(result.Value);
        })
        .RequireAuthorization()
        .WithTags(Tags.PfaRegistrations);
    }
}
