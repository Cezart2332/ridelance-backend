namespace Domain.Banking;

public enum BankConnectionStatus
{
    /// <summary>Requisition created, user has not completed bank authorization yet.</summary>
    Created = 0,

    /// <summary>User started the authorization flow at the bank.</summary>
    Pending = 1,

    /// <summary>Bank accounts linked, transactions are syncing.</summary>
    Linked = 2,

    /// <summary>PSD2 consent expired — user must re-authorize.</summary>
    Expired = 3,

    /// <summary>Repeated sync failures or provider-side rejection.</summary>
    Error = 4,

    /// <summary>User disconnected the bank; historical transactions are kept.</summary>
    Revoked = 5,
}
