using Application.Companies.Page;
using Domain.Companies;
using Shouldly;
using Xunit;

namespace UnitTests.Companies;

/// <summary>
/// Drumul mini-site-ului de la ciornă la public.
/// </summary>
/// <remarks>
/// Regula pe care o apără toate testele de aici e una singură: nimic din ce scrie o firmă nu
/// ajunge pe internet fără o aprobare. A doua, la fel de importantă, e că verificarea nu are voie
/// să scoată din aer o pagină deja aprobată doar fiindcă proprietarul a mai tastat ceva.
/// </remarks>
public sealed class CompanyPageReviewTests
{
    private static readonly Guid Reviewer = Guid.NewGuid();

    private static CompanyProfile Profile() => new()
    {
        LegalName = "Tuki Go SRL",
        PublicDescription = "Închiriem mașini pentru ridesharing.",
    };

    [Fact]
    public void Salvarea_Proprietarului_Trimite_Pagina_La_Verificare()
    {
        CompanyProfile profile = Profile();

        CompanyPageReview.SubmitForReview(profile);

        profile.PageModeration.Status.ShouldBe(CompanyPageReviewStatus.Pending);
        profile.PageModeration.SubmittedAtUtc.ShouldNotBeNull();
        CompanyPageReview.IsLive(profile).ShouldBeFalse();
    }

    [Fact]
    public void O_Pagina_Fara_Continut_Nu_Intra_In_Coada()
    {
        // O paletă schimbată pe o pagină goală n-are ce fi aprobat — cine o deschide n-ar avea ce citi.
        var profile = new CompanyProfile { LegalName = "Tuki Go SRL" };
        profile.PageTheme.Accent = "#123456";

        CompanyPageReview.SubmitForReview(profile);

        profile.PageModeration.Status.ShouldBe(CompanyPageReviewStatus.Draft);
    }

    [Fact]
    public void Aprobarea_Publica_Ciorna()
    {
        CompanyProfile profile = Profile();
        CompanyPageReview.SubmitForReview(profile);

        CompanyPageReview.Approve(profile, Reviewer, note: null, blockedSections: null);

        profile.PageModeration.Status.ShouldBe(CompanyPageReviewStatus.Approved);
        profile.PageModeration.ReviewedByUserId.ShouldBe(Reviewer);
        CompanyPageReview.IsLive(profile).ShouldBeTrue();
        profile.PublishedPage.PublicDescription.ShouldBe("Închiriem mașini pentru ridesharing.");
    }

    [Fact]
    public void Editarea_De_Dupa_Aprobare_Nu_Atinge_Versiunea_Publicata()
    {
        // Miezul modelului: ciorna se schimbă, publicul vede în continuare ce am aprobat.
        CompanyProfile profile = Profile();
        CompanyPageReview.Approve(profile, Reviewer, note: null, blockedSections: null);

        profile.PublicDescription = "Text nou, neverificat.";
        profile.PageContent.Faq.Add(new CompanyPageFaq { Question = "Î?", Answer = "R." });
        CompanyPageReview.SubmitForReview(profile);

        profile.PageModeration.Status.ShouldBe(CompanyPageReviewStatus.Pending);
        CompanyPageReview.IsLive(profile).ShouldBeTrue();
        profile.PublishedPage.PublicDescription.ShouldBe("Închiriem mașini pentru ridesharing.");
        profile.PublishedPage.Content.Faq.ShouldBeEmpty();
    }

    [Fact]
    public void Refuzul_Scoate_Pagina_De_Pe_Internet()
    {
        CompanyProfile profile = Profile();
        CompanyPageReview.Approve(profile, Reviewer, note: null, blockedSections: null);

        CompanyPageReview.Reject(profile, Reviewer, "Textul conține reclamă la alt serviciu.");

        profile.PageModeration.Status.ShouldBe(CompanyPageReviewStatus.Rejected);
        profile.PageModeration.Note.ShouldBe("Textul conține reclamă la alt serviciu.");
        CompanyPageReview.IsLive(profile).ShouldBeFalse();
    }

    [Fact]
    public void Golirea_Paginii_Retrage_Versiunea_Publicata()
    {
        CompanyProfile profile = Profile();
        CompanyPageReview.Approve(profile, Reviewer, note: null, blockedSections: null);

        profile.PublicDescription = null;
        CompanyPageReview.SubmitForReview(profile);

        profile.PageModeration.Status.ShouldBe(CompanyPageReviewStatus.Draft);
        CompanyPageReview.IsLive(profile).ShouldBeFalse();
    }

    [Fact]
    public void Sectiunile_Blocate_Supravietuiesc_Salvarii_Proprietarului()
    {
        // Le-a oprit administrarea, nu proprietarul. O salvare nu e o cale de a le porni înapoi.
        CompanyProfile profile = Profile();
        CompanyPageReview.SetBlockedSections(profile, Reviewer, new[] { CompanyPageSections.About }, note: null);

        CompanyPageReview.SubmitForReview(profile);

        profile.PageModeration.BlockedSections.ShouldBe(new[] { CompanyPageSections.About });
    }

    [Fact]
    public void Aprobarea_Pastreaza_Blocajele_Cand_Nu_Se_Trimite_O_Lista_Noua()
    {
        CompanyProfile profile = Profile();
        CompanyPageReview.SetBlockedSections(profile, Reviewer, new[] { CompanyPageSections.Faq }, note: null);

        CompanyPageReview.Approve(profile, Reviewer, note: null, blockedSections: null);

        profile.PageModeration.BlockedSections.ShouldBe(new[] { CompanyPageSections.Faq });
    }

    [Fact]
    public void Id_urile_De_Sectiune_Necunoscute_Se_Ignora()
    {
        // N-ar bloca nimic, fiindcă nicio secțiune nu se numește așa. Se scot tăcut, ca lista
        // salvată să nu adune gunoi de la un client mai vechi.
        CompanyProfile profile = Profile();

        CompanyPageReview.SetBlockedSections(
            profile, Reviewer, new[] { "flota", "contact", CompanyPageSections.Location }, note: null);

        profile.PageModeration.BlockedSections.ShouldBe(new[] { CompanyPageSections.Location });
    }

    [Fact]
    public void Motivul_Refuzului_Nu_Ramane_Lipit_De_Pagina_Corectata()
    {
        CompanyProfile profile = Profile();
        CompanyPageReview.Reject(profile, Reviewer, "Prea puțin text.");

        profile.PublicDescription = "O descriere completă a flotei și a condițiilor de închiriere.";
        CompanyPageReview.SubmitForReview(profile);

        profile.PageModeration.Note.ShouldBeNull();
    }
}
