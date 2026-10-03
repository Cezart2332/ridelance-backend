using Domain.Accounting;

namespace Application.Accounting;

/// <summary>Numele platformei, doar pentru afișare. Logica fiscală nu se ramifică niciodată după brand.</summary>
public static class PlatformLabels
{
    public static string Name(Platform? platform) => platform switch
    {
        Platform.Bolt => "Bolt",
        Platform.Uber => "Uber",
        _ => string.Empty,
    };
}
