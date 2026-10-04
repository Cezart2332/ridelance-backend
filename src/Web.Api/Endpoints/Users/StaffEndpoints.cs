using Application.Abstractions.Messaging;
using Application.Users.Login;
using Application.Users.Staff;
using Application.Users.TwoFactor;
using SharedKernel;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Users;

/// <summary>
/// Echipa (Admin, Contabil): invitațiile, crearea contului din invitație și autentificarea în doi
/// pași. Drepturile (admin, proprietar) le verifică handlerele.
/// </summary>
internal sealed class StaffEndpoints : IEndpoint
{
    public sealed record InviteRequest(string FullName, string Email, string Role);

    public sealed record AcceptRequest(string Password);

    public sealed record ChallengeRequest(string ChallengeToken);

    public sealed record CodeRequest(string ChallengeToken, string Code);

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder admin = app.MapGroup("admin/staff").RequireAuthorization().WithTags(Tags.Users);

        admin.MapGet("", async (IQueryHandler<GetStaffQuery, StaffOverviewDto> handler, CancellationToken cancellationToken) =>
            (await handler.Handle(new GetStaffQuery(), cancellationToken)).Match(Results.Ok, CustomResults.Problem));

        admin.MapPost("invitations", async (InviteRequest request, ICommandHandler<InviteStaffCommand, StaffInvitationDto> handler, CancellationToken cancellationToken) =>
            (await handler.Handle(new InviteStaffCommand(request.FullName, request.Email, request.Role), cancellationToken)).Match(Results.Ok, CustomResults.Problem));

        admin.MapPost("invitations/{id:guid}/revoke", async (Guid id, ICommandHandler<RevokeStaffInvitationCommand> handler, CancellationToken cancellationToken) =>
            (await handler.Handle(new RevokeStaffInvitationCommand(id), cancellationToken)).Match(Results.NoContent, CustomResults.Problem));

        admin.MapPost("{userId:guid}/reset-2fa", async (Guid userId, ICommandHandler<ResetStaffTwoFactorCommand> handler, CancellationToken cancellationToken) =>
            (await handler.Handle(new ResetStaffTwoFactorCommand(userId), cancellationToken)).Match(Results.NoContent, CustomResults.Problem));

        app.MapGet("staff-invitations/{token}", async (string token, IQueryHandler<GetStaffInvitationQuery, StaffInvitationPreviewDto> handler, CancellationToken cancellationToken) =>
            (await handler.Handle(new GetStaffInvitationQuery(token), cancellationToken)).Match(Results.Ok, CustomResults.Problem))
            .WithTags(Tags.Users);

        app.MapPost("staff-invitations/{token}/accept", async (
            string token, AcceptRequest request, ICommandHandler<AcceptStaffInvitationCommand, LoginResponse> handler, HttpContext httpContext, CancellationToken cancellationToken) =>
        {
            Result<LoginResponse> result = await handler.Handle(new AcceptStaffInvitationCommand(token, request.Password), cancellationToken);
            return result.IsFailure ? CustomResults.Problem(result) : SessionResult.Write(httpContext, result.Value);
        })
        .WithTags(Tags.Users);

        app.MapPost("users/2fa/setup", async (ChallengeRequest request, ICommandHandler<StartTwoFactorSetupCommand, TwoFactorSetupDto> handler, CancellationToken cancellationToken) =>
            (await handler.Handle(new StartTwoFactorSetupCommand(request.ChallengeToken), cancellationToken)).Match(Results.Ok, CustomResults.Problem))
            .WithTags(Tags.Users);

        app.MapPost("users/2fa/setup/confirm", async (
            CodeRequest request, ICommandHandler<ConfirmTwoFactorSetupCommand, TwoFactorEnabledDto> handler, HttpContext httpContext, CancellationToken cancellationToken) =>
        {
            Result<TwoFactorEnabledDto> result = await handler.Handle(new ConfirmTwoFactorSetupCommand(request.ChallengeToken, request.Code), cancellationToken);
            return result.IsFailure
                ? CustomResults.Problem(result)
                : SessionResult.Write(httpContext, result.Value.Session, new { recoveryCodes = result.Value.RecoveryCodes });
        })
        .WithTags(Tags.Users);

        app.MapPost("users/2fa/verify", async (
            CodeRequest request, ICommandHandler<VerifyTwoFactorCommand, LoginResponse> handler, HttpContext httpContext, CancellationToken cancellationToken) =>
        {
            Result<LoginResponse> result = await handler.Handle(new VerifyTwoFactorCommand(request.ChallengeToken, request.Code), cancellationToken);
            return result.IsFailure ? CustomResults.Problem(result) : SessionResult.Write(httpContext, result.Value);
        })
        .WithTags(Tags.Users);
    }
}
