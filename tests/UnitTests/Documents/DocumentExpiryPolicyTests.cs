using Application.Documents.Expiry;
using Application.Documents.Registry;
using Domain.Documents;
using Shouldly;
using Xunit;

namespace UnitTests.Documents;

/// <summary>
/// „Expiră în X zile" se calculează pe server, în fusul României — frontendul primește starea,
/// nu o deduce. Ziua de referință e fixată în teste, ca rezultatul să nu depindă de ceasul
/// mașinii pe care rulează.
/// </summary>
public sealed class DocumentExpiryPolicyTests
{
    private static readonly DateOnly Today = new(2026, 8, 15);

    private static DocumentExpiry Evaluate(DateOnly? expiresOn, DocumentCategory category = DocumentCategory.RCA) =>
        DocumentExpiryPolicy.Evaluate(
            category,
            expiresOn?.ToDateTime(TimeOnly.MinValue),
            Today);

    [Fact]
    public void A_document_far_from_expiry_is_valid()
    {
        DocumentExpiry result = Evaluate(new DateOnly(2026, 12, 1));

        result.State.ShouldBe(DocumentExpiryState.Valid);
        result.DaysUntilExpiry.ShouldBe(108);
    }

    [Fact]
    public void Inside_the_threshold_it_expires_soon()
    {
        // Exemplul din spec: RCA valabil până la 14.09.2026 → „Expiră în 30 zile".
        DocumentExpiry result = Evaluate(new DateOnly(2026, 9, 14));

        result.State.ShouldBe(DocumentExpiryState.ExpiringSoon);
        result.DaysUntilExpiry.ShouldBe(30);
    }

    [Fact]
    public void The_threshold_day_itself_still_counts_as_expiring_soon()
    {
        Evaluate(Today.AddDays(DocumentExpiryPolicy.ExpiringSoonDays)).State
            .ShouldBe(DocumentExpiryState.ExpiringSoon);
        Evaluate(Today.AddDays(DocumentExpiryPolicy.ExpiringSoonDays + 1)).State
            .ShouldBe(DocumentExpiryState.Valid);
    }

    [Fact]
    public void The_expiry_day_is_still_a_valid_day()
    {
        // Un RCA care expiră azi acoperă ziua de azi; abia mâine e expirat.
        DocumentExpiry today = Evaluate(Today);

        today.State.ShouldBe(DocumentExpiryState.ExpiringSoon);
        today.DaysUntilExpiry.ShouldBe(0);
    }

    [Fact]
    public void Past_the_expiry_date_it_is_expired_with_a_negative_countdown()
    {
        DocumentExpiry result = Evaluate(Today.AddDays(-3));

        result.State.ShouldBe(DocumentExpiryState.Expired);
        result.DaysUntilExpiry.ShouldBe(-3);
    }

    [Fact]
    public void Categories_that_do_not_expire_are_never_evaluated()
    {
        // Certificatul de înregistrare nu expiră; o dată pe el e data eliberării.
        DocumentExpiry result = Evaluate(new DateOnly(2020, 1, 1), DocumentCategory.CertificatInregistrare);

        result.State.ShouldBe(DocumentExpiryState.NotApplicable);
        result.DaysUntilExpiry.ShouldBeNull();
    }

    [Fact]
    public void An_expirable_document_without_a_date_is_not_reported_as_expired()
    {
        DocumentExpiry result = Evaluate(expiresOn: null);

        result.State.ShouldBe(DocumentExpiryState.NotApplicable);
    }

    [Fact]
    public void Every_registry_type_marked_as_expiring_has_an_expirable_category()
    {
        // Prinde desincronizarea dintre catalog și politică: un tip afișat cu dată de expirare,
        // dar a cărui categorie nu e în lista celor expirabile, n-ar primi niciodată status.
        foreach (DocumentTypeDef def in DocumentRegistry.All.Where(d => d.HasExpiryDate))
        {
            DocumentExpiryPolicy.Expires(def.PrimaryCategory)
                .ShouldBeTrue($"{def.Key} e marcat cu dată de expirare, dar categoria lui nu expiră");
        }
    }

    [Fact]
    public void Registry_keys_are_unique()
    {
        DocumentRegistry.All.Select(d => d.Key).Distinct().Count()
            .ShouldBe(DocumentRegistry.All.Count);
    }

    [Fact]
    public void Each_group_from_the_spec_has_its_documents()
    {
        DocumentRegistry.ForGroup(DocumentGroup.Personal).Count.ShouldBe(6);
        DocumentRegistry.ForGroup(DocumentGroup.Pfa).Count.ShouldBe(3);
        DocumentRegistry.ForGroup(DocumentGroup.Vehicle).Count.ShouldBe(8);
    }
}
