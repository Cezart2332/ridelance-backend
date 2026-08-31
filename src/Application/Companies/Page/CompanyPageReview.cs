using Domain.Companies;

namespace Application.Companies.Page;

/// <summary>
/// Trecerile mini-site-ului între ciornă, coadă de verificare și public.
/// </summary>
/// <remarks>
/// Toate într-un singur loc, fiindcă sunt aceleași reguli oriunde s-ar atinge pagina: salvarea din
/// editor, încărcarea unei fotografii de cover, verdictul din administrare. Împrăștiate prin
/// handlere, s-ar fi desincronizat la prima cale nouă care modifică pagina — și exact aia ar fi
/// fost calea prin care textul ajunge public nevăzut de nimeni.
/// </remarks>
internal static class CompanyPageReview
{
    /// <summary>
    /// Are pagina ceva scris sau încărcat de proprietar?
    /// </summary>
    /// <remarks>
    /// Culorile nu contează aici. O paletă schimbată nu e conținut de citit, iar o pagină goală cu
    /// alt accent n-are ce căuta în coada de verificare — cine o deschide n-ar avea ce aproba.
    /// </remarks>
    public static bool HasReviewableContent(CompanyProfile profile) =>
        !string.IsNullOrWhiteSpace(profile.Tagline) ||
        !string.IsNullOrWhiteSpace(profile.PublicDescription) ||
        !string.IsNullOrWhiteSpace(profile.CoverImageUrl) ||
        !string.IsNullOrWhiteSpace(profile.PickupAddress) ||
        !string.IsNullOrWhiteSpace(profile.PickupNote) ||
        profile.PageContent.Highlights.Count > 0 ||
        profile.PageContent.Schedule.Count > 0 ||
        profile.PageContent.CoverageAreas.Count > 0 ||
        !string.IsNullOrWhiteSpace(profile.PageContent.CoverageNote) ||
        profile.PageContent.Faq.Count > 0;

    /// <summary>Există o versiune aprobată, deci pagina publică are ce arăta.</summary>
    public static bool IsLive(CompanyProfile profile) => profile.PublishedPage.ApprovedAtUtc.HasValue;

    /// <summary>
    /// Proprietarul tocmai a modificat pagina: ciorna intră (din nou) la verificare.
    /// </summary>
    /// <remarks>
    /// Versiunea deja aprobată **nu** se atinge. Cât timp ciorna nouă își așteaptă rândul, publicul
    /// vede în continuare ce am aprobat data trecută — altfel fiecare corectură de virgulă ar fi
    /// scos pagina de pe internet pentru câteva ore.
    ///
    /// Secțiunile blocate rămân blocate: le-a oprit administrarea, nu proprietarul, deci nu se
    /// deblochează printr-o salvare.
    /// </remarks>
    public static void SubmitForReview(CompanyProfile profile)
    {
        CompanyPageModeration moderation = profile.PageModeration;

        if (!HasReviewableContent(profile))
        {
            // O pagină golită n-are ce fi verificată. Cade înapoi în ciornă, iar dacă exista o
            // versiune publicată, golirea e chiar cererea de a o retrage.
            moderation.Status = CompanyPageReviewStatus.Draft;
            moderation.Note = null;
            moderation.SubmittedAtUtc = null;
            profile.PublishedPage = new CompanyPagePublication();
            return;
        }

        moderation.Status = CompanyPageReviewStatus.Pending;
        moderation.SubmittedAtUtc = DateTime.UtcNow;

        // Motivul refuzului de data trecută se referea la textul de dinainte. Păstrat, ar fi apărut
        // lângă o pagină pe care proprietarul chiar a corectat-o.
        moderation.Note = null;
    }

    /// <summary>Aprobă ciorna: copia ei devine ce vede publicul.</summary>
    public static void Approve(
        CompanyProfile profile,
        Guid reviewerId,
        string? note,
        IEnumerable<string>? blockedSections)
    {
        profile.PageModeration = new CompanyPageModeration
        {
            Status = CompanyPageReviewStatus.Approved,
            BlockedSections = NormalizeSections(blockedSections ?? profile.PageModeration.BlockedSections),
            Note = note,
            SubmittedAtUtc = profile.PageModeration.SubmittedAtUtc,
            ReviewedAtUtc = DateTime.UtcNow,
            ReviewedByUserId = reviewerId,
        };

        profile.PublishedPage = Snapshot(profile);
    }

    /// <summary>
    /// Refuză pagina și o scoate de pe internet.
    /// </summary>
    /// <remarks>
    /// Copia publicată se golește, nu doar se marchează: un refuz care ar fi lăsat versiunea veche
    /// live ar fi însemnat că nu putem opri nimic din ce am aprobat cândva din greșeală.
    /// </remarks>
    public static void Reject(CompanyProfile profile, Guid reviewerId, string? note)
    {
        profile.PageModeration = new CompanyPageModeration
        {
            Status = CompanyPageReviewStatus.Rejected,
            BlockedSections = profile.PageModeration.BlockedSections,
            Note = note,
            SubmittedAtUtc = profile.PageModeration.SubmittedAtUtc,
            ReviewedAtUtc = DateTime.UtcNow,
            ReviewedByUserId = reviewerId,
        };

        profile.PublishedPage = new CompanyPagePublication();
    }

    /// <summary>
    /// Schimbă doar secțiunile blocate, fără să atingă verdictul.
    /// </summary>
    /// <remarks>
    /// Blocarea e o unealtă separată de aprobare: o pagină bună poate avea o singură secțiune
    /// problematică, iar refuzul întreg pentru ea ar fi o pedeapsă disproporționată.
    /// </remarks>
    public static void SetBlockedSections(
        CompanyProfile profile,
        Guid reviewerId,
        IEnumerable<string>? sections,
        string? note)
    {
        profile.PageModeration.BlockedSections = NormalizeSections(sections);
        profile.PageModeration.Note = note;
        profile.PageModeration.ReviewedAtUtc = DateTime.UtcNow;
        profile.PageModeration.ReviewedByUserId = reviewerId;
    }

    /// <summary>
    /// Copia aprobată: exact câmpurile pe care proprietarul le scrie sau le încarcă liber.
    /// </summary>
    /// <remarks>
    /// Culorile și secțiunile se copiază element cu element, nu prin referință. Împărțind aceleași
    /// obiecte, o editare ulterioară a ciornei ar fi rescris pe tăcute și versiunea aprobată — adică
    /// exact ocolirea pe care verificarea trebuie s-o închidă.
    /// </remarks>
    private static CompanyPagePublication Snapshot(CompanyProfile profile) => new()
    {
        ApprovedAtUtc = DateTime.UtcNow,
        Tagline = profile.Tagline,
        PublicDescription = profile.PublicDescription,
        CoverImageUrl = profile.CoverImageUrl,
        Theme = new CompanyPageTheme
        {
            Accent = profile.PageTheme.Accent,
            Background = profile.PageTheme.Background,
            Surface = profile.PageTheme.Surface,
            Text = profile.PageTheme.Text,
            ButtonText = profile.PageTheme.ButtonText,
            HeroOverlay = profile.PageTheme.HeroOverlay,
            HeroOverlayOpacity = profile.PageTheme.HeroOverlayOpacity,
        },
        Content = new CompanyPageContent
        {
            Highlights = profile.PageContent.Highlights
                .Select(h => new CompanyPageHighlight { IconKey = h.IconKey, Title = h.Title, Text = h.Text })
                .ToList(),
            Schedule = profile.PageContent.Schedule
                .Select(r => new CompanyPageScheduleRow { Day = r.Day, Hours = r.Hours })
                .ToList(),
            CoverageAreas = [.. profile.PageContent.CoverageAreas],
            CoverageNote = profile.PageContent.CoverageNote,
            Faq = profile.PageContent.Faq
                .Select(f => new CompanyPageFaq { Question = f.Question, Answer = f.Answer })
                .ToList(),
        },
        PickupAddress = profile.PickupAddress,
        PickupLatitude = profile.PickupLatitude,
        PickupLongitude = profile.PickupLongitude,
        PickupNote = profile.PickupNote,
    };

    /// <summary>
    /// Păstrează doar id-urile de secțiune pe care le cunoaștem.
    /// </summary>
    /// <remarks>
    /// Un id necunoscut nu e o eroare de raportat: n-ar bloca nimic, fiindcă nicio secțiune nu se
    /// numește așa. Se scoate tăcut, ca lista salvată să nu adune gunoi de la o versiune de client
    /// mai veche.
    /// </remarks>
    private static List<string> NormalizeSections(IEnumerable<string>? sections) =>
        (sections ?? [])
            .Where(CompanyPageSections.Blockable.Contains)
            .Distinct(StringComparer.Ordinal)
            .ToList();
}
