using System.Globalization;
using System.Text;
using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.PfaRegistrations;
using Domain.Documents;
using Domain.PfaRegistrations;
using Domain.Uber;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Uber;

public sealed record UberCsvUpload(string FileName, string Content);

public sealed record ImportUberCsvCommand(
    IReadOnlyList<UberCsvUpload> Files,
    int? Year,
    int? Month) : ICommand<UberDashboardResponse>;

public sealed record UberImportDto(
    Guid Id,
    int Year,
    int Month,
    string FileType,
    string FileName,
    DateTime ImportedAtUtc,
    decimal NetEarnings,
    decimal GrossEarnings,
    decimal CashCollected,
    decimal Commission,
    int Trips,
    double Kilometers,
    double OnlineHours,
    double RideHours);

public sealed record UberStatsDto(
    decimal NetEarnings,
    decimal GrossEarnings,
    decimal CashCollected,
    decimal Commission,
    int Trips,
    double Kilometers,
    double OnlineHours,
    double RideHours);

public sealed record UberDashboardResponse(
    string Period,
    int? Year,
    int? Month,
    UberStatsDto Stats,
    IReadOnlyList<UberImportDto> Imports);

internal sealed class ImportUberCsvCommandHandler(
    IApplicationDbContext context,
    IUserContext userContext) : ICommandHandler<ImportUberCsvCommand, UberDashboardResponse>
{
    public async Task<Result<UberDashboardResponse>> Handle(
        ImportUberCsvCommand command,
        CancellationToken cancellationToken)
    {
        if (command.Files.Count is < 1 or > 3)
        {
            return Result.Failure<UberDashboardResponse>(
                Error.Problem("Uber.InvalidFileCount", "Încarcă între 1 și 3 fișiere CSV Uber."));
        }

        if (command.Month is < 1 or > 12)
        {
            return Result.Failure<UberDashboardResponse>(
                Error.Problem("Uber.InvalidMonth", "Luna importului trebuie să fie între 1 și 12."));
        }

        PfaRegistration? pfa = await context.PfaRegistrations
            .Where(p => p.UserId == userContext.UserId)
            .OrderByDescending(p => p.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (pfa is null)
        {
            return Result.Failure<UberDashboardResponse>(
                Error.NotFound("Pfa.NotFound", "Nu există o înregistrare PFA pentru acest cont."));
        }

        List<UberCsvImport> imports = [];
        foreach (UberCsvUpload file in command.Files)
        {
            Result<ParsedUberCsv> parsedResult = UberCsvParser.Parse(file.FileName, file.Content);
            if (parsedResult.IsFailure)
            {
                return Result.Failure<UberDashboardResponse>(parsedResult.Error);
            }

            ParsedUberCsv parsed = parsedResult.Value;
            int year = command.Year ?? parsed.Year ?? DateTime.UtcNow.Year;
            int month = command.Month ?? parsed.Month ?? DateTime.UtcNow.Month;

            bool duplicate = await context.UberCsvImports.AnyAsync(
                i => i.PfaRegistrationId == pfa.Id
                    && i.Year == year
                    && i.Month == month
                    && i.FileType == parsed.FileType
                    && i.FileName == file.FileName,
                cancellationToken);

            if (duplicate)
            {
                return Result.Failure<UberDashboardResponse>(
                    Error.Conflict("Uber.DuplicateImport", $"Fișierul {file.FileName} a fost deja importat pentru perioada selectată."));
            }

            imports.Add(new UberCsvImport
            {
                Id = Guid.NewGuid(),
                UserId = userContext.UserId,
                PfaRegistrationId = pfa.Id,
                Year = year,
                Month = month,
                FileType = parsed.FileType,
                FileName = file.FileName,
                ImportedAtUtc = DateTime.UtcNow,
                NetEarnings = parsed.NetEarnings,
                GrossEarnings = parsed.GrossEarnings,
                CashCollected = parsed.CashCollected,
                Commission = parsed.Commission,
                Trips = parsed.Trips,
                Kilometers = parsed.Kilometers,
                OnlineHours = parsed.OnlineHours,
                RideHours = parsed.RideHours
            });
        }

        foreach (UberCsvImport import in imports)
        {
            context.UberCsvImports.Add(import);
        }

        await context.SaveChangesAsync(cancellationToken);

        foreach (var period in imports.Select(i => new { i.Year, i.Month }).Distinct())
        {
            await UpdateMonthlyUberIncomeAsync(pfa.Id, period.Year, period.Month, cancellationToken);
        }

        await context.SaveChangesAsync(cancellationToken);

        int responseYear = command.Year ?? imports[0].Year;
        int responseMonth = command.Month ?? imports[0].Month;
        return await UberDashboardProjector.GetDashboardAsync(
            context,
            pfa.Id,
            "month",
            responseYear,
            responseMonth,
            cancellationToken);
    }

    private async Task UpdateMonthlyUberIncomeAsync(Guid pfaId, int year, int month, CancellationToken ct)
    {
        PfaMonthlyIncome? income = await context.PfaMonthlyIncomes
            .SingleOrDefaultAsync(i => i.PfaRegistrationId == pfaId && i.Year == year && i.Month == month, ct);

        if (income is null)
        {
            income = new PfaMonthlyIncome
            {
                Id = Guid.NewGuid(),
                PfaRegistrationId = pfaId,
                Year = year,
                Month = month
            };
            context.PfaMonthlyIncomes.Add(income);
        }

        income.VenitUber = await context.UberCsvImports
            .Where(i => i.PfaRegistrationId == pfaId && i.Year == year && i.Month == month && i.FileType == UberCsvParser.Earnings)
            .SumAsync(i => i.NetEarnings, ct);

        decimal ytdGrossIncome = await context.PfaMonthlyIncomes
            .Where(i => i.PfaRegistrationId == pfaId && i.Year == year && i.Month != month)
            .SumAsync(i => i.VenitBolt + i.VenitUber, ct)
            + income.ComputePlatformIncome();

        decimal ytdExpenses = await context.DeductibleExpenses
            .AsNoTracking()
            .Where(e => e.PfaRegistrationId == pfaId && e.Year == year)
            .Join(
                context.Documents.AsNoTracking().Where(d => d.Status == DocumentStatus.Verified),
                e => e.DocumentId,
                d => d.Id,
                (e, _) => e.AmountRon ?? 0m)
            .SumAsync(ct);

        PfaTaxCalculator.TaxResult tax = PfaTaxCalculator.Compute(ytdGrossIncome, ytdExpenses, year);
        income.TaxeEstimate = tax.TotalTax;
        income.UpdatedAtUtc = DateTime.UtcNow;
        income.UpdatedByUserId = userContext.UserId;
    }
}

internal static class UberDashboardProjector
{
    public static async Task<UberDashboardResponse> GetDashboardAsync(
        IApplicationDbContext context,
        Guid pfaId,
        string period,
        int? year,
        int? month,
        CancellationToken ct)
    {
        IQueryable<UberCsvImport> query = context.UberCsvImports
            .AsNoTracking()
            .Where(i => i.PfaRegistrationId == pfaId);

        if (period == "month")
        {
            query = query.Where(i => i.Year == year && i.Month == month);
        }
        else if (period == "year")
        {
            query = query.Where(i => i.Year == year);
        }

        List<UberCsvImport> imports = await query
            .OrderByDescending(i => i.ImportedAtUtc)
            .ToListAsync(ct);

        UberStatsDto stats = new(
            imports.Where(i => i.FileType == UberCsvParser.Earnings).Sum(i => i.NetEarnings),
            imports.Where(i => i.FileType == UberCsvParser.Earnings).Sum(i => i.GrossEarnings),
            imports.Where(i => i.FileType == UberCsvParser.Earnings).Sum(i => i.CashCollected),
            imports.Where(i => i.FileType == UberCsvParser.Earnings).Sum(i => i.Commission),
            imports.Where(i => i.FileType == UberCsvParser.Hours).Sum(i => i.Trips),
            Math.Round(imports.Where(i => i.FileType == UberCsvParser.Trips).Sum(i => i.Kilometers), 1),
            Math.Round(imports.Where(i => i.FileType == UberCsvParser.Hours).Sum(i => i.OnlineHours), 2),
            Math.Round(imports.Where(i => i.FileType == UberCsvParser.Hours).Sum(i => i.RideHours), 2));

        return new UberDashboardResponse(
            period,
            year,
            period == "month" ? month : null,
            stats,
            imports.Select(Map).ToList());
    }

    private static UberImportDto Map(UberCsvImport import) => new(
        import.Id,
        import.Year,
        import.Month,
        import.FileType,
        import.FileName,
        import.ImportedAtUtc,
        import.NetEarnings,
        import.GrossEarnings,
        import.CashCollected,
        import.Commission,
        import.Trips,
        import.Kilometers,
        import.OnlineHours,
        import.RideHours);
}

internal sealed record ParsedUberCsv(
    string FileType,
    int? Year,
    int? Month,
    decimal NetEarnings = 0,
    decimal GrossEarnings = 0,
    decimal CashCollected = 0,
    decimal Commission = 0,
    int Trips = 0,
    double Kilometers = 0,
    double OnlineHours = 0,
    double RideHours = 0);

internal static class UberCsvParser
{
    public const string Earnings = "earnings";
    public const string Hours = "hours";
    public const string Trips = "trips";

    public static Result<ParsedUberCsv> Parse(string fileName, string content)
    {
        List<string[]> rows = ReadRows(content);
        if (rows.Count < 2)
        {
            return Result.Failure<ParsedUberCsv>(
                Error.Problem("Uber.InvalidCsv", $"Fișierul {fileName} nu conține rânduri CSV valide."));
        }

        string[] header = rows[0];
        string normalizedHeader = Normalize(string.Join(",", header));

        if (normalizedHeader.Contains("castiguri primite"))
        {
            return ParseEarnings(fileName, header, rows.Skip(1));
        }

        if (normalizedHeader.Contains("durata conectarii"))
        {
            return ParseHours(fileName, header, rows.Skip(1));
        }

        if (normalizedHeader.Contains("distanta cursei"))
        {
            return ParseTrips(fileName, header, rows.Skip(1));
        }

        return Result.Failure<ParsedUberCsv>(
            Error.Problem("Uber.UnknownCsv", $"Fișierul {fileName} nu pare să fie un CSV Uber acceptat."));
    }

    private static Result<ParsedUberCsv> ParseEarnings(string fileName, string[] header, IEnumerable<string[]> rows)
    {
        string[]? row = rows.FirstOrDefault(r => r.Length > 3 && Guid.TryParse(Cell(r, 0), out _));
        if (row is null)
        {
            return Invalid(fileName);
        }

        decimal net = ReadMoney(row, ExactColumn(header, "Câștiguri primite : Câștigurile tale"));
        decimal gross = ReadMoney(row, ExactColumn(header, "Câștiguri primite:Câștigurile tale:Tarif:Tarif"));
        decimal cash = Math.Abs(ReadMoney(row, ContainsColumn(header, "numerar incasat")));
        decimal commission = Math.Abs(ReadMoney(row, ContainsColumn(header, "taxa de servicii")));

        return new ParsedUberCsv(Earnings, null, null, net, gross, cash, commission);
    }

    private static Result<ParsedUberCsv> ParseHours(string fileName, string[] header, IEnumerable<string[]> rows)
    {
        string[]? row = rows.FirstOrDefault(r => r.Length > 5 && Guid.TryParse(Cell(r, 0), out _));
        if (row is null)
        {
            return Invalid(fileName);
        }

        return new ParsedUberCsv(
            Hours,
            null,
            null,
            Trips: ReadInt(row, ContainsColumn(header, "curse finalizate")),
            OnlineHours: ReadUberHours(Cell(row, ContainsColumn(header, "durata conectarii"))),
            RideHours: ReadUberHours(Cell(row, ContainsColumn(header, "durata cursei"))));
    }

    private static Result<ParsedUberCsv> ParseTrips(string fileName, string[] header, IEnumerable<string[]> rows)
    {
        int distanceIndex = ContainsColumn(header, "distanta cursei");
        int statusIndex = ContainsColumn(header, "starea cursei");
        int dateIndex = ContainsColumn(header, "ora la care a fost comandata cursa");
        int trips = 0;
        double km = 0;
        int? year = null;
        int? month = null;

        foreach (string[] row in rows.Where(r => r.Length > statusIndex && Guid.TryParse(Cell(r, 0), out _)))
        {
            if (year is null && DateTime.TryParse(Cell(row, dateIndex), CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime date))
            {
                year = date.Year;
                month = date.Month;
            }

            if (!Cell(row, statusIndex).Equals("completed", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            trips++;
            km += ReadDouble(row, distanceIndex);
        }

        if (trips == 0 && year is null)
        {
            return Invalid(fileName);
        }

        return new ParsedUberCsv(Trips, year, month, Trips: trips, Kilometers: km);
    }

    private static Result<ParsedUberCsv> Invalid(string fileName) =>
        Result.Failure<ParsedUberCsv>(Error.Problem("Uber.InvalidCsv", $"Fișierul {fileName} nu are date Uber valide."));

    private static List<string[]> ReadRows(string content)
    {
        using var reader = new StringReader(content);
        List<string[]> rows = [];
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                rows.Add(ParseLine(line));
            }
        }

        return rows;
    }

    private static string[] ParseLine(string line)
    {
        List<string> cells = [];
        var current = new System.Text.StringBuilder();
        bool quoted = false;

        int i = 0;
        while (i < line.Length)
        {
            char ch = line[i];
            if (ch == '"' && quoted && i + 1 < line.Length && line[i + 1] == '"')
            {
                current.Append('"');
                i += 2;
                continue;
            }
            else if (ch == '"')
            {
                quoted = !quoted;
            }
            else if (ch == ',' && !quoted)
            {
                cells.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(ch);
            }

            i++;
        }

        cells.Add(current.ToString());
        return cells.ToArray();
    }

    private static int ExactColumn(string[] header, string name)
    {
        string target = Normalize(name);
        for (int i = 0; i < header.Length; i++)
        {
            if (Normalize(header[i]) == target)
            {
                return i;
            }
        }

        return -1;
    }

    private static int ContainsColumn(string[] header, string text)
    {
        string target = Normalize(text);
        for (int i = 0; i < header.Length; i++)
        {
            if (Normalize(header[i]).Contains(target))
            {
                return i;
            }
        }

        return -1;
    }

    private static decimal ReadMoney(string[] row, int index) =>
        decimal.TryParse(Cell(row, index), NumberStyles.Number, CultureInfo.InvariantCulture, out decimal value)
            ? value
            : 0m;

    private static double ReadDouble(string[] row, int index) =>
        double.TryParse(Cell(row, index), NumberStyles.Number, CultureInfo.InvariantCulture, out double value)
            ? value
            : 0;

    private static int ReadInt(string[] row, int index) =>
        int.TryParse(Cell(row, index), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            ? value
            : 0;

    private static double ReadUberHours(string value)
    {
        string[] parts = value.Split(':', StringSplitOptions.TrimEntries);
        if (parts.Length != 3
            || !int.TryParse(parts[0], out int days)
            || !int.TryParse(parts[1], out int hours)
            || !int.TryParse(parts[2], out int minutes))
        {
            return 0;
        }

        return Math.Round(days * 24 + hours + minutes / 60.0, 2);
    }

    private static string Cell(string[] row, int index) =>
        index >= 0 && index < row.Length ? row[index].Trim().Trim('\ufeff') : string.Empty;

    private static string Normalize(string value)
    {
        string text = value.Trim().Trim('\ufeff').Replace('\u00a0', ' ').ToUpperInvariant().Normalize(NormalizationForm.FormD);
        Span<char> buffer = stackalloc char[text.Length];
        int length = 0;
        foreach (char ch in text)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            {
                buffer[length++] = ch;
            }
        }

        return new string(buffer[..length]).Normalize(NormalizationForm.FormC);
    }
}
