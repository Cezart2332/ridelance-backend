using Domain.Users;
using SharedKernel;

namespace Domain.Eldrive;

/// <summary>
/// Invitația prin care un client RIDElance intră în contul de partener Eldrive (partnerId-ul
/// nostru). Eldrive o identifică prin <see cref="EldriveInviteId"/>; cu el se și șterge, când
/// clientul renunță la abonament.
/// </summary>
/// <remarks>
/// Rândul rămâne după ștergere, cu <see cref="RemovedAtUtc"/> completat: e evidența cui a avut
/// acces și până când. Un client are cel mult o invitație activă.
/// </remarks>
public sealed class EldriveInvite : Entity
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }

    /// <summary>Adresa invitată — contul Eldrive, care poate diferi de emailul RIDElance.</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>ID-ul invitației la Eldrive (<c>data.id</c>). Necesar pentru ștergere.</summary>
    public long EldriveInviteId { get; set; }

    /// <summary>ID-ul utilizatorului Eldrive, când invitația e deja acceptată.</summary>
    public long? EldriveUserId { get; set; }

    /// <summary>Starea la Eldrive în momentul invitației: <c>accepted</c>, <c>pending</c>…</summary>
    public string Status { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }
    public DateTime? RemovedAtUtc { get; set; }
    public Guid? RemovedByUserId { get; set; }

    public User User { get; set; } = null!;
}
