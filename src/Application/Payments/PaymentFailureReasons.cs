namespace Application.Payments;

/// <summary>
/// De ce n-a trecut o plată, pe înțelesul adminului și al clientului. Codul Stripe rămâne în
/// paranteză: cu el se caută mai departe, la bancă sau în documentația Stripe.
/// </summary>
public static class PaymentFailureReasons
{
    private static readonly Dictionary<string, string> Labels = new(StringComparer.OrdinalIgnoreCase)
    {
        // Banca a refuzat (decline_code)
        ["insufficient_funds"] = "Fonduri insuficiente",
        ["do_not_honor"] = "Refuzat de bancă",
        ["generic_decline"] = "Refuzat de bancă",
        ["card_declined"] = "Refuzat de bancă",
        ["call_issuer"] = "Refuzat de bancă — clientul trebuie să sune la bancă",
        ["try_again_later"] = "Banca a cerut reîncercare mai târziu",
        ["expired_card"] = "Card expirat",
        ["lost_card"] = "Card raportat pierdut",
        ["stolen_card"] = "Card raportat furat",
        ["pickup_card"] = "Card blocat de bancă",
        ["restricted_card"] = "Card restricționat de bancă",
        ["incorrect_cvc"] = "CVC greșit",
        ["invalid_cvc"] = "CVC greșit",
        ["incorrect_number"] = "Număr de card greșit",
        ["invalid_account"] = "Cont invalid",
        ["card_velocity_exceeded"] = "Limita cardului depășită",
        ["withdrawal_count_limit_exceeded"] = "Limita de tranzacții depășită",
        ["transaction_not_allowed"] = "Banca nu permite tranzacția",
        ["not_permitted"] = "Banca nu permite tranzacția",
        ["card_not_supported"] = "Card neacceptat pentru plăți recurente",
        ["currency_not_supported"] = "Cardul nu acceptă plăți în lei",
        ["fraudulent"] = "Refuzat ca suspect de fraudă",
        ["merchant_blacklist"] = "Refuzat de Stripe",
        ["security_violation"] = "Refuzat de bancă (securitate)",
        ["service_not_allowed"] = "Banca nu permite tranzacția",
        ["stop_payment_order"] = "Clientul a oprit plățile din bancă",
        ["revocation_of_authorization"] = "Clientul a retras acordul pentru plăți recurente",
        ["new_account_information_available"] = "Cardul a fost înlocuit de bancă",
        ["processing_error"] = "Eroare la procesarea plății",
        ["issuer_not_available"] = "Banca nu a răspuns",
        ["reenter_transaction"] = "Banca a cerut reîncercare",
        ["approve_with_id"] = "Banca nu a putut autoriza plata",

        // Stripe (code)
        ["authentication_required"] = "Banca cere confirmarea plății (3D Secure)",
        ["payment_intent_authentication_failure"] = "Confirmarea 3D Secure a eșuat",
        ["payment_method_not_available"] = "Metoda de plată nu e disponibilă",
        ["card_decline_rate_limit_exceeded"] = "Prea multe încercări refuzate pe card",
        ["payment_intent_payment_attempt_failed"] = "Plata a eșuat",
    };

    /// <summary>Codul care contează: cel al băncii, dacă există, altfel al Stripe.</summary>
    public static string? CodeOf(string? code, string? declineCode) =>
        string.IsNullOrWhiteSpace(declineCode) ? NullIfEmpty(code) : declineCode;

    /// <summary>„Fonduri insuficiente (insufficient_funds)”. Fără cod, mesajul Stripe ca atare.</summary>
    public static string Describe(string? code, string? declineCode, string? message)
    {
        string? key = CodeOf(code, declineCode);
        if (key is not null && Labels.TryGetValue(key, out string? label))
        {
            return $"{label} ({key})";
        }

        if (!string.IsNullOrWhiteSpace(message))
        {
            return key is null ? message.Trim() : $"{message.Trim()} ({key})";
        }

        return key is null ? "Motiv necunoscut" : $"Refuzat ({key})";
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
