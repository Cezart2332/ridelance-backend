using RidelanceSpv.Core.Anaf;
using RidelanceSpv.Core.Server;

namespace RidelanceSpv.Core.Sync;

/// <summary>Rezultatul unei trimiteri, pentru ecranul aplicației.</summary>
/// <param name="Skipped">Serverul avea deja o trimitere în curs (alt calculator); nimic de făcut acum.</param>
public sealed record SyncReport(int Days, int Listed, int Sent, int RequestsSent, int Failed, string? Error, bool Skipped)
{
    public bool Succeeded => Error is null && !Skipped;
}

/// <summary>
/// O trimitere: serverul spune de câte zile să ceară (de la ultima trimitere reușită, maxim 60) și
/// ce cereri așteaptă; aplicația listează la SPV, descarcă doar mesajele pe care serverul nu le are,
/// le trimite, apoi trimite cererile. Nimic nu se păstrează local: o trimitere întreruptă (PC închis)
/// se reia pur și simplu data viitoare, iar serverul ignoră ce are deja.
/// </summary>
public sealed class SyncEngine(ISpvApi spv, IRidelanceApi server, string machine, string agentVersion)
{
    public async Task<SyncReport> RunAsync(IProgress<string>? progress, CancellationToken cancellationToken)
    {
        SpvRunStart run;
        try
        {
            run = await server.StartRunAsync(machine, agentVersion, cancellationToken);
        }
        catch (RidelanceException exception) when (exception.Code == "Spv.RunInProgress")
        {
            progress?.Report("O altă trimitere e în curs. Reîncerc la următoarea.");
            return new SyncReport(0, 0, 0, 0, 0, null, Skipped: true);
        }

        int listed = 0;
        int sent = 0;
        int requestsSent = 0;
        int failed = 0;
        try
        {
            progress?.Report($"Cer mesajele din ultimele {run.Days} zile.");
            IReadOnlyList<SpvListedMessage> messages = await spv.ListMessagesAsync(run.Days, cancellationToken);

            // Doar clienții RIDElance: certificatul poate avea drepturi și pe alte firme.
            var cuis = run.Cuis.ToHashSet(StringComparer.Ordinal);
            List<SpvListedMessage> ours = [.. messages.Where(m => cuis.Contains(Digits(m.Cif)))];
            listed = ours.Count;

            IReadOnlyList<string> newIds = ours.Count == 0 ? [] : await server.NewIdsAsync(run.RunId, [.. ours.Select(m => m.Id)], cancellationToken);
            var wanted = newIds.ToHashSet(StringComparer.Ordinal);
            progress?.Report(wanted.Count == 0 ? "Niciun mesaj nou." : $"{wanted.Count} mesaje noi.");

            foreach (SpvListedMessage message in ours.Where(m => wanted.Contains(m.Id)).OrderBy(m => m.CreatedAtUtc))
            {
                SpvDocument document;
                try
                {
                    document = await spv.DownloadAsync(message.Id, cancellationToken);
                }
                catch (SpvException exception)
                {
                    // Rămâne netrimis, deci serverul îl cere din nou data viitoare.
                    failed++;
                    progress?.Report($"Mesajul {message.Id}: {exception.Message}");
                    continue;
                }

                await server.SendMessageAsync(run.RunId, message, document, cancellationToken);
                sent++;
            }

            foreach (SpvRequestToSend request in run.Requests)
            {
                try
                {
                    string anafRequestId = await spv.RequestAsync(request.Type, request.Cui, request.Parameters, cancellationToken);
                    await server.ReportRequestAsync(run.RunId, request.Id, anafRequestId, null, cancellationToken);
                    requestsSent++;
                }
                catch (SpvException exception)
                {
                    failed++;
                    await server.ReportRequestAsync(run.RunId, request.Id, null, exception.Message, cancellationToken);
                    progress?.Report($"Cererea „{request.Type}” ({request.Cui}): {exception.Message}");
                }
            }

            await server.FinishRunAsync(run.RunId, null, cancellationToken);
            progress?.Report($"Trimis: {sent} mesaje, {requestsSent} cereri.");
            return new SyncReport(run.Days, listed, sent, requestsSent, failed, null, Skipped: false);
        }
        catch (Exception exception) when (exception is SpvException or RidelanceException)
        {
            await TryFinishAsync(run.RunId, exception.Message);
            progress?.Report(exception.Message);
            return new SyncReport(run.Days, listed, sent, requestsSent, failed, exception.Message, Skipped: false);
        }
    }

    /// <summary>Închiderea trimiterii cu eroare; dacă nici asta nu merge, serverul o eliberează singur după 10 minute.</summary>
    private async Task TryFinishAsync(Guid runId, string error)
    {
        try
        {
            await server.FinishRunAsync(runId, error, CancellationToken.None);
        }
        catch (RidelanceException)
        {
            // Serverul expiră trimiterea singur.
        }
    }

    private static string Digits(string value) => new([.. value.Where(char.IsDigit)]);
}
