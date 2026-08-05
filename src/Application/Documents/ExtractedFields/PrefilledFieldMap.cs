using System.Text.Json;

namespace Application.Documents.ExtractedFields;

/// <summary>
/// Evidența „de unde vine fiecare câmp" din dosarul de înființare: ce a completat OCR-ul și ce
/// a schimbat userul cu mâna. Serveşte două scopuri concrete:
///
/// 1. **UI** — câmpurile precompletate poartă un indicator „completat automat din CI".
/// 2. **Idempotență** — o reîncărcare a cărții de identitate nu are voie să suprascrie o valoare
///    pe care userul a corectat-o deja; inferența poate greși, omul nu se contrazice de două ori.
/// </summary>
internal sealed class PrefilledFieldMap
{
    private sealed record Entry(bool Prefilled, bool ManuallyEdited);

    private readonly Dictionary<string, Entry> _entries;

    private PrefilledFieldMap(Dictionary<string, Entry> entries) => _entries = entries;

    public static PrefilledFieldMap Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new PrefilledFieldMap(new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase));
        }

        try
        {
            Dictionary<string, Entry>? parsed = JsonSerializer.Deserialize<Dictionary<string, Entry>>(json);
            return new PrefilledFieldMap(
                parsed is null
                    ? new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, Entry>(parsed, StringComparer.OrdinalIgnoreCase));
        }
        catch (JsonException)
        {
            // Un JSON stricat nu are voie să blocheze onboardingul; repornim evidența.
            return new PrefilledFieldMap(new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase));
        }
    }

    /// <summary>
    /// Harta goală, pentru datele care nu trec niciodată prin OCR (sediul social, proprietarii):
    /// maparea cere una, dar acolo nu are ce marca.
    /// </summary>
    public static PrefilledFieldMap Untracked() => Parse(null);

    public bool IsPrefilled(string field) => _entries.TryGetValue(field, out Entry? e) && e.Prefilled;

    public bool IsManuallyEdited(string field) => _entries.TryGetValue(field, out Entry? e) && e.ManuallyEdited;

    public void MarkPrefilled(string field)
    {
        _entries.TryGetValue(field, out Entry? existing);
        _entries[field] = new Entry(true, existing?.ManuallyEdited ?? false);
    }

    public void MarkManuallyEdited(string field)
    {
        _entries.TryGetValue(field, out Entry? existing);
        _entries[field] = new Entry(existing?.Prefilled ?? false, true);
    }

    /// <summary>Câmpurile venite din OCR și neatinse de user — cele care poartă indicatorul în UI.</summary>
    public IReadOnlyCollection<string> PrefilledUntouched() =>
        _entries.Where(kv => kv.Value.Prefilled && !kv.Value.ManuallyEdited).Select(kv => kv.Key).ToList();

    public string Serialize() => JsonSerializer.Serialize(_entries);
}
