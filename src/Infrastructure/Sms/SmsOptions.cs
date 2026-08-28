namespace Infrastructure.Sms;

/// <summary>
/// Contul de la furnizorul de SMS. Secțiunea <c>Sms</c> din configurație.
/// </summary>
/// <remarks>
/// Fără ele, trimiterea eșuează explicit, cu un mesaj care spune că nu e configurată — nu tăcut
/// cu succes. Un cod de confirmare despre care sistemul crede că a plecat, dar n-a plecat, e mai
/// rău decât unul care n-a plecat și se știe.
/// </remarks>
public sealed class SmsOptions
{
    public const string SectionName = "Sms";

    public string? AccountSid { get; set; }

    public string? AuthToken { get; set; }

    /// <summary>Numărul sau alfanumericul de la care pleacă mesajul.</summary>
    public string? From { get; set; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(AccountSid)
        && !string.IsNullOrWhiteSpace(AuthToken)
        && !string.IsNullOrWhiteSpace(From);
}
