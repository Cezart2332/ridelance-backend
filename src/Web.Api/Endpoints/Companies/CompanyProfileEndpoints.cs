using Application.Abstractions.Messaging;
using Application.Abstractions.Services;
using Application.Companies;
using Application.Companies.Commands.CompanySignature;
using Application.Companies.Commands.UpdateCompanyProfile;
using Application.Companies.Commands.UploadCompanyLogo;
using Application.Companies.Queries.GetCompanyProfile;
using SharedKernel;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Companies;

/// <summary>
/// Profilul firmei contului curent (spec §7.2).
///
/// Rutele nu poartă id de proprietar: fiecare handler citește contul din <c>IUserContext</c>,
/// deci un cont nu poate ajunge la profilul altuia nici măcar prin URL greșit.
/// </summary>
internal sealed class GetCompanyProfile : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("companies/profile", async (
            IQueryHandler<GetCompanyProfileQuery, CompanyProfileDto?> handler,
            CancellationToken cancellationToken) =>
        {
            Result<CompanyProfileDto?> result = await handler.Handle(new GetCompanyProfileQuery(), cancellationToken);

            // Profil inexistent = 204, nu 404: contul e valid, doar nu și-a completat încă datele.
            if (result.IsFailure)
            {
                return CustomResults.Problem(result);
            }

            return result.Value is null ? Results.NoContent() : Results.Ok(result.Value);
        })
        .RequireAuthorization()
        .WithTags(Tags.Companies);
    }
}

internal sealed class UpdateCompanyProfile : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPut("companies/profile", async (
            UpdateCompanyProfileCommand command,
            ICommandHandler<UpdateCompanyProfileCommand, CompanyProfileDto> handler,
            CancellationToken cancellationToken) =>
        {
            Result<CompanyProfileDto> result = await handler.Handle(command, cancellationToken);
            return result.IsFailure ? CustomResults.Problem(result) : Results.Ok(result.Value);
        })
        .RequireAuthorization()
        .WithTags(Tags.Companies);
    }
}

internal sealed class UploadCompanyLogo : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("companies/profile/logo", async (
            IFormFile file,
            ICommandHandler<UploadCompanyLogoCommand, string> handler,
            CancellationToken cancellationToken) =>
        {
            using Stream stream = file.OpenReadStream();

            var command = new UploadCompanyLogoCommand(file.FileName, stream, file.ContentType);
            Result<string> result = await handler.Handle(command, cancellationToken);

            return result.IsFailure ? CustomResults.Problem(result) : Results.Ok(new { logoUrl = result.Value });
        })
        .RequireAuthorization()
        .DisableAntiforgery()
        .WithTags(Tags.Companies);
    }
}

/// <summary>
/// Datele firmei din registrul ANAF, pentru completarea profilului la prima configurare.
/// </summary>
/// <remarks>
/// Aceleași date pe care le folosește deja facturarea (<c>invoices/company/{cui}</c>), dar ruta
/// stă lângă profil, fiindcă de aici o cheamă flota — n-are sens ca setarea firmei să treacă
/// printr-un endpoint de facturi ca să afle cum o cheamă.
///
/// Cere autentificare, deși registrul e public: altfel ar fi un proxy deschis peste ANAF, pe
/// socoteala noastră.
/// </remarks>
internal sealed class LookupCompanyForProfile : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("companies/lookup/{cui}", async (
            string cui,
            ICompanyLookupService lookup,
            CancellationToken cancellationToken) =>
        {
            CompanyLookupResult? company = await lookup.FindByCuiAsync(cui, cancellationToken);
            return company is null ? Results.NotFound() : Results.Ok(company);
        })
        .RequireAuthorization()
        .WithTags(Tags.Companies);
    }
}

/// <summary>
/// Specimenul de semnătură al firmei, cel care se tipărește pe documentele generate.
/// </summary>
/// <remarks>
/// Se trimite ca imagine în corpul cererii, nu ca fișier încărcat: vine dintr-o pânză de desenat,
/// nu de pe discul cuiva. Citirea lui înapoi se face prin <c>documents/{id}/download</c>, ca la
/// orice document criptat — nu are endpoint propriu, ca să nu existe două căi către același fișier.
/// </remarks>
internal sealed class CompanySignature : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPut("companies/profile/signature", async (
            SaveCompanySignatureRequest request,
            ICommandHandler<SaveCompanySignatureCommand, Guid> handler,
            CancellationToken cancellationToken) =>
        {
            Result<Guid> result = await handler.Handle(
                new SaveCompanySignatureCommand(request.SignatureImage), cancellationToken);

            return result.IsFailure
                ? CustomResults.Problem(result)
                : Results.Ok(new { signatureDocumentId = result.Value });
        })
        .RequireAuthorization()
        .WithTags(Tags.Companies);

        app.MapDelete("companies/profile/signature", async (
            ICommandHandler<DeleteCompanySignatureCommand> handler,
            CancellationToken cancellationToken) =>
        {
            Result result = await handler.Handle(new DeleteCompanySignatureCommand(), cancellationToken);

            return result.IsFailure ? CustomResults.Problem(result) : Results.NoContent();
        })
        .RequireAuthorization()
        .WithTags(Tags.Companies);
    }
}

public sealed record SaveCompanySignatureRequest(string SignatureImage);
