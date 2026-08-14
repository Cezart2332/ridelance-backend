using Application.Cars;
using Shouldly;
using Xunit;

namespace UnitTests.Cars;

/// <summary>
/// Deduplicarea vizualizărilor se bazează pe hash: dacă nu e stabil, fiecare refresh contorizează;
/// dacă e reversibil, am stocat de fapt adrese IP.
/// </summary>
public class VisitorFingerprintTests
{
    private const string Ip = "86.120.44.11";
    private const string Agent = "Mozilla/5.0 (Linux; Android 14)";
    private const string Salt = "salt-de-test";

    [Fact]
    public void Compute_IsStableForTheSameVisitor()
    {
        VisitorFingerprint.Compute(Ip, Agent, Salt)
            .ShouldBe(VisitorFingerprint.Compute(Ip, Agent, Salt));
    }

    [Fact]
    public void Compute_SeparatesDifferentAddresses()
    {
        VisitorFingerprint.Compute(Ip, Agent, Salt)
            .ShouldNotBe(VisitorFingerprint.Compute("86.120.44.12", Agent, Salt));
    }

    [Fact]
    public void Compute_SeparatesDifferentBrowsers()
    {
        VisitorFingerprint.Compute(Ip, Agent, Salt)
            .ShouldNotBe(VisitorFingerprint.Compute(Ip, "Mozilla/5.0 (Macintosh)", Salt));
    }

    [Fact]
    public void Compute_ChangesWithTheSalt()
    {
        // Fără asta, oricine poate verifica dacă un IP anume a văzut un anunț: hash-ul e ghicibil.
        VisitorFingerprint.Compute(Ip, Agent, Salt)
            .ShouldNotBe(VisitorFingerprint.Compute(Ip, Agent, "alt-salt"));
    }

    [Fact]
    public void Compute_DoesNotLeakTheAddress()
    {
        string hash = VisitorFingerprint.Compute(Ip, Agent, Salt);

        hash.ShouldNotContain(Ip);
        hash.Length.ShouldBe(64);
    }

    [Fact]
    public void Compute_HandlesMissingRequestData()
    {
        // Un client fără user-agent sau fără IP vizibil (proxy) nu are voie să arunce.
        VisitorFingerprint.Compute(null, null, Salt).Length.ShouldBe(64);
    }
}
