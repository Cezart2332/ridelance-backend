using Application.Abstractions.Data;
using Application.Abstractions.Security;
using Application.Documents.ExtractedFields;
using Domain.Documents;
using Domain.PfaRegistrations;
using Domain.PfaRegistrations.CompanyFormation;
using Microsoft.EntityFrameworkCore;

namespace Application.PfaRegistrations.Onboarding.CompanyFormation;

/// <summary>
/// Reia în dosarul de înființare datele deja citite din buletin.
///
/// De ce e nevoie de o reluare, în loc să fie de ajuns aplicarea de la OCR:
///
/// Buletinul se încarcă la pasul de <b>eligibilitate</b>, adică ÎNAINTE ca dosarul PFA să existe
/// — el se creează abia la pasul următor, când utilizatorul răspunde „Nu am PFA". În momentul în
/// care OCR-ul termină de citit, <c>ExtractedFieldApplier</c> caută un <see cref="PfaRegistration"/>,
/// nu găsește niciunul și se oprește. Valorile rămân salvate în <see cref="ExtractedField"/>, dar
/// nimeni nu le mai aplică vreodată — de aici „datele din buletin nu se precompletează".
///
/// Serviciul ăsta le replaya prin exact același applier, deci toate garanțiile rămân în picioare:
/// nu se scrie peste un câmp editat manual, nu se atinge un dosar semnat, iar sediul social se
/// oglindește din domiciliu ca înainte.
/// </summary>
public sealed class CompanyFormationPrefillService(
    IApplicationDbContext context,
    IExtractedFieldApplier applier,
    ISecretProtector secretProtector)
{
    /// <summary>Documentele din care se poate citi identitatea solicitantului.</summary>
    private static readonly DocumentCategory[] IdentityCategories =
    [
        DocumentCategory.CarteIdentitate,
        DocumentCategory.Buletin,
    ];

    /// <summary>
    /// Umple dosarul din buletinul deja încărcat. Idempotent: câmpurile atinse de utilizator sunt
    /// marcate „editate manual" și nu se rescriu, deci se poate apela la fiecare deschidere a
    /// paginii fără să strice nimic.
    /// </summary>
    /// <returns><see langword="true"/> dacă s-a scris ceva și starea trebuie recitită.</returns>
    public async Task<bool> BackfillFromIdentityDocumentAsync(Guid userId, CancellationToken cancellationToken)
    {
        PfaRegistration? registration = await context.PfaRegistrations
            .AsNoTracking()
            .Where(r => r.UserId == userId)
            .OrderByDescending(r => r.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        // Dosarul de înființare există doar pe ramura „Nu am PFA".
        if (registration?.RegistrationType != RegistrationType.NuAmPfa)
        {
            return false;
        }

        CompanyFormationRequest? request = await context.CompanyFormationRequests
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.PfaRegistrationId == registration.Id, cancellationToken);

        // Dosar semnat: datele sunt înghețate, iar semnătura e legată de ele.
        if (request?.IsLocked == true)
        {
            return false;
        }

        // Deja completat de om — nu mai are ce reface, iar o scriere pe fiecare încărcare de
        // pagină ar fi trafic degeaba.
        if (request?.PersonalDataComplete == true)
        {
            return false;
        }

        Document? identity = await context.Documents
            .AsNoTracking()
            .Where(d => d.UserId == userId
                && d.Status != DocumentStatus.Rejected
                && IdentityCategories.Contains(d.Category))
            .OrderByDescending(d => d.UploadedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (identity is null)
        {
            return false;
        }

        List<ExtractedField> fields = await context.ExtractedFields
            .AsNoTracking()
            .Where(f => f.DocumentId == identity.Id)
            .ToListAsync(cancellationToken);

        if (fields.Count == 0)
        {
            return false;
        }

        bool applied = false;

        foreach (ExtractedField field in fields)
        {
            // Valoarea confirmată de un om bate întotdeauna ce a citit modelul; pentru câmpurile
            // sensibile (CNP) valoarea reală e criptată, nu în coloana de afișare.
            string? value = SensitiveFieldProtection.Reveal(field, secretProtector);

            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            await applier.ApplyAsync(identity, field.FieldKey, value, cancellationToken);
            applied = true;
        }

        if (applied)
        {
            await context.SaveChangesAsync(cancellationToken);
        }

        return applied;
    }
}
