using Application.Abstractions.Dossiers;

namespace Infrastructure.Dossiers;

/// <summary>
/// Dosarele de onboarding: exact documentele încărcate de șofer, într-un singur PDF.
///
/// Aveau și o copertă generată de noi — antet RIDElance, solicitant, CUI, agenție, taxă. A ieșit
/// la cererea celui care le depune: la ghișeu se depun actele, iar datele de pe copertă sunt
/// oricum în formularul oficial. Rămânea o filă de aruncat înaintea fiecărui dosar.
///
/// Ce a mai rămas de făcut aici e lipirea, iar aceea e treaba lui <see cref="DossierAssembler"/> —
/// clasa asta e doar poarta prin care aplicația cere un dosar.
/// </summary>
internal sealed class ArrDossierGenerator : IDossierGenerator
{
    public byte[] GenerateArrDossier(ArrDossierData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return DossierAssembler.Assemble(data.IncludedDocuments, data.IsTest);
    }

    public byte[] GenerateVehicleDossier(VehicleDossierData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return DossierAssembler.Assemble(data.IncludedDocuments, data.IsTest);
    }
}
