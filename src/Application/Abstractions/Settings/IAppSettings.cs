namespace Application.Abstractions.Settings;

/// <summary>
/// Acces cu cache scurt (60s) la parametrii comerciali/operaționali din tabelul app_settings.
/// Valorile sunt serializate JSON; se deserializează la tipul cerut.
/// </summary>
public interface IAppSettings
{
    /// <summary>Întoarce valoarea cheii, sau <paramref name="defaultValue"/> dacă lipsește / nu se poate deserializa.</summary>
    Task<T> GetAsync<T>(string key, T defaultValue, CancellationToken cancellationToken = default);
}
