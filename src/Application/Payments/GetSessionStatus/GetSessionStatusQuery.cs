using Application.Abstractions.Messaging;

namespace Application.Payments.GetSessionStatus;

public sealed record GetSessionStatusQuery(string SessionId) : IQuery<SessionStatusResponse>;

/// <param name="Status">Starea formularului: <c>open</c>, <c>complete</c>, <c>expired</c>.</param>
/// <param name="PaymentStatus">Starea banilor: <c>paid</c>, <c>unpaid</c>, <c>no_payment_required</c>.</param>
/// <param name="Paid">
/// Singurul câmp pe care are voie să se bazeze un ecran de „plată reușită": <c>complete</c> fără
/// <c>paid</c> e un formular trimis, nu o plată.
/// </param>
public sealed record SessionStatusResponse(string Status, string? CustomerEmail, string PaymentStatus, bool Paid);
