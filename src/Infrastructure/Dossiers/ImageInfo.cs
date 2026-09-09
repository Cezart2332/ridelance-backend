namespace Infrastructure.Dossiers;

/// <summary>
/// Cât trebuie să știm despre o poză înainte s-o punem în dosar: cât e de mare și cum a ținut
/// telefonul aparatul când a făcut-o.
///
/// Orientarea contează mai mult decât pare. Un telefon ținut pe orizontală scrie pixelii tot în
/// portret și pune în EXIF „rotește la afișare"; browserele și galeriile respectă indicația, PDF-ul
/// nu are așa ceva. Așa ajungea un act fotografiat perfect să stea culcat în dosar — și, pentru că
/// pagina rămânea portret, mic și înconjurat de alb.
///
/// Se citește direct din anteturi, fără bibliotecă de imagini: ne trebuie trei numere, nu pixeli.
/// </summary>
internal sealed record ImageInfo(int Width, int Height, int Orientation)
{
    /// <summary>Poza trebuie întoarsă cu un sfert de tură — deci laturile se inversează la afișare.</summary>
    public bool QuarterTurn => Orientation is 5 or 6 or 7 or 8;

    public int DisplayWidth => QuarterTurn ? Height : Width;

    public int DisplayHeight => QuarterTurn ? Width : Height;

    public bool IsLandscape => DisplayWidth > DisplayHeight;

    /// <summary>
    /// Ce nu se poate citi se tratează ca portret nerotit: dosarul iese ca înainte, nu în cap.
    /// </summary>
    public static ImageInfo Read(byte[] content)
    {
        try
        {
            if (content.Length > 8 && content[0] == 0x89 && content[1] == 0x50)
            {
                return ReadPng(content);
            }

            if (content.Length > 4 && content[0] == 0xFF && content[1] == 0xD8)
            {
                return ReadJpeg(content);
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Un antet ciudat nu are voie să pice generarea dosarului.
        }

        return new ImageInfo(0, 0, 1);
    }

    /// <summary>IHDR e mereu primul chunk, imediat după semnătura de 8 octeți.</summary>
    private static ImageInfo ReadPng(byte[] b) =>
        new(ReadInt32(b, 16), ReadInt32(b, 20), 1);

    private static ImageInfo ReadJpeg(byte[] b)
    {
        int width = 0;
        int height = 0;
        int orientation = 1;

        for (int i = 2; i < b.Length - 9;)
        {
            if (b[i] != 0xFF)
            {
                i++;
                continue;
            }

            byte marker = b[i + 1];
            int length = ReadUInt16(b, i + 2);

            // SOF0..SOF15, fără markerii de tabele care împart același interval.
            if (marker >= 0xC0 && marker <= 0xCF && marker != 0xC4 && marker != 0xC8 && marker != 0xCC)
            {
                height = ReadUInt16(b, i + 5);
                width = ReadUInt16(b, i + 7);
                break;
            }

            if (marker == 0xE1 && orientation == 1)
            {
                orientation = ReadExifOrientation(b, i + 4, length - 2);
            }

            i += 2 + length;
        }

        return new ImageInfo(width, height, orientation);
    }

    /// <summary>
    /// Eticheta 0x0112 din primul IFD. Blocul are propria endianness, declarată în primii doi
    /// octeți — de aceea nu se poate citi cu un singur set de funcții.
    /// </summary>
    private static int ReadExifOrientation(byte[] b, int start, int length)
    {
        if (length < 14 || start + length > b.Length)
        {
            return 1;
        }

        if (b[start] != 'E' || b[start + 1] != 'x' || b[start + 2] != 'i' || b[start + 3] != 'f')
        {
            return 1;
        }

        int tiff = start + 6;
        bool littleEndian = b[tiff] == 'I' && b[tiff + 1] == 'I';

        int ifd = tiff + (int)ReadUInt32(b, tiff + 4, littleEndian);
        if (ifd + 2 > b.Length)
        {
            return 1;
        }

        int entries = ReadUInt16(b, ifd, littleEndian);

        for (int e = 0; e < entries; e++)
        {
            int entry = ifd + 2 + e * 12;
            if (entry + 12 > b.Length)
            {
                break;
            }

            if (ReadUInt16(b, entry, littleEndian) == 0x0112)
            {
                int value = ReadUInt16(b, entry + 8, littleEndian);
                return value is >= 1 and <= 8 ? value : 1;
            }
        }

        return 1;
    }

    private static int ReadUInt16(byte[] b, int offset) => (b[offset] << 8) | b[offset + 1];

    private static int ReadUInt16(byte[] b, int offset, bool littleEndian) =>
        littleEndian ? b[offset] | (b[offset + 1] << 8) : (b[offset] << 8) | b[offset + 1];

    private static uint ReadUInt32(byte[] b, int offset, bool littleEndian) => littleEndian
        ? (uint)(b[offset] | (b[offset + 1] << 8) | (b[offset + 2] << 16) | (b[offset + 3] << 24))
        : (uint)((b[offset] << 24) | (b[offset + 1] << 16) | (b[offset + 2] << 8) | b[offset + 3]);

    private static int ReadInt32(byte[] b, int offset) =>
        (b[offset] << 24) | (b[offset + 1] << 16) | (b[offset + 2] << 8) | b[offset + 3];
}
