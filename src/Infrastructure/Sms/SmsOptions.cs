namespace Infrastructure.Sms;

/// <summary>
/// Contul de la Vonage. Secțiunea <c>Sms</c> din configurație.
/// </summary>
/// <remarks>
/// Fără ele, trimiterea eșuează explicit, cu un mesaj care spune că nu e configurată — nu tăcut
/// cu succes. Un cod de confirmare despre care sistemul crede că a plecat, dar n-a plecat, e mai
/// rău decât unul care n-a plecat și se știe.
/// </remarks>
public sealed class SmsOptions
{
    public const string SectionName = "Sms";

    /// <summary>Cheia din tabloul de bord Vonage.</summary>
    public string? ApiKey { get; set; }

    public string? ApiSecret { get; set; }

    /// <summary>
    /// Expeditorul afișat pe telefon: un număr în format internațional sau un nume.
    /// </summary>
    /// <remarks>
    /// În România, un expeditor alfanumeric („RIDElance") trebuie înregistrat în prealabil la
    /// operatori prin Vonage; până atunci, mesajele trimise cu el pot fi respinse sau livrate cu
    /// alt expeditor. Un număr Vonage funcționează fără înregistrare.
    /// </remarks>
    public string? From { get; set; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ApiKey)
        && !string.IsNullOrWhiteSpace(ApiSecret)
        && !string.IsNullOrWhiteSpace(From);
}
