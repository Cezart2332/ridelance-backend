namespace Application.Abstractions.Services;

public interface ILicensePlateDetectionService
{
    /// <summary>
    /// Pregătește o poză de mașină pentru anunț: o micșorează și o scoate ca JPEG. Cu
    /// <paramref name="blurPlates"/>, caută și numerele de înmatriculare și le blurează.
    /// </summary>
    /// <param name="imageStream">Poza încărcată.</param>
    /// <param name="blurPlates">
    /// Doar pentru mașinile cu opțiunea „număr ascuns” plătită. Fără ea, poza rămâne cum a făcut-o
    /// proprietarul: blurarea e un serviciu plătit, nu ceva ce i se face fără să fi cerut.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Poza procesată (JPEG).</returns>
    Task<byte[]> ProcessImageAsync(Stream imageStream, bool blurPlates, CancellationToken cancellationToken = default);
}
