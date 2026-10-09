using Application.PfaRegistrations.Onboarding.ArrFleet;
using Domain.Documents;
using Domain.PfaRegistrations;
using Domain.PfaRegistrations.ArrFleet;
using Shouldly;
using Xunit;

namespace UnitTests.PfaRegistrations;

/// <summary>Pasul „ARR &amp; Cont Flotă”: suma, actele cerute și statusurile procedurii.</summary>
public sealed class ArrFleetRulesTests
{
    [Theory]
    [InlineData(ArrFleetPlatforms.Uber, 40_800)]
    [InlineData(ArrFleetPlatforms.Bolt, 40_800)]
    [InlineData(ArrFleetPlatforms.Uber | ArrFleetPlatforms.Bolt, 41_600)]
    [InlineData(ArrFleetPlatforms.None, 0)]
    public void Amount_Is408ForOnePlatformAnd416ForBoth(ArrFleetPlatforms platforms, long expectedBani) =>
        ArrFleetPricing.AmountBani(platforms).ShouldBe(expectedBani);

    [Fact]
    public void Payments_AreThreeSeparateExactAmounts()
    {
        IReadOnlyList<ArrFleetPayment> uber = ArrFleetRules.Payments(ArrFleetPlatforms.Uber);
        uber.Select(p => p.AmountBani).ShouldBe([30_000, 10_000, 800]);
        uber.Select(p => p.ProofCategory).ShouldBe(
        [
            DocumentCategory.ArrAuthorizationPaymentProof,
            DocumentCategory.ArrCertifiedCopyPaymentProof,
            DocumentCategory.ArrBadgesPaymentProof,
        ]);
        uber[2].Explanation.ShouldBe("Câte 8 lei pe platformă: 8 lei Uber.");

        IReadOnlyList<ArrFleetPayment> both = ArrFleetRules.Payments(ArrFleetPlatforms.Uber | ArrFleetPlatforms.Bolt);
        both[2].AmountBani.ShouldBe(1_600);
        both.Sum(p => p.AmountBani).ShouldBe(ArrFleetPricing.AmountBani(ArrFleetPlatforms.Uber | ArrFleetPlatforms.Bolt));
    }

    [Fact]
    public void Missing_AsksForEachPaymentProofSeparately()
    {
        List<Document> documents = CompleteDocuments();
        documents.RemoveAll(d => d.Category == DocumentCategory.ArrCertifiedCopyPaymentProof);

        ArrFleetRules.Missing(Application(ArrFleetVehicleOwnership.Ownership), [DriverAccount(hasAccount: false)], documents)
            .ShouldBe(["Dovada plății copiei conforme"]);
    }

    [Fact]
    public void Missing_WithEverythingInPlace_IsEmpty()
    {
        ArrFleetApplication application = Application(ArrFleetVehicleOwnership.Rental);

        ArrFleetRules.Missing(application, [DriverAccount(hasAccount: false)], CompleteDocuments(DocumentCategory.ContractInchiriere))
            .ShouldBeEmpty();
    }

    [Fact]
    public void Missing_AsksForTheContractOfTheChosenOwnershipOnly()
    {
        ArrFleetApplication owned = Application(ArrFleetVehicleOwnership.Ownership);
        ArrFleetRules.Missing(owned, [DriverAccount(hasAccount: false)], CompleteDocuments()).ShouldBeEmpty();

        ArrFleetApplication leasing = Application(ArrFleetVehicleOwnership.Leasing);
        ArrFleetRules.Missing(leasing, [DriverAccount(hasAccount: false)], CompleteDocuments(DocumentCategory.ContractInchiriere))
            .ShouldBe(["Contract de leasing"]);
    }

    [Fact]
    public void Missing_ASupersededContractNoLongerCounts()
    {
        List<Document> documents = CompleteDocuments(DocumentCategory.ContractComodat);
        documents.Single(d => d.Category == DocumentCategory.ContractComodat).IsSuperseded = true;

        ArrFleetRules.Missing(Application(ArrFleetVehicleOwnership.Loan), [DriverAccount(hasAccount: false)], documents)
            .ShouldBe(["Comodat autentificat la notariat"]);
    }

    [Fact]
    public void Missing_ExistingDriverAccountNeedsEmailAndPhone_NoAccountNeedsNothing()
    {
        ArrFleetApplication application = Application(ArrFleetVehicleOwnership.Ownership);

        ArrFleetRules.Missing(application, [DriverAccount(hasAccount: true)], CompleteDocuments())
            .ShouldBe(["emailul și telefonul contului Uber"]);

        PfaPlatformAccount filled = DriverAccount(hasAccount: true);
        filled.DriverEmail = "sofer@example.com";
        filled.DriverPhone = "+40712345678";
        ArrFleetRules.Missing(application, [filled], CompleteDocuments()).ShouldBeEmpty();

        ArrFleetRules.Missing(application, [], CompleteDocuments()).ShouldBe(["contul de șofer Uber"]);
    }

    [Fact]
    public void Missing_CascoIsOptional()
    {
        List<Document> documents = CompleteDocuments();
        documents.ShouldNotContain(d => d.Category == DocumentCategory.Casco);

        ArrFleetRules.Missing(Application(ArrFleetVehicleOwnership.Ownership), [DriverAccount(hasAccount: false)], documents)
            .ShouldBeEmpty();
    }

    [Fact]
    public void BadgesProof_UploadedBeforeTheAmountChanged_IsOutdated()
    {
        ArrFleetApplication application = Application(ArrFleetVehicleOwnership.Ownership);
        List<Document> documents = CompleteDocuments();
        application.PaymentAmountChangedAtUtc = documents.Single(d => d.Category == DocumentCategory.ArrBadgesPaymentProof)
            .UploadedAtUtc.AddMinutes(1);

        ArrFleetRules.PaymentProofOutdated(application, documents).ShouldBeTrue();
        ArrFleetRules.Missing(application, [DriverAccount(hasAccount: false)], documents)
            .ShouldBe(["dovada plății ecusoanelor pentru suma actuală"]);
    }

    [Theory]
    [InlineData(ArrFleetStatus.InProgress, null)]
    [InlineData(ArrFleetStatus.AuthorizationIssued, "Autorizație de transport")]
    [InlineData(ArrFleetStatus.CertifiedCopyIssued, "Autorizație de transport")]
    [InlineData(ArrFleetStatus.Completed, "Autorizație de transport")]
    public void Status_FromAuthorizationOnward_NeedsTheOfficialDocument(ArrFleetStatus target, string? missing)
    {
        ArrFleetRules.MissingOfficialDocument(Application(ArrFleetVehicleOwnership.Ownership), target, [])
            .ShouldBe(missing);
    }

    [Fact]
    public void BadgesIssued_NeedsTheBadgeOfEveryChosenPlatform()
    {
        ArrFleetApplication application = Application(ArrFleetVehicleOwnership.Ownership);
        application.Platforms = ArrFleetPlatforms.Uber | ArrFleetPlatforms.Bolt;

        List<Document> documents =
        [
            Doc(DocumentCategory.AutorizatieTransportAlternativ),
            Doc(DocumentCategory.CopieConforma),
            Doc(DocumentCategory.EcusonUber),
        ];

        ArrFleetRules.MissingOfficialDocument(application, ArrFleetStatus.BadgesIssued, documents).ShouldBe("Ecuson Bolt");

        documents.Add(Doc(DocumentCategory.EcusonBolt));
        ArrFleetRules.MissingOfficialDocument(application, ArrFleetStatus.Completed, documents).ShouldBeNull();
    }

    private static ArrFleetApplication Application(ArrFleetVehicleOwnership ownership) => new()
    {
        Id = Guid.NewGuid(),
        Platforms = ArrFleetPlatforms.Uber,
        PaymentAmountBani = ArrFleetPricing.AmountBani(ArrFleetPlatforms.Uber),
        VehicleOwnership = ownership,
    };

    private static PfaPlatformAccount DriverAccount(bool hasAccount) => new()
    {
        Id = Guid.NewGuid(),
        Provider = PfaPlatformProvider.Uber,
        Kind = PfaPlatformAccountKind.Driver,
        IsSelectedByUser = true,
        DriverHasExistingAccount = hasAccount,
    };

    private static List<Document> CompleteDocuments(params DocumentCategory[] extra) =>
    [
        Doc(DocumentCategory.AdeverintaMedicala),
        Doc(DocumentCategory.AvizPsihologic),
        Doc(DocumentCategory.CazierJudiciar),
        Doc(DocumentCategory.ArrAuthorizationPaymentProof),
        Doc(DocumentCategory.ArrCertifiedCopyPaymentProof),
        Doc(DocumentCategory.ArrBadgesPaymentProof),
        Doc(DocumentCategory.Talon),
        Doc(DocumentCategory.RCA),
        Doc(DocumentCategory.AsigurareCalatori),
        .. extra.Select(Doc),
    ];

    private static Document Doc(DocumentCategory category) => new()
    {
        Id = Guid.NewGuid(),
        Category = category,
        Status = DocumentStatus.Pending,
        UploadedAtUtc = new DateTime(2026, 10, 1, 10, 0, 0, DateTimeKind.Utc),
    };
}
