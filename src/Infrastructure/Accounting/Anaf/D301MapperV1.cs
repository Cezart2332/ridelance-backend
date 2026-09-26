using System.Globalization;
using Anaf.D301.V1;
using Application.Abstractions.Anaf;
using Application.Accounting;
using Application.Accounting.Contracts;
using Domain.Accounting;

namespace Infrastructure.Accounting.Anaf;

/// <summary>
/// D301 (decont special de TVA), namespace <c>mfp:anaf:dgti:d301:declaratie:v1</c>. Fiecare factură
/// de comision e o achiziție intracomunitară de servicii: apare în secțiunea 4.1 și, cum secțiunea 4
/// le cuprinde pe cele din 4.1, și în secțiunea 4 (regula R32 a validatorului). PFA-ul e înregistrat
/// în scopuri de TVA doar pentru achiziții intracomunitare (<c>pers_inreg = 2</c>, art. 317).
/// </summary>
internal sealed class D301MapperV1 : IDeclarationXmlMapper
{
    public const string Namespace = "mfp:anaf:dgti:d301:declaratie:v1";

    public DeclarationType Type => DeclarationType.D301;

    public string SchemaVersion => "v1-20200130";

    public byte[] Map(AnafDeclarationInput input)
    {
        (int month, int year) = AnafFormat.Period(input.Period);
        double bases = (double)input.Lines.Sum(line => line.Base);
        double vat = (double)input.Lines.Sum(line => line.Value);

        var declaration = new Declaratie301Type
        {
            Luna = (byte)month,
            An = (ushort)year,
            DRec = (byte)(input.Rectificative ? 1 : 0),
            MijlTrans = 0,
            Temei = 0,
            Cif = input.Taxpayer.Cui,
            Denumire = AnafFormat.Text(input.Taxpayer.Name),
            Adresa = AnafFormat.Text(input.Taxpayer.Address),
            Banca = AnafFormat.Text(input.Taxpayer.BankName),
            Cont = AnafFormat.Text(input.Taxpayer.Iban),
            PersInreg = 2,
            NrEvid = decimal.Parse(AnafFormat.EvidenceNumber($"1030101{AnafFormat.PeriodAndDue(input.Period)}0000"), CultureInfo.InvariantCulture),
            Baza4 = bases,
            Tva4 = vat,
            Baza5 = bases,
            Tva5 = vat,
            NumeDeclarant = AnafFormat.Text(input.Taxpayer.DeclarantLastName),
            PrenumeDeclarant = AnafFormat.Text(input.Taxpayer.DeclarantFirstName),
            FunctiaDeclarant = AnafFormat.Text(input.Taxpayer.DeclarantFunction),
        };

        foreach (AnafDeclarationLine line in input.Lines)
        {
            declaration.Sectiune.Add(Section(IntNrTabelSType.Item4, line));
            declaration.Sectiune.Add(Section(IntNrTabelSType.Item5, line));
        }

        declaration.TotalPlataA = Checksum(declaration);
        return AnafFormat.Serialize(declaration, Namespace);
    }

    public IReadOnlyList<ValidationMessage> Verify(AnafDeclarationInput input, byte[] xml)
    {
        Declaratie301Type? declaration = AnafFormat.Deserialize<Declaratie301Type>(xml);
        if (declaration is null)
        {
            return [new ValidationMessage(null, "XML-ul D301 nu poate fi citit.")];
        }

        var messages = new List<ValidationMessage>();
        (int month, int year) = AnafFormat.Period(input.Period);
        if (declaration.Luna != month || declaration.An != year)
        {
            messages.Add(new ValidationMessage("luna", $"Perioada din XML ({declaration.Luna}/{declaration.An}) nu e {input.Period}."));
        }

        if (declaration.Cif != input.Taxpayer.Cui)
        {
            messages.Add(new ValidationMessage("cif", $"CIF-ul din XML ({declaration.Cif}) diferă de al PFA-ului ({input.Taxpayer.Cui})."));
        }

        if ((decimal)declaration.Tva4 != input.Amount)
        {
            messages.Add(new ValidationMessage("tva4", $"TVA-ul din XML ({Amount(declaration.Tva4)} lei) diferă de totalul declarației ({AccountingJson.Amount(input.Amount)} lei)."));
        }

        foreach (IntNrTabelSType section in new[] { IntNrTabelSType.Item4, IntNrTabelSType.Item5 })
        {
            var lines = declaration.Sectiune.Where(s => s.TipOperatie == section).ToList();
            (double baza, double tva) = section == IntNrTabelSType.Item4 ? (declaration.Baza4, declaration.Tva4) : (declaration.Baza5, declaration.Tva5);
            string name = section == IntNrTabelSType.Item4 ? "4" : "4.1";
            if (lines.Count != input.Lines.Count)
            {
                messages.Add(new ValidationMessage("sectiune", $"Secțiunea {name} are {lines.Count} facturi, calculul are {input.Lines.Count}."));
            }

            if ((decimal)baza != lines.Sum(l => (decimal)l.Baza) || (decimal)tva != lines.Sum(l => (decimal)l.Tva))
            {
                messages.Add(new ValidationMessage($"baza{(int)section + 1}", $"Totalurile secțiunii {name} nu sunt suma facturilor din ea."));
            }
        }

        foreach (AnafDeclarationLine line in input.Lines)
        {
            string number = AnafFormat.Text(line.DocumentNumber);
            SectiuneType? section = declaration.Sectiune.FirstOrDefault(s => s.TipOperatie == IntNrTabelSType.Item5 && s.NrDoc == number);
            if (section is null || (decimal)section.Baza != line.Base || (decimal)section.Tva != line.Value || (decimal)section.ValValuta != line.AmountInCurrency)
            {
                messages.Add(new ValidationMessage("sectiune", $"Factura {line.DocumentNumber ?? "fără număr"} nu apare în XML cu baza {AccountingJson.Amount(line.Base)} și TVA {AccountingJson.Amount(line.Value)}."));
                continue;
            }

            if (!string.Equals(section.TipValuta.ToString(), line.Currency, StringComparison.OrdinalIgnoreCase))
            {
                messages.Add(new ValidationMessage("tip_valuta", $"Moneda {line.Currency} a facturii {line.DocumentNumber} nu e în lista de valute a D301."));
            }

            if (line.DocumentDate is null || string.IsNullOrWhiteSpace(line.DocumentNumber))
            {
                messages.Add(new ValidationMessage("nr_doc", $"Factura din {AccountingJson.Amount(line.Base)} lei nu are număr sau dată."));
            }
        }

        if (declaration.TotalPlataA != Checksum(declaration))
        {
            messages.Add(new ValidationMessage("totalPlata_A", $"Suma de control ({declaration.TotalPlataA}) nu e partea întreagă a sumei bazelor și TVA-urilor ({Checksum(declaration)})."));
        }

        if (declaration.PersInreg != 2)
        {
            messages.Add(new ValidationMessage("pers_inreg", "PFA-ul e înregistrat în scopuri de TVA doar pentru achiziții intracomunitare (art. 317): pers_inreg = 2."));
        }

        string evidence = declaration.NrEvid.ToString(CultureInfo.InvariantCulture);
        if (!AnafFormat.IsValidEvidenceNumber(evidence) || !evidence.StartsWith("1030101", StringComparison.Ordinal) || evidence[7..17] != AnafFormat.PeriodAndDue(input.Period))
        {
            messages.Add(new ValidationMessage("nr_evid", $"Numărul de evidență a plății ({evidence}) nu e corect."));
        }

        return messages;
    }

    private static SectiuneType Section(IntNrTabelSType type, AnafDeclarationLine line) => new()
    {
        TipOperatie = type,
        NrDoc = AnafFormat.Text(line.DocumentNumber),
        DataDoc = line.DocumentDate is { } date ? AnafFormat.Date(date) : string.Empty,
        ValValuta = (double)line.AmountInCurrency,
        TipValuta = Enum.TryParse(line.Currency, ignoreCase: true, out StrTipValutaSType currency) ? currency : StrTipValutaSType.Ron,
        CursValutar = (double)(line.ExchangeRate ?? 1m),
        Baza = (double)line.Base,
        Tva = (double)line.Value,
    };

    /// <summary><c>totalPlata_A = INT(baza1 + … + baza5 + tva1 + … + tva5)</c> (structura D301, nr. crt. 28).</summary>
    private static long Checksum(Declaratie301Type d) =>
        (long)Math.Floor(
            (decimal)d.Baza1 + (decimal)d.Baza2 + (decimal)d.Baza3 + (decimal)d.Baza4 + (decimal)d.Baza5 +
            (decimal)d.Tva1 + (decimal)d.Tva2 + (decimal)d.Tva3 + (decimal)d.Tva4 + (decimal)d.Tva5);

    private static string Amount(double value) => AccountingJson.Amount((decimal)value);
}
