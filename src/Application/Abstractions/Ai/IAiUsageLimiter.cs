namespace Application.Abstractions.Ai;

/// <summary>
/// Plafon pe cont pentru butoanele care cheamă un model plătit.
/// </summary>
/// <remarks>
/// Nu e o măsură de securitate, e una de cost: un buton „mai generează una" apăsat de cincizeci
/// de ori la rând nu ajută pe nimeni, dar se plătește. Numărătoarea stă în memoria procesului —
/// la mai multe instanțe fiecare are plafonul ei, ceea ce e suficient pentru ce apără aici.
/// </remarks>
public interface IAiUsageLimiter
{
    /// <summary>
    /// Consumă o unitate din plafonul contului. <c>false</c> înseamnă plafon atins.
    /// </summary>
    bool TryConsume(Guid userId, string feature, int maxCalls, TimeSpan window);
}
