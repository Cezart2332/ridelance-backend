using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Infrastructure.Banking;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace UnitTests.Banking;

/// <summary>
/// Cum ajunge certificatul mTLS din configurare în memorie.
///
/// Testul păzește drumul de deploy, nu criptografia: un PEM are linii, iar interfețele de
/// configurare le tratează fiecare altfel — unele le păstrează, altele le transformă în „\n"
/// literal, iar cine vrea să scape de problemă lipește tot blocul în base64. Fără toleranța asta,
/// un certificat perfect valid, pus într-un câmp care nu ține newline-uri, s-ar manifesta doar ca
/// „integrarea nu este configurată", fără niciun indiciu de unde vine.
/// </summary>
public sealed class SmartAccountsCertificateTests
{
    [Fact]
    public void PlainPem_IsAccepted()
    {
        (string cert, string key) = SelfSigned();

        Load(cert, key).ShouldNotBeNull();
    }

    [Fact]
    public void Base64EncodedPem_IsAccepted()
    {
        (string cert, string key) = SelfSigned();

        Load(Base64(cert), Base64(key)).ShouldNotBeNull();
    }

    [Fact]
    public void PemWithEscapedNewlines_IsAccepted()
    {
        (string cert, string key) = SelfSigned();

        Load(Escape(cert), Escape(key)).ShouldNotBeNull();
    }

    [Fact]
    public void Garbage_LeavesTheIntegrationUnconfiguredInsteadOfThrowing()
    {
        // Un certificat stricat nu trebuie să doboare pornirea: restul platformei merge mai
        // departe și doar open bankingul se raportează neconfigurat.
        var certificate = new SmartAccountsCertificate(Options.Create(new SmartAccountsOptions
        {
            CertificatePem = "nu-e-un-certificat",
            PrivateKeyPem = "nici-asta",
        }));

        certificate.IsPresent.ShouldBeFalse();
        certificate.Value.ShouldBeNull();
    }

    [Fact]
    public void NothingConfigured_IsNotAnError()
    {
        var certificate = new SmartAccountsCertificate(Options.Create(new SmartAccountsOptions()));

        certificate.IsPresent.ShouldBeFalse();
    }

    private static X509Certificate2? Load(string certificatePem, string privateKeyPem) =>
        new SmartAccountsCertificate(Options.Create(new SmartAccountsOptions
        {
            CertificatePem = certificatePem,
            PrivateKeyPem = privateKeyPem,
        })).Value;

    private static (string Certificate, string PrivateKey) SelfSigned()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=ridelance-test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        using X509Certificate2 certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(1));

        return (certificate.ExportCertificatePem(), rsa.ExportPkcs8PrivateKeyPem());
    }

    private static string Base64(string pem) => Convert.ToBase64String(Encoding.UTF8.GetBytes(pem));

    private static string Escape(string pem) => pem.Replace("\n", "\\n", StringComparison.Ordinal);
}
