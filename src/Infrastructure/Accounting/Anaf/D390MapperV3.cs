using Anaf.D390.V3;
using Application.Abstractions.Anaf;
using Application.Accounting.Contracts;
using Domain.Accounting;

namespace Infrastructure.Accounting.Anaf;

/// <summary>
/// D390 (declarația recapitulativă), namespace <c>mfp:anaf:dgti:d390:declaratie:v3</c>. Câte o
/// operațiune <c>S</c> (achiziții intracomunitare de servicii) pe furnizor, cu baza în lei întregi.
/// Fără secțiunea <c>cos</c> (stocuri la dispoziția clientului).
/// </summary>
internal sealed class D390MapperV3 : IDeclarationXmlMapper
{
    public const string Namespace = "mfp:anaf:dgti:d390:declaratie:v3";

    public DeclarationType Type => DeclarationType.D390;

    public string SchemaVersion => "v3-20210212";

    public byte[] Map(AnafDeclarationInput input)
    {
        (int month, int year) = AnafFormat.Period(input.Period);
        var declaration = new Declaratie390Type
        {
            Luna = (byte)month,
            An = (ushort)year,
            DRec = (byte)(input.Rectificative ? 1 : 0),
            NumeDeclar = AnafFormat.Text(input.Taxpayer.DeclarantLastName),
            PrenumeDeclar = AnafFormat.Text(input.Taxpayer.DeclarantFirstName),
            FunctieDeclar = AnafFormat.Text(input.Taxpayer.DeclarantFunction),
            Cui = input.Taxpayer.Cui,
            Den = AnafFormat.Text(input.Taxpayer.Name),
            Adresa = AnafFormat.Text(input.Taxpayer.Address),
        };

        foreach (AnafDeclarationLine line in input.Lines)
        {
            declaration.Operatie.Add(new OperatieType
            {
                Tip = StrListaTipuriSType.S,
                Tara = Enum.TryParse(line.SupplierCountry, ignoreCase: true, out StrListaTariSType country) ? country : default,
                CodO = AnafFormat.VatNumber(line.SupplierVatId),
                DenO = AnafFormat.OperatorName(line.SupplierName),
                Baza = WholeLei(line.Base),
            });
        }

        long total = declaration.Operatie.Sum(operation => operation.Baza);
        declaration.Rezumat = new RezumatType
        {
            NrPag = 1,
            NrOpi = (ulong)declaration.Operatie.Count,
            BazaS = total,
            TotalBaza = total,
        };
        declaration.TotalPlataA = Checksum(declaration);
        return AnafFormat.Serialize(declaration, Namespace);
    }

    public IReadOnlyList<ValidationMessage> Verify(AnafDeclarationInput input, byte[] xml)
    {
        Declaratie390Type? declaration = AnafFormat.Deserialize<Declaratie390Type>(xml);
        if (declaration?.Rezumat is null)
        {
            return [new ValidationMessage(null, "XML-ul D390 nu poate fi citit.")];
        }

        var messages = new List<ValidationMessage>();
        (int month, int year) = AnafFormat.Period(input.Period);
        if (declaration.Luna != month || declaration.An != year)
        {
            messages.Add(new ValidationMessage("luna", $"Perioada din XML ({declaration.Luna}/{declaration.An}) nu e {input.Period}."));
        }

        if (declaration.Cui != input.Taxpayer.Cui)
        {
            messages.Add(new ValidationMessage("cui", $"CUI-ul din XML ({declaration.Cui}) diferă de al PFA-ului ({input.Taxpayer.Cui})."));
        }

        foreach (AnafDeclarationLine line in input.Lines)
        {
            string code = AnafFormat.VatNumber(line.SupplierVatId);
            OperatieType? operation = declaration.Operatie.FirstOrDefault(o => o.CodO == code);
            if (operation is null || operation.Baza != WholeLei(line.Base) || operation.Tip != StrListaTipuriSType.S)
            {
                messages.Add(new ValidationMessage("operatie", $"Furnizorul {line.SupplierName} ({line.SupplierVatId}) nu apare în XML ca operațiune S cu baza {WholeLei(line.Base)} lei."));
            }
            else if (!string.Equals(operation.Tara.ToString(), line.SupplierCountry, StringComparison.OrdinalIgnoreCase))
            {
                messages.Add(new ValidationMessage("tara", $"Țara {line.SupplierCountry} a furnizorului {line.SupplierName} nu e în nomenclatorul D390."));
            }
        }

        if (declaration.Operatie.Count != input.Lines.Count)
        {
            messages.Add(new ValidationMessage("operatie", $"XML-ul are {declaration.Operatie.Count} operațiuni, calculul are {input.Lines.Count}."));
        }

        RezumatType summary = declaration.Rezumat;
        long bases = declaration.Operatie.Sum(o => o.Baza);
        long services = declaration.Operatie.Where(o => o.Tip == StrListaTipuriSType.S).Sum(o => o.Baza);
        if (summary.NrOpi != (ulong)declaration.Operatie.Count || summary.BazaS != services || summary.TotalBaza != bases)
        {
            messages.Add(new ValidationMessage("rezumat", "Rezumatul (număr de operatori, baza S, total) nu corespunde operațiunilor."));
        }

        if (declaration.TotalPlataA != Checksum(declaration))
        {
            messages.Add(new ValidationMessage("totalPlata_A", $"Suma de control ({declaration.TotalPlataA}) nu e nrOPI plus suma bazelor ({Checksum(declaration)})."));
        }

        if (input.Amount != 0)
        {
            messages.Add(new ValidationMessage("total", "D390 e doar de raportare: totalul de plată trebuie să fie 0."));
        }

        return messages;
    }

    /// <summary><c>totalPlata_A = nrOPI + bazaL + bazaT + bazaA + bazaP + bazaS + bazaR</c> (structura D390).</summary>
    private static long Checksum(Declaratie390Type d) =>
        (long)d.Rezumat.NrOpi + d.Rezumat.BazaL + d.Rezumat.BazaT + d.Rezumat.BazaA + d.Rezumat.BazaP + d.Rezumat.BazaS + d.Rezumat.BazaR;

    private static long WholeLei(decimal value) => (long)Math.Round(value, 0, MidpointRounding.AwayFromZero);
}
