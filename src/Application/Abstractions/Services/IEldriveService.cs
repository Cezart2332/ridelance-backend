using SharedKernel;

namespace Application.Abstractions.Services;

/// <summary>Ce întoarce Eldrive la o invitație nouă.</summary>
/// <param name="InviteId">ID-ul invitației, cu care se șterge ulterior.</param>
/// <param name="Status">Starea la Eldrive: <c>accepted</c> dacă adresa are deja cont.</param>
/// <param name="EldriveUserId">Utilizatorul Eldrive, când invitația e acceptată.</param>
public sealed record EldriveInviteResult(long InviteId, string Status, long? EldriveUserId);

/// <summary>
/// API-ul public Eldrive pentru invitațiile de partener: clienții RIDElance intră în contul de
/// partener și primesc tarifele negociate.
/// </summary>
public interface IEldriveService
{
    Task<Result<EldriveInviteResult>> InviteAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>Scoate invitația. O invitație pe care Eldrive n-o mai are e deja scoasă: succes.</summary>
    Task<Result> DeleteInviteAsync(long inviteId, CancellationToken cancellationToken = default);
}
