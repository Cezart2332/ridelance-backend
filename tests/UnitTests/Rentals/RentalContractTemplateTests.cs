using Application.Abstractions.Dossiers;
using Application.Rentals.Documents;
using Domain.Cars;
using Domain.Companies;
using Domain.Rentals;
using Infrastructure.Dossiers.Latex;
using Shouldly;
using Xunit;

namespace UnitTests.Rentals;

/// <summary>
/// Contractul și procesul-verbal urmează șablonul firmelor (<c>CTR inchiriere.pdf</c>), completat.
/// </summary>
/// <remarks>
/// Textul șablonului rămâne neschimbat; se completează doar locurile goale, cu ce știe deja sistemul.
/// Ce nu știe rămâne linie de completat — un contract nu inventează o zi de plată sau o franșiză.
/// Unde șablonul spune „dacă părțile au convenit”, lipsa valorii are formularea ei.
/// </remarks>
public sealed class RentalContractTemplateTests
{
    [Fact]
    public void Contractul_are_titlul_si_capitolele_sablonului()
    {
        string tex = Tex(RentalDocumentType.RentalContract);

        tex.ShouldContain("CONTRACT DE ÎNCHIRIERE AUTOVEHICUL");
        tex.ShouldContain("Nr. RL-000042 /");
        foreach (string chapter in new[]
        {
            "I. PĂRȚILE CONTRACTANTE", "II. OBIECTUL CONTRACTULUI", "III. DURATA CONTRACTULUI. CHIRIA. GARANȚIA",
            "IV. PREDAREA ȘI FOLOSIREA AUTOVEHICULULUI", "V. OBLIGAȚIILE PĂRȚILOR", "VI. ASIGURAREA. ACCIDENTELE ȘI DAUNELE",
            "VII. ÎNCETAREA CONTRACTULUI. RESTITUIREA AUTOVEHICULULUI", "VIII. DISPOZIȚII FINALE",
        })
        {
            tex.ShouldContain(@"\capitol{" + chapter + "}");
        }
    }

    [Fact]
    public void Datele_firmei_ale_chiriasului_si_ale_masinii_se_completeaza()
    {
        string tex = Tex(RentalDocumentType.RentalContract);

        // Forma juridică nu se dublează: „Flota Test SRL”, nu „Flota Test SRL S.R.L.”.
        tex.ShouldContain(@"1.1. \textbf{Flota Test SRL}, cu sediul în \textbf{București, Str. Exemplu 1}");
        tex.ShouldContain(@"CUI \textbf{RO12345678}");
        tex.ShouldContain(@"cont IBAN \textbf{RO49AAAA1B31007593840000}");
        tex.ShouldContain(@"1.2. \textbf{Adrian Popescu}, cu domiciliul în \textbf{Cluj-Napoca}");
        tex.ShouldContain(@"CNP \textbf{1900101123456}, identificat(ă) cu CI seria \textbf{CJ} nr. \textbf{123456}");
        tex.ShouldContain(@"marca \textbf{Dacia}, model \textbf{Logan}, număr de înmatriculare \textbf{B 123 RID}");
        tex.ShouldContain(@"Chiria este de \textbf{450,00} lei / săptămână");
        tex.ShouldContain(@"garanție în cuantum de \textbf{1.000,00} lei");
        tex.ShouldContain(@"Kilometrajul inclus este de \textbf{2.000} km / săptămână");
        tex.ShouldContain(@"perioada \textbf{de 28 de zile}");
    }

    [Fact]
    public void Termenii_conveniti_se_completeaza()
    {
        string tex = Tex(RentalDocumentType.RentalContract);

        tex.ShouldContain(@"reprezentată prin \textbf{Ion Ionescu}, în calitate de \textbf{administrator}, denumită în continuare Locator");
        tex.ShouldContain(@"se achită în fiecare zi de \textbf{luni}, prin \textbf{transfer bancar în contul IBAN al Locatorului}.");
        tex.ShouldContain(@"penalități de \textbf{0,5} \% pe zi");
        tex.ShouldContain(@"chiria pentru perioada de indisponibilitate \textbf{se reduce proporțional cu durata indisponibilității}.");
        tex.ShouldContain(@"Franșiza aplicabilă în cazul unei daune este de \textbf{1.500,00 lei}.");
        tex.ShouldContain(@"cu un preaviz de \textbf{15 zile}.");
        tex.ShouldContain(@"conducător autorizat: \textbf{Mihai Pop}, CNP \textbf{1850202123456}, permis de conducere seria/nr. \textbf{CJ123456}.");
        tex.ShouldContain(@"Părțile stabilesc: \textbf{plin - plin}.");
        tex.ShouldContain(@"3.6. Pe lângă chirie și garanție, Locatarul achită costurile convenite la semnare, în sumă de \textbf{200,00} lei.");
        tex.ShouldNotContain(@"\gol{");
    }

    [Fact]
    public void Ce_nu_stim_ramane_linie_de_completat()
    {
        Rental rental = Rental();
        rental.PaymentDueDay = null;
        rental.PaymentMethod = null;
        rental.FranchiseBani = null;
        rental.NoticeDays = null;
        rental.UnavailabilityRule = null;

        string tex = RentalDocumentLatex.Build(Compose(RentalDocumentType.RentalContract, Tenant(), rental));

        tex.ShouldContain(@"se achită la data de \gol{2.5cm}, prin \gol{4cm}.");
        tex.ShouldContain(@"Franșiza aplicabilă în cazul unei daune este de \gol{3cm}");
        tex.ShouldContain(@"preaviz de \gol{1.5cm} zile");
        tex.ShouldContain(@"perioada de indisponibilitate \gol{5cm}.");
    }

    [Fact]
    public void Fara_penalitati_si_fara_sofer_suplimentar_textul_spune_asta()
    {
        Rental rental = Rental();
        rental.LatePenaltyPercentPerDay = null;
        rental.AdditionalDriverName = null;
        rental.OtherCostsBani = 0;

        string tex = RentalDocumentLatex.Build(Compose(RentalDocumentType.RentalContract, Tenant(), rental));

        tex.ShouldContain("3.5. Pentru întârzierea la plata chiriei, părțile nu au convenit penalități.");
        tex.ShouldContain("4.3. Autovehiculul poate fi condus numai de Locatar.");
        tex.ShouldNotContain("3.6.");
        tex.ShouldNotContain(@"\gol{");
    }

    [Theory]
    [InlineData("Cash", @", \textbf{în numerar}.")]
    [InlineData("Card", @", \textbf{cu card bancar}.")]
    [InlineData("Other", @", prin \textbf{altă modalitate convenită de părți}.")]
    public void Modalitatea_de_plata_isi_alege_prepozitia(string method, string expected)
    {
        Rental rental = Rental();
        rental.PaymentMethod = Enum.Parse<RentalPaymentMethod>(method);

        RentalDocumentLatex.Build(Compose(RentalDocumentType.RentalContract, Tenant(), rental)).ShouldContain(expected);
    }

    [Fact]
    public void Franșiza_zero_se_scrie_ca_atare()
    {
        Rental rental = Rental();
        rental.FranchiseBani = 0;

        RentalDocumentLatex.Build(Compose(RentalDocumentType.RentalContract, Tenant(), rental))
            .ShouldContain(@"este de \textbf{0,00 lei (fără franșiză)}.");
    }

    [Theory]
    [InlineData(1, "o zi")]
    [InlineData(7, "7 zile")]
    [InlineData(30, "30 de zile")]
    public void Preavizul_se_acorda_gramatical(int days, string expected)
    {
        Rental rental = Rental();
        rental.NoticeDays = days;

        RentalDocumentLatex.Build(Compose(RentalDocumentType.RentalContract, Tenant(), rental))
            .ShouldContain(@"cu un preaviz de \textbf{" + expected + "}.");
    }

    [Fact]
    public void Un_pfa_proprietar_nu_devine_srl_pe_contract()
    {
        CompanyProfile company = Company();
        company.OwnerType = OwnerType.Pfa;
        company.LegalName = "Ionescu Ion PFA";

        string tex = RentalDocumentLatex.Build(Compose(RentalDocumentType.RentalContract, Tenant(), company: company));

        tex.ShouldContain(@"1.1. \textbf{Ionescu Ion PFA}, cu sediul");
        tex.ShouldNotContain("PFA S.R.L.");
    }

    [Fact]
    public void Chiriasul_srl_isi_are_reprezentantul_in_clauza_1_2()
    {
        var tenant = new Tenant { Type = TenantType.Srl, Name = "Transport Rapid SRL", Cui = "RO999", RegCom = "J40/1/2020", Address = "Iași", RepresentativeName = "Maria Ionescu" };

        RentalDocumentLatex.Build(Compose(RentalDocumentType.RentalContract, tenant))
            .ShouldContain(@"reprezentat(ă), după caz, prin \textbf{Maria Ionescu}, denumit(ă) în continuare Locatar");
    }

    [Fact]
    public void Anexa_ia_cheile_documentele_si_avariile_de_pe_inchiriere()
    {
        string tex = Tex(RentalDocumentType.RentalContract);

        tex.ShouldContain(@"Chei predate: \textbf{2} set/seturi.");
        tex.ShouldContain(@"Documente predate: $\boxtimes$ certificat de înmatriculare $\boxtimes$ RCA $\boxtimes$ alte documente: \textbf{ITP}.");
        tex.ShouldContain(@"avarii existente la predare: \textbf{Zgârietură bara spate}.");
        // Fără predare consemnată în aplicație nu se știe dacă s-au făcut fotografii.
        tex.ShouldContain(@"Fotografii realizate la predare: $\square$ Da $\square$ Nu.");
    }

    [Fact]
    public void Predarea_consemnata_bate_ce_s_a_notat_pe_inchiriere()
    {
        var checkIn = new CheckRecord
        {
            Kind = CheckKind.CheckIn,
            OccurredAtUtc = new DateTime(2026, 9, 2, 10, 0, 0, DateTimeKind.Utc),
            Mileage = 45_120,
            FuelLevel = "3/4",
            KeysCount = 1,
            Documents = [HandoverDocumentCodes.Rca],
            Accessories = ["Chei", "Vestă"],
            Damages = "Fără avarii vizibile",
            Photos = [new CheckPhoto { Slot = CheckPhotoSlot.Front }],
        };

        string tex = RentalDocumentLatex.Build(Compose(RentalDocumentType.HandoverProtocol, Tenant(), checkIn: checkIn));

        tex.ShouldContain(@"Încheiat astăzi, \textbf{02.09.2026}");
        tex.ShouldContain(@"Kilometraj la predare: \textbf{45.120} km.");
        tex.ShouldContain(@"Nivel combustibil / stare baterie: \textbf{3/4}.");
        tex.ShouldContain(@"Chei predate: \textbf{1} set/seturi.");
        tex.ShouldContain(@"Documente predate: $\square$ certificat de înmatriculare $\boxtimes$ RCA");
        tex.ShouldContain(@"Accesorii/dotări predate: \textbf{Vestă}.");
        tex.ShouldContain(@"avarii existente la predare: \textbf{Fără avarii vizibile}.");
        tex.ShouldContain(@"Fotografii realizate la predare: $\boxtimes$ Da $\square$ Nu.");
    }

    [Fact]
    public void Restituirea_se_completeaza_din_primirea_consemnata()
    {
        var checkOut = new CheckRecord
        {
            Kind = CheckKind.CheckOut,
            OccurredAtUtc = new DateTime(2026, 9, 29, 15, 0, 0, DateTimeKind.Utc),
            Mileage = 47_800,
            FuelLevel = "1/2",
            KeysCount = 2,
            Documents = [HandoverDocumentCodes.RegistrationCertificate, HandoverDocumentCodes.Rca],
            Damages = "Oglindă stânga spartă",
            Notes = "Se reține contravaloarea oglinzii.",
        };

        string tex = RentalDocumentLatex.Build(Compose(RentalDocumentType.ReturnProtocol, Tenant(), checkOut: checkOut));

        tex.ShouldContain(@"Kilometraj la restituire: \textbf{47.800} km.");
        tex.ShouldContain(@"Chei restituite: \textbf{2} set/seturi.");
        tex.ShouldContain(@"Documente restituite: $\boxtimes$ certificat de înmatriculare $\boxtimes$ RCA $\square$ alte documente");
        tex.ShouldContain(@"avarii constatate la restituire: \textbf{Oglindă stânga spartă}.");
        tex.ShouldContain(@"Fotografii realizate la restituire: $\square$ Da $\boxtimes$ Nu.");
    }

    [Fact]
    public void Clauzele_suplimentare_stau_in_contract_nu_dupa_anexa()
    {
        RentalDocumentData data = Compose(RentalDocumentType.RentalContract, Tenant()) with { Clauses = "Fumatul este interzis." };
        string tex = RentalDocumentLatex.Build(data);

        int clauses = tex.IndexOf("Clauze suplimentare", StringComparison.Ordinal);
        clauses.ShouldBeGreaterThan(tex.IndexOf("VII. ÎNCETAREA CONTRACTULUI", StringComparison.Ordinal));
        clauses.ShouldBeLessThan(tex.IndexOf("VIII. DISPOZIȚII FINALE", StringComparison.Ordinal));
        clauses.ShouldBeLessThan(tex.IndexOf("ANEXA NR. 1", StringComparison.Ordinal));
    }

    [Fact]
    public void Textul_liber_terminat_in_punct_nu_dubleaza_punctul()
    {
        string tex = Tex(RentalDocumentType.HandoverProtocol);

        tex.ShouldContain(@"Observații: \textbf{Mașina a fost spălată}.");
        tex.ShouldNotContain("..");
    }

    [Fact]
    public void Datele_sunt_in_ora_romaniei_nu_a_serverului()
    {
        // 22:30 UTC pe 31 august e deja 1 septembrie în România (UTC+3 vara).
        RomanianTime.Date(new DateTime(2026, 8, 31, 22, 30, 0, DateTimeKind.Utc)).ShouldBe("01.09.2026");
        // Iarna, UTC+2.
        RomanianTime.Date(new DateTime(2026, 12, 31, 21, 59, 0, DateTimeKind.Utc)).ShouldBe("31.12.2026");
        RomanianTime.Date(new DateTime(2026, 12, 31, 22, 0, 0, DateTimeKind.Utc)).ShouldBe("01.01.2027");
    }

    [Fact]
    public void Chiriasul_firma_foloseste_varianta_cu_sediu_si_registrul_comertului()
    {
        var tenant = new Tenant { Type = TenantType.Srl, Name = "Transport Rapid SRL", Cui = "RO999", RegCom = "J40/1/2020", Address = "Iași" };
        string tex = RentalDocumentLatex.Build(Compose(RentalDocumentType.RentalContract, tenant));

        tex.ShouldContain(@"1.2. \textbf{Transport Rapid SRL}, cu sediul în \textbf{Iași}, CUI \textbf{RO999}, înregistrat(ă) la Registrul Comerțului sub nr. \textbf{J40/1/2020}");
        tex.ShouldNotContain("identificat(ă) cu CI");
    }

    [Fact]
    public void Contractul_are_anexa_cu_procesul_verbal_pe_pagina_noua()
    {
        string tex = Tex(RentalDocumentType.RentalContract);

        tex.ShouldContain(@"\newpage");
        tex.ShouldContain("ANEXA NR. 1");
        tex.ShouldContain("PROCES-VERBAL DE PREDARE-PRIMIRE AUTOVEHICUL");
        tex.ShouldContain(@"Kilometraj la predare: \textbf{45.000} km.");
        tex.ShouldContain(@"Accesorii/dotări predate: \textbf{Stingător, Triunghi}");
        tex.ShouldContain(@"$\boxtimes$ certificat de înmatriculare");
        // Semnăturile stau unde le are șablonul: la finalul contractului și al anexei.
        tex.ShouldContain(@"\textbf{LOCATOR} & \textbf{LOCATAR}");
        tex.ShouldContain(@"\textbf{AM PREDAT, LOCATOR} & \textbf{AM PRIMIT, LOCATAR}");
        tex.ShouldContain(@"Nume: Flota Test SRL & Nume: Adrian Popescu");
    }

    [Fact]
    public void Procesul_verbal_de_restituire_inverseaza_rolurile()
    {
        string tex = Tex(RentalDocumentType.ReturnProtocol);

        tex.ShouldContain("PROCES-VERBAL DE RESTITUIRE AUTOVEHICUL");
        tex.ShouldContain("Locatarul predă, iar Locatorul primește");
        tex.ShouldContain(@"\textbf{AM PRIMIT, LOCATOR} & \textbf{AM PREDAT, LOCATAR}");
    }

    [Fact]
    public void Textul_scris_de_utilizator_ramane_text_si_in_sablon()
    {
        var tenant = new Tenant { Type = TenantType.Individual, Name = @"\input{/etc/passwd}" };
        string tex = RentalDocumentLatex.Build(Compose(RentalDocumentType.RentalContract, tenant));

        tex.ShouldNotContain(@"\input{/etc/passwd}");
        tex.ShouldContain(@"\textbf{\textbackslash{}input\{/etc/passwd\}}");
    }

    private static string Tex(RentalDocumentType type) => RentalDocumentLatex.Build(Compose(type, Tenant()));

    private static Tenant Tenant() => new()
    {
        Type = TenantType.Individual,
        Name = "Adrian Popescu",
        Cnp = "1900101123456",
        IdSeries = "CJ",
        IdNumber = "123456",
        Address = "Cluj-Napoca",
    };

    private static Rental Rental() => new()
    {
        PublicCode = "RL-000042",
        StartAtUtc = new DateTime(2026, 9, 1, 9, 0, 0, DateTimeKind.Utc),
        EndAtUtc = new DateTime(2026, 9, 29, 9, 0, 0, DateTimeKind.Utc),
        WeeklyRentBani = 45_000,
        DepositBani = 100_000,
        OtherCostsBani = 20_000,
        HasKmLimit = true,
        MileageLimit = 2000,
        ExtraKmCostBani = 50,
        FuelRule = "plin - plin",
        StartMileage = 45_000,
        FuelLevelAtPickup = "Plin",
        Accessories = ["Chei", "Stingător", "Triunghi"],
        PaymentDueDay = DayOfWeek.Monday,
        PaymentMethod = RentalPaymentMethod.BankTransfer,
        LatePenaltyPercentPerDay = 0.5m,
        FranchiseBani = 150_000,
        NoticeDays = 15,
        UnavailabilityRule = RentalUnavailabilityRule.Proportional,
        AdditionalDriverName = "Mihai Pop",
        AdditionalDriverCnp = "1850202123456",
        AdditionalDriverLicense = "CJ123456",
        KeysCount = 2,
        HandoverDocuments = [HandoverDocumentCodes.RegistrationCertificate, HandoverDocumentCodes.Rca],
        HandoverDocumentsOther = "ITP",
        DamagesAtPickup = "Zgârietură bara spate",
        Notes = "Mașina a fost spălată.",
    };

    private static CompanyProfile Company() => new()
    {
        LegalName = "Flota Test SRL",
        Cui = "RO12345678",
        RegCom = "J40/123/2024",
        RegisteredOffice = "București, Str. Exemplu 1",
        Iban = "RO49AAAA1B31007593840000",
        LegalRepresentative = "Ion Ionescu",
        LegalRepresentativeRole = "administrator",
    };

    private static RentalDocumentData Compose(
        RentalDocumentType type,
        Tenant tenant,
        Rental? rental = null,
        CompanyProfile? company = null,
        CheckRecord? checkIn = null,
        CheckRecord? checkOut = null) => RentalDocumentComposer.Compose(
        type,
        rental ?? Rental(),
        new Car { Brand = "Dacia", Model = "Logan", Year = 2022, PlateNumber = "B 123 RID", Vin = "UU1TEST0000000001" },
        company ?? Company(),
        tenant,
        null,
        checkIn,
        checkOut);
}
