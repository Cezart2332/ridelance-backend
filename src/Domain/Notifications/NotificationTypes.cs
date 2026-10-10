namespace Domain.Notifications;

public static class NotificationTypes
{
    public const string RecurringDocumentation = "RecurringDocumentation";
    public const string TaxThreshold = "TaxThreshold";
    public const string ChatRoomMessage = "ChatRoomMessage";
    public const string PfaStatusUpdate = "PfaStatusUpdate";
    public const string OnboardingStarted = "OnboardingStarted";
    public const string OnboardingSectionUpdate = "OnboardingSectionUpdate";
    public const string OnboardingStepAwaitingAdmin = "OnboardingStepAwaitingAdmin";
    public const string OnboardingStepUpdate = "OnboardingStepUpdate";
    public const string DocumentUploaded = "DocumentUploaded";
    public const string DocumentAiCheck = "DocumentAiCheck";
    public const string DocumentStatusUpdate = "DocumentStatusUpdate";
    public const string PaymentConfirmed = "PaymentConfirmed";
    public const string DocumentExpiringSoon = "DocumentExpiringSoon";
    public const string MonthProcessed = "MonthProcessed";
    public const string FleetAccountConfigured = "FleetAccountConfigured";

    /// <summary>O firmă a adăugat sau a modificat un anunț: adminul are o validare de făcut.</summary>
    public const string CarListingReview = "CarListingReview";
    public const string BankConnection = "BankConnection";

    /// <summary>Profilul fiscal anual: reamintiri către PFA și anunțul de completare către contabil.</summary>
    public const string FiscalProfile = "FiscalProfile";

    /// <summary>Notificare scrisă de contabil (sau admin) pentru clientul PFA.</summary>
    public const string AccountantMessage = "AccountantMessage";
}
