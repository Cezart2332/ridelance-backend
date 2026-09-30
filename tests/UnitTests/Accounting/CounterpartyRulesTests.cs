using Application.Accounting;
using Application.Accounting.Ledger;
using Shouldly;
using Xunit;

namespace UnitTests.Accounting;

/// <summary>Clasificarea după contrapartidă (spec flux contabil R20, R40–R43).</summary>
public sealed class CounterpartyRulesTests
{
    private static readonly PfaIdentity Ion = new(["Ion Popescu", "Popescu Ion-Andrei"], new HashSet<string>(StringComparer.Ordinal) { "RO49••••0002" });
    private static readonly AccountingOptions Options = new();

    private static CounterpartyKind Classify(decimal amount, string? name, string? iban = null, string? details = null) =>
        CounterpartyRules.Classify(amount, name, iban, details, Ion, Options);

    [Theory]
    [InlineData("POPESCU ION")]
    [InlineData("Ion Popescu")]
    [InlineData("  popescu   ion ")]
    public void R40_OwnerNameInAnyOrderAndCaseIsTheOwner(string name) =>
        Classify(-2000, name).ShouldBe(CounterpartyKind.OwnerWithdrawal);

    [Fact]
    public void R41_MoneyFromTheOwnerIsAContribution() =>
        Classify(2000, "Popescu Ion").ShouldBe(CounterpartyKind.OwnerContribution);

    [Theory]
    [InlineData("Popescu Ion PFA")]
    [InlineData("POPESCU ION PERSOANA FIZICA AUTORIZATA")]
    public void R43_TheOwnersPfaNameIsAnotherPfaAccount(string name) =>
        Classify(-500, name).ShouldBe(CounterpartyKind.InternalTransfer);

    [Fact]
    public void R43_AnOwnIbanIsAnInternalTransferWhateverTheName() =>
        Classify(-500, "Oricine", "RO49 BTRL 0000 0000 0000 0002").ShouldBe(CounterpartyKind.InternalTransfer);

    [Theory]
    [InlineData("Trezoreria Operativă Brașov", null)]
    [InlineData("DGRFP BRASOV", null)]
    [InlineData("Plata", "RO12TREZ1315069XXX012345")]
    public void R42_TreasuryAndAnafAreTaxPayments(string name, string? iban) =>
        Classify(-1500, name, iban).ShouldBe(CounterpartyKind.Tax);

    [Fact]
    public void R20_PlatformPayoutIsASettlementOnlyWhenIncoming()
    {
        Classify(4650, "BOLT OPERATIONS OU").ShouldBe(CounterpartyKind.PlatformSettlement);
        Classify(-20, "BOLT OPERATIONS OU").ShouldBe(CounterpartyKind.None);
    }

    [Theory]
    [InlineData("Popescu")]
    [InlineData("Popescu Ion Auto SRL")]
    [InlineData("Ionescu Maria")]
    [InlineData("PANAFINA SRL")]
    public void Others_AreNotClassifiedByCounterparty(string name) =>
        Classify(-100, name).ShouldBe(CounterpartyKind.None);

    [Fact]
    public void Merchants_AreNormalizedWithoutDiacriticsAndLegalForms() =>
        CounterpartyRules.NormalizeMerchant("Spălătoria Țăndărei S.R.L.").ShouldBe("SPALATORIA TANDAREI");
}
