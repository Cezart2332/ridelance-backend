namespace Infrastructure.Banking;

/// <summary>
/// Localizează fișierul .pem al aplicației Enable Banking când cheia nu e dată prin config:
/// caută în directorul binarelor și în directorul curent (unde stă în dev fișierul din Web.Api).
/// Enable Banking numește fișierul după Application ID, deci ID-ul poate fi derivat din nume.
/// </summary>
internal static class EnableBankingKeyFile
{
    public static string? Find()
    {
        foreach (string directory in (string[])[AppContext.BaseDirectory, Directory.GetCurrentDirectory()])
        {
            if (!Directory.Exists(directory))
            {
                continue;
            }

            string[] pemFiles = Directory.GetFiles(directory, "*.pem");
            if (pemFiles.Length > 0)
            {
                Array.Sort(pemFiles, StringComparer.Ordinal);
                return pemFiles[0];
            }
        }

        return null;
    }

    /// <summary>Application ID-ul derivat din numele fișierului, dacă numele e un GUID.</summary>
    public static string? ApplicationIdFromFileName(string path)
    {
        string name = Path.GetFileNameWithoutExtension(path);
        return Guid.TryParse(name, out Guid id) ? id.ToString() : null;
    }
}
