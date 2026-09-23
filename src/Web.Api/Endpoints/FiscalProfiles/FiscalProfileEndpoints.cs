using System.Globalization;
using Application.Abstractions.Messaging;
using Application.FiscalEstimates;
using Application.FiscalProfiles;
using Infrastructure.Authorization;
using SharedKernel;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.FiscalProfiles;

/// <summary>
/// Profilul fiscal anual al PFA-ului, din cele trei dashboarduri. Aceleași handlere; ruta spune
/// doar cine cheamă (PFA, admin, contabil), iar accesul la dosar îl verifică backendul.
/// Concurența: revizia profilului e ETag-ul, iar modificările o cer în <c>If-Match</c>.
/// </summary>
internal sealed class FiscalProfileEndpoints : IEndpoint
{
    public sealed record SaveAnswersRequest(FiscalProfileAnswers Answers);

    public sealed record CompleteRequest(FiscalProfileAnswers Answers, bool Confirmed);

    public sealed record StaffEditRequest(FiscalProfileAnswers Answers, string? Reason);

    public sealed record DataCorrectionRequest(int? Year, string? Fields, string Details);

    public sealed record ExistingReserveRequest(decimal? Amount);

    public sealed record PriorPeriodRequest(IReadOnlyList<PriorPeriodMonthInput>? Months);

    public sealed record UpdateTaskRequest(string? State, string? CallOutcome, DateTime? RescheduledToUtc, bool AssignToMe);

    private const string Tag = "Fiscal profiles";

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        MapPfa(app);
        MapStaff(app, "admin/pfas", FiscalProfileScope.Admin, Permissions.ManagePfaRegistrations);
        MapStaff(app, "accounting/pfas", FiscalProfileScope.Accounting, Permissions.ViewAssignedClients);
        MapTasks(app);
    }

    private static void MapPfa(IEndpointRouteBuilder app)
    {
        app.MapGet("pfa/me/fiscal-profiles/{year:int}", async (
            int year,
            HttpContext http,
            IQueryHandler<GetFiscalProfileQuery, FiscalProfileResponse> handler,
            CancellationToken cancellationToken) =>
            WithETag(http, await handler.Handle(new GetFiscalProfileQuery(FiscalProfileScope.Pfa, null, year), cancellationToken)))
        .RequireAuthorization()
        .HasPermission(Permissions.ViewOwnProfile)
        .WithTags(Tag);

        app.MapPatch("pfa/me/fiscal-profiles/{year:int}/draft", async (
            int year,
            SaveAnswersRequest request,
            HttpContext http,
            ICommandHandler<SaveFiscalProfileDraftCommand, FiscalProfileResponse> handler,
            CancellationToken cancellationToken) =>
            WithETag(http, await handler.Handle(
                new SaveFiscalProfileDraftCommand(year, request.Answers, IfMatch(http)), cancellationToken)))
        .RequireAuthorization()
        .HasPermission(Permissions.ViewOwnProfile)
        .WithTags(Tag);

        app.MapPost("pfa/me/fiscal-profiles/{year:int}/complete", async (
            int year,
            CompleteRequest request,
            HttpContext http,
            ICommandHandler<CompleteFiscalProfileCommand, FiscalProfileResponse> handler,
            CancellationToken cancellationToken) =>
            WithETag(http, await handler.Handle(
                new CompleteFiscalProfileCommand(year, request.Answers, request.Confirmed, IfMatch(http)), cancellationToken)))
        .RequireAuthorization()
        .HasPermission(Permissions.ViewOwnProfile)
        .WithTags(Tag);

        app.MapPatch("pfa/me/fiscal-profiles/{year:int}", async (
            int year,
            SaveAnswersRequest request,
            HttpContext http,
            ICommandHandler<EditFiscalProfileCommand, FiscalProfileResponse> handler,
            CancellationToken cancellationToken) =>
            WithETag(http, await handler.Handle(
                new EditFiscalProfileCommand(FiscalProfileScope.Pfa, null, year, request.Answers, null, IfMatch(http)),
                cancellationToken)))
        .RequireAuthorization()
        .HasPermission(Permissions.ViewOwnProfile)
        .WithTags(Tag);

        app.MapPost("pfa/me/fiscal-profiles/{year:int}/prompt-shown", async (
            int year,
            HttpContext http,
            ICommandHandler<MarkFiscalProfilePromptShownCommand, FiscalProfileResponse> handler,
            CancellationToken cancellationToken) =>
            WithETag(http, await handler.Handle(new MarkFiscalProfilePromptShownCommand(year), cancellationToken)))
        .RequireAuthorization()
        .HasPermission(Permissions.ViewOwnProfile)
        .WithTags(Tag);

        app.MapGet("pfa/me/fiscal-profiles/{year:int}/revisions", async (
            int year,
            IQueryHandler<GetFiscalProfileRevisionsQuery, IReadOnlyList<FiscalProfileRevisionResponse>> handler,
            CancellationToken cancellationToken) =>
        {
            Result<IReadOnlyList<FiscalProfileRevisionResponse>> result =
                await handler.Handle(new GetFiscalProfileRevisionsQuery(FiscalProfileScope.Pfa, null, year), cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .RequireAuthorization()
        .HasPermission(Permissions.ViewOwnProfile)
        .WithTags(Tag);

        app.MapGet("pfa/me/estimated-taxes/{year:int}", async (
            int year,
            IQueryHandler<GetEstimatedTaxesQuery, EstimatedTaxesResponse> handler,
            CancellationToken cancellationToken) =>
        {
            Result<EstimatedTaxesResponse> result =
                await handler.Handle(new GetEstimatedTaxesQuery(FiscalProfileScope.Pfa, null, year), cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .RequireAuthorization()
        .HasPermission(Permissions.ViewOwnProfile)
        .WithTags(Tag);

        app.MapPut("pfa/me/estimated-taxes/{year:int}/existing-reserve", async (
            int year,
            ExistingReserveRequest request,
            ICommandHandler<SetExistingReserveCommand, EstimatedTaxesResponse> handler,
            CancellationToken cancellationToken) =>
        {
            Result<EstimatedTaxesResponse> result =
                await handler.Handle(new SetExistingReserveCommand(year, request.Amount), cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .RequireAuthorization()
        .HasPermission(Permissions.ViewOwnProfile)
        .WithTags(Tag);

        // „Reîncearcă” după o rulare eșuată.
        app.MapPost("pfa/me/estimated-taxes/{year:int}/recalculate", async (
            int year,
            ICommandHandler<RequestEstimateRecalculationCommand, EstimatedTaxesResponse> handler,
            CancellationToken cancellationToken) =>
        {
            Result<EstimatedTaxesResponse> result =
                await handler.Handle(new RequestEstimateRecalculationCommand(FiscalProfileScope.Pfa, Guid.Empty, year), cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .RequireAuthorization()
        .HasPermission(Permissions.ViewOwnProfile)
        .WithTags(Tag);

        app.MapPost("pfa/me/data-corrections", async (
            DataCorrectionRequest request,
            ICommandHandler<CreateDataCorrectionCommand, DataCorrectionResponse> handler,
            CancellationToken cancellationToken) =>
        {
            int year = request.Year ?? DateTime.UtcNow.Year;
            Result<DataCorrectionResponse> result =
                await handler.Handle(new CreateDataCorrectionCommand(year, request.Fields, request.Details), cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .RequireAuthorization()
        .HasPermission(Permissions.ViewOwnProfile)
        .WithTags(Tag);
    }

    private static void MapStaff(IEndpointRouteBuilder app, string prefix, FiscalProfileScope scope, string permission)
    {
        app.MapGet($"{prefix}/{{pfaId:guid}}/fiscal-profiles/{{year:int}}", async (
            Guid pfaId,
            int year,
            HttpContext http,
            IQueryHandler<GetFiscalProfileQuery, FiscalProfileResponse> handler,
            CancellationToken cancellationToken) =>
            WithETag(http, await handler.Handle(new GetFiscalProfileQuery(scope, pfaId, year), cancellationToken)))
        .RequireAuthorization()
        .HasPermission(permission)
        .WithTags(Tag);

        app.MapPatch($"{prefix}/{{pfaId:guid}}/fiscal-profiles/{{year:int}}", async (
            Guid pfaId,
            int year,
            StaffEditRequest request,
            HttpContext http,
            ICommandHandler<EditFiscalProfileCommand, FiscalProfileResponse> handler,
            CancellationToken cancellationToken) =>
            WithETag(http, await handler.Handle(
                new EditFiscalProfileCommand(scope, pfaId, year, request.Answers, request.Reason, IfMatch(http)),
                cancellationToken)))
        .RequireAuthorization()
        .HasPermission(permission)
        .WithTags(Tag);

        app.MapGet($"{prefix}/{{pfaId:guid}}/fiscal-profiles/{{year:int}}/revisions", async (
            Guid pfaId,
            int year,
            IQueryHandler<GetFiscalProfileRevisionsQuery, IReadOnlyList<FiscalProfileRevisionResponse>> handler,
            CancellationToken cancellationToken) =>
        {
            Result<IReadOnlyList<FiscalProfileRevisionResponse>> result =
                await handler.Handle(new GetFiscalProfileRevisionsQuery(scope, pfaId, year), cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .RequireAuthorization()
        .HasPermission(permission)
        .WithTags(Tag);

        app.MapGet($"{prefix}/{{pfaId:guid}}/estimated-taxes/{{year:int}}", async (
            Guid pfaId,
            int year,
            IQueryHandler<GetEstimatedTaxesQuery, EstimatedTaxesResponse> handler,
            CancellationToken cancellationToken) =>
        {
            Result<EstimatedTaxesResponse> result = await handler.Handle(new GetEstimatedTaxesQuery(scope, pfaId, year), cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .RequireAuthorization()
        .HasPermission(permission)
        .WithTags(Tag);

        app.MapPost($"{prefix}/{{pfaId:guid}}/estimated-taxes/{{year:int}}/recalculate", async (
            Guid pfaId,
            int year,
            ICommandHandler<RequestEstimateRecalculationCommand, EstimatedTaxesResponse> handler,
            CancellationToken cancellationToken) =>
        {
            Result<EstimatedTaxesResponse> result =
                await handler.Handle(new RequestEstimateRecalculationCommand(scope, pfaId, year), cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .RequireAuthorization()
        .HasPermission(permission)
        .WithTags(Tag);

        app.MapGet($"{prefix}/{{pfaId:guid}}/prior-period/{{year:int}}", async (
            Guid pfaId,
            int year,
            IQueryHandler<GetPriorPeriodQuery, PriorPeriodResponse> handler,
            CancellationToken cancellationToken) =>
        {
            Result<PriorPeriodResponse> result = await handler.Handle(new GetPriorPeriodQuery(scope, pfaId, year), cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .RequireAuthorization()
        .HasPermission(permission)
        .WithTags(Tag);

        app.MapPut($"{prefix}/{{pfaId:guid}}/prior-period/{{year:int}}", async (
            Guid pfaId,
            int year,
            PriorPeriodRequest request,
            ICommandHandler<SavePriorPeriodCommand, PriorPeriodResponse> handler,
            CancellationToken cancellationToken) =>
        {
            Result<PriorPeriodResponse> result =
                await handler.Handle(new SavePriorPeriodCommand(scope, pfaId, year, request.Months ?? []), cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .RequireAuthorization()
        .HasPermission(permission)
        .WithTags(Tag);

        string correctionsPrefix = scope == FiscalProfileScope.Admin ? "admin" : "accounting";
        app.MapPost($"{correctionsPrefix}/data-corrections/{{id:guid}}/resolve", async (
            Guid id,
            ICommandHandler<ResolveDataCorrectionCommand, DataCorrectionResponse> handler,
            CancellationToken cancellationToken) =>
        {
            Result<DataCorrectionResponse> result = await handler.Handle(new ResolveDataCorrectionCommand(scope, id), cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .RequireAuthorization()
        .HasPermission(permission)
        .WithTags(Tag);
    }

    private static void MapTasks(IEndpointRouteBuilder app)
    {
        app.MapGet("admin/tasks", async (
            string? type,
            bool? includeClosed,
            IQueryHandler<GetAdminCallTasksQuery, IReadOnlyList<AdminCallTaskResponse>> handler,
            CancellationToken cancellationToken) =>
        {
            Result<IReadOnlyList<AdminCallTaskResponse>> result =
                await handler.Handle(new GetAdminCallTasksQuery(type, includeClosed ?? false), cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .RequireAuthorization()
        .HasPermission(Permissions.ManagePfaRegistrations)
        .WithTags(Tag);

        app.MapPatch("admin/tasks/{taskId:guid}", async (
            Guid taskId,
            UpdateTaskRequest request,
            ICommandHandler<UpdateAdminCallTaskCommand, AdminCallTaskResponse> handler,
            CancellationToken cancellationToken) =>
        {
            Result<AdminCallTaskResponse> result = await handler.Handle(
                new UpdateAdminCallTaskCommand(taskId, request.State, request.CallOutcome, request.RescheduledToUtc, request.AssignToMe),
                cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .RequireAuthorization()
        .HasPermission(Permissions.ManagePfaRegistrations)
        .WithTags(Tag);
    }

    /// <summary>Revizia din <c>If-Match</c>: <c>"3"</c>, <c>W/"3"</c> sau <c>3</c>.</summary>
    private static int? IfMatch(HttpContext http)
    {
        string? raw = http.Request.Headers.IfMatch.ToString();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        string value = raw.Trim();
        if (value.StartsWith("W/", StringComparison.Ordinal))
        {
            value = value[2..];
        }

        return int.TryParse(value.Trim('"'), NumberStyles.None, CultureInfo.InvariantCulture, out int revision)
            ? revision
            : -1;
    }

    private static IResult WithETag(HttpContext http, Result<FiscalProfileResponse> result)
    {
        if (result.IsSuccess)
        {
            http.Response.Headers.ETag = $"\"{result.Value.Revision.ToString(CultureInfo.InvariantCulture)}\"";
        }

        return result.Match(Results.Ok, CustomResults.Problem);
    }
}
