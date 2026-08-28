using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Options;

namespace Infrastructure.Dossiers.Latex;

/// <summary>Compilează o sursă LaTeX și întoarce PDF-ul.</summary>
/// <remarks>
/// Fiecare compilare primește un director propriu, șters la final: motorul scrie pe lângă PDF un
/// `.log` și un `.aux`, iar două compilări simultane în același loc s-ar suprascrie.
/// </remarks>
internal sealed class LatexPdfCompiler(IOptions<LatexOptions> options)
{
    private const string JobName = "document";

    public async Task<byte[]> CompileAsync(string source, CancellationToken cancellationToken)
    {
        LatexOptions settings = options.Value;

        string workDir = Path.Combine(
            Path.GetTempPath(), "ridelance-latex", Guid.NewGuid().ToString("n"));
        string home = Path.Combine(workDir, "home");

        Directory.CreateDirectory(home);

        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(workDir, $"{JobName}.tex"), source, new UTF8Encoding(false), cancellationToken);

            int exitCode = await RunAsync(settings, workDir, home, cancellationToken);

            string pdf = Path.Combine(workDir, $"{JobName}.pdf");

            // Se verifică și codul de ieșire, nu doar existența fișierului: un motor oprit la
            // jumătate lasă în urmă un PDF cu paginile de până la eroare, iar un contract trunchiat
            // arată ca un contract întreg.
            if (exitCode != 0 || !File.Exists(pdf))
            {
                throw new InvalidOperationException(
                    $"Compilarea LaTeX a eșuat (cod {exitCode}). {ReadLog(workDir)}");
            }

            return await File.ReadAllBytesAsync(pdf, cancellationToken);
        }
        finally
        {
            try
            {
                Directory.Delete(workDir, recursive: true);
            }
            catch (IOException)
            {
                // Un temporar rămas nu strică documentul deja produs.
            }
        }
    }

    private static async Task<int> RunAsync(
        LatexOptions settings, string workDir, string home, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = settings.Engine,
            WorkingDirectory = workDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        // `-no-shell-escape` taie execuția de comenzi din document; restul opresc motorul la prima
        // eroare, în loc să aștepte la nesfârșit un răspuns de la o consolă pe care n-o are nimeni.
        startInfo.ArgumentList.Add("-no-shell-escape");
        startInfo.ArgumentList.Add("-halt-on-error");
        startInfo.ArgumentList.Add("-interaction=nonstopmode");
        startInfo.ArgumentList.Add($"-output-directory={workDir}");
        startInfo.ArgumentList.Add($"{JobName}.tex");

        // Motorul are nevoie de un loc unde să-și scrie cache-ul de fonturi; în container, `HOME`
        // e adesea nescriabil, iar fără asta prima compilare eșuează cu o eroare de font.
        startInfo.Environment["HOME"] = home;
        startInfo.Environment["XDG_CACHE_HOME"] = home;
        startInfo.Environment["TEXMFVAR"] = home;
        // Modul paranoic al TeX-ului: documentul nu poate citi și nu poate scrie în afara lui.
        startInfo.Environment["openin_any"] = "p";
        startInfo.Environment["openout_any"] = "p";

        using var process = new Process { StartInfo = startInfo };

        try
        {
            process.Start();
        }
        catch (Win32Exception exception)
        {
            throw new InvalidOperationException(
                $"Motorul LaTeX „{settings.Engine}” nu a putut fi pornit. " +
                "Documentele de închiriere se generează cu XeLaTeX; instalează-l pe mașina care rulează API-ul.",
                exception);
        }

        // Ieșirea se citește chiar dacă n-o folosim: un motor care umple canalul fără să fie citit
        // se blochează în loc să se termine.
        Task<string> output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> error = process.StandardError.ReadToEndAsync(cancellationToken);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(settings.TimeoutSeconds));

        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            process.Kill(entireProcessTree: true);
            throw new InvalidOperationException(
                $"Compilarea LaTeX a depășit {settings.TimeoutSeconds} secunde.");
        }

        await Task.WhenAll(output, error);

        return process.ExitCode;
    }

    /// <summary>Partea din jurnal care spune ce a mers prost.</summary>
    /// <remarks>
    /// Doar rândurile de eroare, nu tot jurnalul: acesta conține documentul reconstituit, adică
    /// numele, adresa și actul de identitate ale chiriașului, care n-au ce căuta în loguri.
    /// </remarks>
    private static string ReadLog(string workDir)
    {
        string path = Path.Combine(workDir, $"{JobName}.log");

        if (!File.Exists(path))
        {
            return "Nu s-a scris niciun jurnal.";
        }

        try
        {
            string[] complaints = [.. File.ReadLines(path).Where(l => l.StartsWith('!')).Take(5)];

            return complaints.Length > 0 ? string.Join(" ", complaints) : "Jurnalul nu conține erori.";
        }
        catch (IOException)
        {
            return "Jurnalul nu a putut fi citit.";
        }
    }
}
