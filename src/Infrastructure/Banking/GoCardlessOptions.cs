namespace Infrastructure.Banking;

public sealed class GoCardlessOptions
{
    public const string SectionName = "GoCardless";

    /// <summary>Secret ID din portalul GoCardless Bank Account Data (User secrets).</summary>
    public string SecretId { get; set; } = string.Empty;

    /// <summary>Secret Key din portalul GoCardless Bank Account Data (User secrets).</summary>
    public string SecretKey { get; set; } = string.Empty;

    public string BaseUrl { get; set; } = "https://bankaccountdata.gocardless.com/api/v2";

    /// <summary>
    /// Când e true, instituția de test "Sandbox Finance" apare în lista de bănci.
    /// GoCardless nu are un host separat de sandbox — instituția de test trăiește pe API-ul de producție.
    /// </summary>
    public bool UseSandboxInstitution { get; set; }
}
