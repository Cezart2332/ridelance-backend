namespace Application.SrlDashboard;

/// <param name="Severity">`danger`, `warning` sau `info` — decisă pe server, ca UI-ul să nu-și
/// inventeze propriile praguri.</param>
/// <param name="Target">Ruta din dashboard care rezolvă problema. Un item de atenție fără
/// destinație e o notificare, nu o sarcină.</param>
public sealed record AttentionItemDto(
    string Id,
    string Severity,
    string Title,
    string Detail,
    string Target);

public sealed record ActiveRentalRowDto(
    Guid Id,
    string CarLabel,
    string TenantName,
    DateTime StartAtUtc,
    DateTime EndAtUtc,
    long WeeklyRentBani,
    string Status);

public sealed record SrlHomeDto(
    int FleetSize,
    int PublishedCount,
    int RentedCount,
    int AvailableCount,
    int ActiveRentals,
    long MonthlyContractValueBani,
    int DocumentsExpiringSoon,
    int ScheduledMaintenance,
    List<AttentionItemDto> Attention,
    List<ActiveRentalRowDto> ActiveRentalRows);
