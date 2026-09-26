using Anaf.D100.V2;
using Application.Abstractions.Anaf;
using Application.Accounting;
using Application.Accounting.Contracts;
using Domain.Accounting;

namespace Infrastructure.Accounting.Anaf;

/// <summary>Un mapper per declarație și per versiune de schemă (spec contabilitate B4).</summary>
internal interface IDeclarationXmlMapper
{
    DeclarationType Type { get; }

    /// <summary><see cref="AnafDeclarationSchema.Version"/> pentru care produce XML.</summary>
    string SchemaVersion { get; }

    byte[] Map(AnafDeclarationInput input);

    IReadOnlyList<ValidationMessage> Verify(AnafDeclarationInput input, byte[] xml);
}

/// <summary>
/// D100, namespace <c>mfp:anaf:dgti:d100:declaratie:v2</c>. O singură obligație: impozitul pe
/// comisioanele reținute de platformele nerezidente, cod 634, modelul de completare 1#
/// (<c>suma_plata = suma_dat</c>, fără sume anterioare). Sumele sunt în lei întregi.
/// </summary>
internal sealed class D100MapperV2 : IDeclarationXmlMapper
{
    public const string Namespace = "mfp:anaf:dgti:d100:declaratie:v2";

    /// <summary>Structura D100, poz. 16: „Impozit pe veniturile din comisioane obținute din România de persoane nerezidente”.</summary>
    public const string CommissionObligation = "634";

    /// <summary>Codul bugetar al obligației (<c>5503</c>), completat cu X până la 10 caractere.</summary>
    public const string BudgetCode = "5503XXXXXX";

    public DeclarationType Type => DeclarationType.D100;

    public string SchemaVersion => "v2-20220224";

    public byte[] Map(AnafDeclarationInput input)
    {
        (int month, int year) = AnafFormat.Period(input.Period);
        ulong amount = WholeLei(input.Amount);
        var obligation = new ObligatieType
        {
            CodOblig = IntListaCodObligSType.Item634,
            CodBugetar = BudgetCode,
            Scadenta = AnafFormat.Date(AnafFormat.DueDate(input.Period)),
            NrEvid = decimal.Parse(AnafFormat.EvidenceNumber($"10{CommissionObligation}01{AnafFormat.PeriodAndDue(input.Period)}0000"), System.Globalization.CultureInfo.InvariantCulture),
            SumaDat = amount,
            SumaDed = 0,
            SumaPlata = amount,
            SumaRest = 0,
        };

        var declaration = new Declaratie100Type
        {
            Luna = (byte)month,
            An = (ushort)year,
            DAnulare = 0,
            NumeDeclar = AnafFormat.Text(input.Taxpayer.DeclarantLastName),
            PrenumeDeclar = AnafFormat.Text(input.Taxpayer.DeclarantFirstName),
            FunctieDeclar = AnafFormat.Text(input.Taxpayer.DeclarantFunction),
            Cui = input.Taxpayer.Cui,
            Den = AnafFormat.Text(input.Taxpayer.Name),
            Adresa = AnafFormat.Text(input.Taxpayer.Address),
            TotalPlataA = Checksum([obligation]),
        };
        declaration.Obligatie.Add(obligation);
        return AnafFormat.Serialize(declaration, Namespace);
    }

    public IReadOnlyList<ValidationMessage> Verify(AnafDeclarationInput input, byte[] xml)
    {
        Declaratie100Type? declaration = AnafFormat.Deserialize<Declaratie100Type>(xml);
        if (declaration is null)
        {
            return [new ValidationMessage(null, "XML-ul D100 nu poate fi citit.")];
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

        if (declaration.Obligatie.Count != 1 || declaration.Obligatie[0].CodOblig != IntListaCodObligSType.Item634)
        {
            messages.Add(new ValidationMessage("cod_oblig", $"D100 trebuie să aibă o singură obligație, codul {CommissionObligation}."));
            return messages;
        }

        ObligatieType obligation = declaration.Obligatie[0];
        if (obligation.SumaDat != input.Amount)
        {
            messages.Add(new ValidationMessage(
                "suma_dat",
                $"Suma din XML ({obligation.SumaDat} lei) diferă de totalul declarației ({AccountingJson.Amount(input.Amount)} lei). " +
                "D100 se declară în lei întregi: rotunjirea D100 se setează în configurare (Accounting:DeclarationRounding)."));
        }

        if (obligation.SumaPlata != obligation.SumaDat || obligation.SumaDed != 0 || obligation.SumaRest != 0)
        {
            messages.Add(new ValidationMessage("suma_plata", "Modelul de completare 1#: suma de plată = suma datorată, fără sume deductibile sau de restituit."));
        }

        if (declaration.TotalPlataA != Checksum(declaration.Obligatie))
        {
            messages.Add(new ValidationMessage("totalPlata_A", $"Suma de control ({declaration.TotalPlataA}) nu e suma sumelor din obligații ({Checksum(declaration.Obligatie)})."));
        }

        string due = AnafFormat.Date(AnafFormat.DueDate(input.Period));
        if (obligation.Scadenta != due)
        {
            messages.Add(new ValidationMessage("scadenta", $"Scadența din XML ({obligation.Scadenta}) nu e {due}."));
        }

        string evidence = obligation.NrEvid.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (!AnafFormat.IsValidEvidenceNumber(evidence) || evidence[2..5] != CommissionObligation || evidence[7..17] != AnafFormat.PeriodAndDue(input.Period))
        {
            messages.Add(new ValidationMessage("nr_evid", $"Numărul de evidență a plății ({evidence}) nu e corect."));
        }

        return messages;
    }

    /// <summary><c>totalPlata_A</c> = suma tuturor sumelor din obligații (structura D100, 11a).</summary>
    private static ulong Checksum(IEnumerable<ObligatieType> obligations) =>
        obligations.Aggregate(0UL, (total, o) => total + (o.SumaDat ?? 0) + (o.SumaDed ?? 0) + (o.SumaPlata ?? 0) + (o.SumaRest ?? 0));

    private static ulong WholeLei(decimal amount) => (ulong)Math.Max(0, Math.Round(amount, 0, MidpointRounding.AwayFromZero));
}
