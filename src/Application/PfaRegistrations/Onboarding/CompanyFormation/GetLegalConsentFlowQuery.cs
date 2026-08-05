using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.PfaRegistrations.CompanyFormation;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.PfaRegistrations.Onboarding.CompanyFormation;

public sealed record LegalConsentStepDto(
    string Key,
    string Title,
    string Subtitle,
    string Body,
    string CheckboxLabel);

public sealed record LegalConsentFlowDto(
    string Version,
    DateOnly EffectiveFrom,
    IReadOnlyList<LegalConsentStepDto> Steps);

/// <summary>
/// Textele wizardului de consimțământ, în versiunea activă. Nu trăiesc în frontend: juridicul
/// le va schimba, iar acordurile deja date rămân legate de versiunea afișată atunci.
/// </summary>
public sealed record GetLegalConsentFlowQuery(string Context) : IQuery<LegalConsentFlowDto>;

internal sealed class GetLegalConsentFlowQueryHandler(IApplicationDbContext context)
    : IQueryHandler<GetLegalConsentFlowQuery, LegalConsentFlowDto>
{
    public async Task<Result<LegalConsentFlowDto>> Handle(
        GetLegalConsentFlowQuery query,
        CancellationToken cancellationToken)
    {
        LegalConsentFlow? flow = await context.LegalConsentFlows
            .AsNoTracking()
            .Include(f => f.Steps)
            .Where(f => f.Context == query.Context && f.IsActive)
            .OrderByDescending(f => f.EffectiveFrom)
            .FirstOrDefaultAsync(cancellationToken);

        if (flow is null)
        {
            return Result.Failure<LegalConsentFlowDto>(CompanyFormationErrors.ConsentFlowNotFound);
        }

        return Result.Success(new LegalConsentFlowDto(
            flow.Version,
            flow.EffectiveFrom,
            flow.Steps
                .OrderBy(s => s.Position)
                .Select(s => new LegalConsentStepDto(s.Key, s.Title, s.Subtitle, s.Body, s.CheckboxLabel))
                .ToList()));
    }
}
