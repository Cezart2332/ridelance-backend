using System.Text.Json.Serialization;
using Domain.PfaRegistrations;
using SharedKernel;

namespace Domain.Accounting;

/// <summary>
/// Cheia aplicației desktop RIDElance SPV. Aplicația folosește stickul împuternicitului pentru
/// SPV (SPVWS2 cere certificatul la fiecare conexiune, deci serverul nu poate intra singur) și
/// trimite mesajele aici, autentificată cu această cheie. Se păstrează doar hash-ul cheii.
/// </summary>
public sealed class SpvAgentKey : Entity, IAccountingRecord
{
    public Guid Id { get; set; }

    /// <summary>Adminul pentru care s-a emis cheia.</summary>
    public Guid UserId { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Primele caractere, ca să se recunoască în listă (<c>rdl_spv_ab12…</c>).</summary>
    public string Prefix { get; set; } = string.Empty;

    /// <summary>SHA-256 al cheii întregi, hex.</summary>
    public string KeyHash { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? LastUsedAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
}

[JsonConverter(typeof(UpperSnakeCaseEnumConverter<SpvSyncRunStatus>))]
public enum SpvSyncRunStatus
{
    Running = 0,
    Completed = 1,
    Failed = 2,

    /// <summary>A rămas „în lucru” peste termen (PC închis în timpul trimiterii).</summary>
    Abandoned = 3,
}

/// <summary>
/// O trimitere a aplicației desktop. Cea mai recentă reușită dă intervalul următoarei (de la ea,
/// cu două zile de suprapunere, maximum 60); una „în lucru” blochează o a doua trimitere simultană.
/// </summary>
public sealed class SpvSyncRun : Entity, IAccountingRecord
{
    public Guid Id { get; set; }
    public Guid AgentKeyId { get; set; }
    public SpvSyncRunStatus Status { get; set; }
    public string Machine { get; set; } = string.Empty;
    public string AgentVersion { get; set; } = string.Empty;

    /// <summary>Câte zile a cerut la <c>listaMesaje</c>.</summary>
    public int Days { get; set; }

    public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Până când ține blocarea; se prelungește la fiecare apel al aplicației.</summary>
    public DateTime LeaseUntilUtc { get; set; }
    public DateTime? FinishedAtUtc { get; set; }
    public int Listed { get; set; }
    public int Received { get; set; }
    public int RequestsSent { get; set; }
    public string? Error { get; set; }
}

[JsonConverter(typeof(UpperSnakeCaseEnumConverter<SpvMessageStatus>))]
public enum SpvMessageStatus
{
    /// <summary>Asociat și procesat automat (de ex. recipisa legată de declarație).</summary>
    Processed = 0,

    /// <summary>Nimic automat de făcut; e în lista clientului.</summary>
    New = 1,

    /// <summary>Nu s-a putut asocia sau procesa singur: apare la excepții.</summary>
    NeedsAttention = 2,
}

/// <summary>Un mesaj din SPV (recipisă, notificare, decizie, răspuns la o cerere), cu documentul lui.</summary>
public sealed class SpvMessage : Entity, IAccountingRecord
{
    public Guid Id { get; set; }

    /// <summary><c>id</c> din <c>listaMesaje</c>; unic la ANAF.</summary>
    public string AnafMessageId { get; set; } = string.Empty;

    /// <summary>CIF-ul mesajului, cum vine de la ANAF.</summary>
    public string Cif { get; set; } = string.Empty;

    /// <summary>PFA-ul găsit după CIF; <c>null</c> dacă niciun client nu are acel CUI.</summary>
    public Guid? PfaRegistrationId { get; set; }

    /// <summary><c>tip</c>: <c>RECIPISA</c>, <c>NOTIFICARE</c>, <c>DECIZIE</c>, <c>RASPUNS SOLICITARE</c>…</summary>
    public string Type { get; set; } = string.Empty;
    public DateTime AnafCreatedAtUtc { get; set; }
    public string? RequestId { get; set; }
    public string? Details { get; set; }

    public Guid? DocumentId { get; set; }

    /// <summary>SHA-256 al documentului: același fișier nu se păstrează de două ori.</summary>
    public string? FileHash { get; set; }

    public SpvMessageStatus Status { get; set; }

    /// <summary>Ce s-a făcut automat sau de ce nu (<c>Recipisă D100 08.2026</c>).</summary>
    public string? Note { get; set; }

    public Guid? DeclarationVersionId { get; set; }
    public Guid? SpvRequestId { get; set; }
    public DateTime? ReadAtUtc { get; set; }
    public DateTime ReceivedAtUtc { get; set; } = DateTime.UtcNow;

    public PfaRegistration? PfaRegistration { get; set; }
}

[JsonConverter(typeof(UpperSnakeCaseEnumConverter<SpvRequestStatus>))]
public enum SpvRequestStatus
{
    /// <summary>În coadă: pleacă la următoarea trimitere a aplicației desktop.</summary>
    Queued = 0,

    /// <summary>Preluată de aplicație; dacă nu se confirmă în termen, revine în coadă.</summary>
    Sending = 1,

    /// <summary>ANAF a primit-o (<c>id_solicitare</c>); răspunsul vine ca mesaj SPV.</summary>
    Sent = 2,

    Answered = 3,
    Failed = 4,
}

/// <summary>O cerere SPV (<c>cerere?tip=…</c>): Vector fiscal, Obligații de plată, Fișa rol…</summary>
public sealed class SpvRequest : Entity, IAccountingRecord
{
    public Guid Id { get; set; }
    public Guid PfaRegistrationId { get; set; }
    public string Cui { get; set; } = string.Empty;

    /// <summary>Tipul ANAF, exact (<c>VECTOR FISCAL</c>, <c>Obligatii de plata</c>).</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>JSON: parametrii în afară de <c>tip</c> și <c>cui</c> (<c>{"an":"2026"}</c>).</summary>
    public string ParametersJson { get; set; } = "{}";

    public SpvRequestStatus Status { get; set; }
    public Guid RequestedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ClaimedAtUtc { get; set; }
    public Guid? ClaimedByRunId { get; set; }
    public DateTime? SentAtUtc { get; set; }

    /// <summary><c>id_solicitare</c> primit de la ANAF; răspunsul are același id.</summary>
    public string? AnafRequestId { get; set; }
    public Guid? AnswerMessageId { get; set; }
    public string? Error { get; set; }

    public PfaRegistration PfaRegistration { get; set; } = null!;
}
