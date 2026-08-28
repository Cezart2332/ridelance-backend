using Application.Abstractions.Dossiers;
using Infrastructure.Dossiers.Latex;
using Shouldly;
using Xunit;

namespace UnitTests.Rentals;

/// <summary>
/// Sursa LaTeX a documentelor de închiriere.
/// </summary>
/// <remarks>
/// Sursa se verifică aici, nu PDF-ul: dacă textul care intră în document e corect escapat și pus la
/// locul lui, restul e treaba motorului. Interesează în primul rând escaparea — e singurul loc din
/// aplicație unde un text scris de un utilizator ajunge într-un limbaj care se execută.
/// </remarks>
public sealed class RentalDocumentLatexTests
{
    [Fact]
    public void Caracterele_speciale_din_datele_firmei_raman_text()
    {
        string tex = Build(new RentalDocumentField("Denumire", @"S.C. 100% & Fiii #1 \ Co_2"));

        tex.ShouldContain(@"S.C. 100\% \& Fiii \#1 \textbackslash{} Co\_2");
    }

    [Fact]
    public void O_comanda_scrisa_intr_un_camp_nu_se_executa()
    {
        // Un chiriaș care își scrie numele așa nu trebuie să poată citi fișiere de pe server.
        string tex = Build(new RentalDocumentField("Nume", @"\input{/etc/passwd}"));

        tex.ShouldNotContain(@"\input{/etc/passwd}");
        tex.ShouldContain(@"\textbackslash{}input\{/etc/passwd\}");
    }

    [Fact]
    public void Un_camp_gol_se_tipareste_ca_liniuta()
    {
        Build(new RentalDocumentField("Adresă", null)).ShouldContain(@"Adresă & --- \\");
    }

    [Fact]
    public void O_valoare_scrisa_pe_mai_multe_randuri_intra_pe_un_singur_rand()
    {
        // Rândul nou într-o celulă de tabel rupe alinierea documentului.
        Build(new RentalDocumentField("Observații", "zgârietură\npe ușa dreapta"))
            .ShouldContain(@"Observații & zgârietură pe ușa dreapta \\");
    }

    [Fact]
    public void Conditiile_isi_pastreaza_randurile_si_paragrafele()
    {
        string tex = Build(clauses: "1. Fumatul interzis.\n2. Fără animale.\n\nAvariile se anunță în 24h.");

        tex.ShouldContain("1. Fumatul interzis." + @"\\{}" + "\n2. Fără animale.");
        tex.ShouldContain(@"\alineat{Avariile se anunță în 24h.}");
    }

    [Fact]
    public void Documentul_are_titlul_numarul_si_liniile_de_semnatura()
    {
        string tex = Build();

        tex.ShouldContain(@"{\LARGE\bfseries Contract de închiriere}");
        tex.ShouldContain("Nr. RL-000123");
        tex.ShouldContain(@"Flota S.R.L. & Ion Popescu \\");
    }

    [Fact]
    public void Documentul_nu_poarta_marca_platformei()
    {
        // Contractul e între firmă și chiriaș; RIDElance nu e parte în el.
        Build().ShouldNotContain("RIDElance");
    }

    private static string Build(RentalDocumentField? field = null, string? clauses = null) =>
        RentalDocumentLatex.Build(new RentalDocumentData(
            "Contract de închiriere",
            "RL-000123",
            [new RentalDocumentSection("Proprietar", [field ?? new RentalDocumentField("Denumire", "Flota S.R.L.")])],
            clauses,
            ["Flota S.R.L.", "Ion Popescu"],
            new DateTime(2026, 8, 28, 9, 0, 0, DateTimeKind.Utc)));
}
