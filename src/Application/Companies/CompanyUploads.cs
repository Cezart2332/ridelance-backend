namespace Application.Companies;

/// <summary>
/// Ștergerea fișierelor pe care le-am scris noi în <c>uploads/companies</c> — logo și cover.
/// </summary>
/// <remarks>
/// Verificarea prefixului nu e paranoia: valoarea vine din baza de date, dar tot ea ar deveni
/// calea unui <c>File.Delete</c>. Un prefix greșit acolo ar transforma o înlocuire de logo într-o
/// ștergere de fișier oarecare de pe server.
/// </remarks>
internal static class CompanyUploads
{
    public static void DeleteIfOurs(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !url.StartsWith("/uploads/companies/", StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            string path = Path.Combine("uploads", "companies", Path.GetFileName(url));
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // Un fișier orfan pe disc nu merită să rupă salvarea profilului.
        }
        catch (UnauthorizedAccessException)
        {
            // Idem: lipsa drepturilor pe fișierul vechi nu invalidează pe cel nou.
        }
    }
}
