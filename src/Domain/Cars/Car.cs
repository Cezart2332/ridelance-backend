using SharedKernel;

namespace Domain.Cars;

public sealed class Car : Entity
{
    public Guid Id { get; set; }
    public string Brand { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public int Year { get; set; }

    /// <summary>Identitatea din URL-ul public (<see cref="CarSlug"/>). Unică pe tot tabelul.</summary>
    public string Slug { get; set; } = string.Empty;

    // Specs
    public string Engine { get; set; } = string.Empty;       // Electric, Hybrid, GPL, Benzină, Diesel
    public string Transmission { get; set; } = string.Empty; // Automată, Manuală
    public string? Color { get; set; }
    public int? Seats { get; set; }

    // Location – stored as comma-separated or JSON string
    public string Location { get; set; } = string.Empty;

    /// <summary>Cartierul sau zona din oraș. Se afișează public când pinul exact e ascuns.</summary>
    public string? Zone { get; set; }

    /// <summary>
    /// Pinul de preluare. Ambele sau niciunul — o latitudine fără longitudine nu e o locație.
    /// </summary>
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }

    /// <summary>
    /// Dacă publicul vede pinul exact sau doar zona. Implicit ascuns: adresa unde stă mașina
    /// noaptea nu e o informație pe care proprietarul o dă fără să fie întrebat.
    /// </summary>
    public bool ShowExactLocation { get; set; }

    /// <summary>Anunțul folosește datele de contact ale firmei, filtrate de setările ei.</summary>
    public bool UseCompanyContacts { get; set; } = true;

    // Pricing
    public decimal PricePerWeek { get; set; }
    public decimal? OldPrice { get; set; }
    public bool DiscountActive { get; set; }
    public decimal? Garantie { get; set; }

    // Classification
    public CarOfferType OfferType { get; set; } = CarOfferType.Weekly;
    public CarStatus Status { get; set; } = CarStatus.Available;

    /// <summary>Perioada minimă de închiriere, ca text: „2 luni", „Fără perioadă minimă".</summary>
    public string? MinimumPeriod { get; set; }

    /// <summary>Condițiile principale, afișate pe anunț sub preț.</summary>
    public string? Conditions { get; set; }

    /// <summary>De când devine disponibilă, când nu e disponibilă acum.</summary>
    public DateTime? AvailableFromUtc { get; set; }

    // Platform categories (stored as JSON arrays)
    public List<string> UberCategories { get; set; } = [];
    public List<string> BoltCategories { get; set; } = [];
    public List<string> Badges { get; set; } = [];

    // Content
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Starea anunțului public. A luat locul lui <c>Active</c>: un boolean nu putea deosebi
    /// „încă nepublicat" de „retras temporar" de „scos definitiv", iar toate trei ajungeau `false`.
    /// </summary>
    public ListingStatus ListingStatus { get; set; } = ListingStatus.Draft;

    /// <summary>
    /// Se vede anunțul în marketplace?
    ///
    /// Derivat din cele trei condiții, nu stocat. Ca boolean scris în baza de date, răspunsul ăsta
    /// trebuia recalculat de mână în unsprezece locuri — la aprobare, la respingere, la creare, la
    /// editare, la fiecare eveniment Stripe — și oricare uitat lăsa un anunț plătit invizibil sau
    /// unul neplătit pe piață. `ListingStatus` spune ce vrea proprietarul; celelalte două spun dacă
    /// are voie.
    /// </summary>
    public bool Active =>
        ListingStatus == ListingStatus.Published
        && ApprovalStatus == CarApprovalStatus.Approved
        && PaymentStatus is CarListingPaymentStatus.Paid or CarListingPaymentStatus.NotRequired;

    // Listing metadata
    public Guid? PostedByUserId { get; set; }
    public CarListingSource ListingSource { get; set; } = CarListingSource.Ridelance;
    public CarApprovalStatus ApprovalStatus { get; set; } = CarApprovalStatus.Approved;
    public CarListingPaymentStatus PaymentStatus { get; set; } = CarListingPaymentStatus.NotRequired;
    public string? StripeCheckoutSessionId { get; set; }
    public string? StripeSubscriptionId { get; set; }
    public DateTime? PaidAtUtc { get; set; }

    // Audit
    /// <summary>
    /// Scorul „Recomandate", 0–100 (spec §5.2).
    /// </summary>
    /// <remarks>
    /// Stocat, nu calculat la fiecare cerere: sortarea marketplace-ului nu are voie să depindă de
    /// cât durează un calcul per rând. Se recalculează la evenimentele care îl pot schimba și,
    /// pentru componenta de prospețime, printr-un job nocturn.
    /// </remarks>
    public int RecommendationScore { get; set; }

    /// <summary>Când a fost calculat ultima dată. `null` = niciodată, deci scorul e 0 implicit.</summary>
    public DateTime? ScoreComputedAtUtc { get; set; }

    // ── Dosarul vehiculului ────────────────────────────────────────────────────────────────
    // Opționale prin design: o mașină se publică fără ele (vezi pasul 5 din fluxul de adăugare).
    // Devin obligatorii abia înainte de generarea primului contract de închiriere.

    public string? PlateNumber { get; set; }
    public string? Vin { get; set; }
    public int? Mileage { get; set; }
    public DateTime? FirstRegistrationAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    // Analytics (forms = Leads.Count)
    public int ViewCount { get; set; }

    /// <summary>Vizitatori distincți, după hash. Un refresh nu îl mai mișcă.</summary>
    public int UniqueViewCount { get; set; }
    public int ClickCount { get; set; }

    // Navigation
    public List<CarImage> Images { get; set; } = [];
    public List<CarLead> Leads { get; set; } = [];
}
