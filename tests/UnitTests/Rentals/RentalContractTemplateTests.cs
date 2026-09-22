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
    public void Ce_nu_stim_ramane_linie_de_completat()
    {
        string tex = Tex(RentalDocumentType.RentalContract);

        tex.ShouldContain(@"penalități de \gol{1.5cm} \% pe zi");
        tex.ShouldContain(@"Franșiza aplicabilă în cazul unei daune este de \gol{3cm}");
        tex.ShouldContain(@"preaviz de \gol{1.5cm} zile");
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
        tex.ShouldContain(@"$\square$ certificat de înmatriculare");
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

    private static RentalDocumentData Compose(RentalDocumentType type, Tenant tenant) => RentalDocumentComposer.Compose(
        type,
        new Rental
        {
            PublicCode = "RL-000042",
            StartAtUtc = new DateTime(2026, 9, 1, 9, 0, 0, DateTimeKind.Utc),
            EndAtUtc = new DateTime(2026, 9, 29, 9, 0, 0, DateTimeKind.Utc),
            WeeklyRentBani = 45_000,
            DepositBani = 100_000,
            HasKmLimit = true,
            MileageLimit = 2000,
            ExtraKmCostBani = 50,
            StartMileage = 45_000,
            FuelLevelAtPickup = "Plin",
            Accessories = ["Chei", "Stingător", "Triunghi"],
        },
        new Car { Brand = "Dacia", Model = "Logan", Year = 2022, PlateNumber = "B 123 RID", Vin = "UU1TEST0000000001" },
        new CompanyProfile
        {
            LegalName = "Flota Test SRL",
            Cui = "RO12345678",
            RegCom = "J40/123/2024",
            RegisteredOffice = "București, Str. Exemplu 1",
            Iban = "RO49AAAA1B31007593840000",
            LegalRepresentative = "Ion Ionescu",
        },
        tenant,
        null);
}
