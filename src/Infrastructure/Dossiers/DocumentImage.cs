using OpenCvSharp;

namespace Infrastructure.Dossiers;

/// <summary>
/// Aduce o poză de act la forma în care se poate depune: doar actul, drept, pe toată pagina.
///
/// Ce intră azi în dosar nu e un scan, e o fotografie: buletinul pe masa de lemn, cu actul pe
/// jumătate din cadru, sau diploma pusă pe geamul scannerului, cu o treime bandă albă lângă ea.
/// Puse pe A4, ies mici și înconjurate de fundal — de aici „nu se vede ca lumea".
///
/// Ce face, în ordine: aplică rotațiile știute (eticheta EXIF a telefonului, plus unghiul spus de
/// modelul care verifică documentul), apoi scoate actul din fundal.
///
/// Regula de bază: la orice nesiguranță se întoarce poza originală. Un act pe care nu l-am înțeles
/// rămâne cum a venit — tăiat greșit ar fi mai rău decât netăiat, iar dosarul se depune la ghișeu
/// o singură dată.
/// </summary>
internal static class DocumentImage
{
    /// <summary>Latura maximă pe care se face detecția. Nu-i trebuie rezoluția întreagă.</summary>
    private const int WorkingSide = 1000;

    /// <summary>
    /// Cât din cadru trebuie să ocupe actul ca să-l credem act. Sub prag e un obiect de pe masă;
    /// peste, e chiar marginea pozei, deci n-am găsit nimic.
    /// </summary>
    private const double MinDocumentArea = 0.18;
    private const double MaxDocumentArea = 0.97;

    /// <summary>Sub atâta câștig nu merită recomprimat: rămâne originalul.</summary>
    private const double MinGain = 0.06;

    /// <param name="orientation">Eticheta EXIF a pozei (1..8). Ce a scris telefonul.</param>
    /// <param name="rotationDegrees">
    /// Cu câte grade trebuie rotită poza în sensul acelor de ceasornic ca actul să fie drept: 0,
    /// 90, 180 sau 270. Vine de la modelul care verifică oricum fiecare document.
    ///
    /// Nu se deduce aici, deși am încercat: pe o diplomă reală, scorurile statistice ale celor
    /// patru rotații au ieșit −0.043 / −0.053 / −0.060 / −0.060, adică zgomot. Distribuția
    /// cernelii spune dacă rândurile sunt orizontale, dar nu spune unde e susul — iar o rotire
    /// greșită e mai rea decât niciuna.
    /// </param>
    public static byte[] Tidy(byte[] content, int orientation = 1, int rotationDegrees = 0)
    {
        try
        {
            using Mat source = Cv2.ImDecode(content, ImreadModes.Color);
            if (source.Empty())
            {
                return content;
            }

            using Mat upright = Upright(source, orientation, rotationDegrees);
            using Mat? cropped = Crop(upright);

            if (cropped is null && orientation is 1 && rotationDegrees is 0)
            {
                // Nimic de schimbat: se întoarce originalul, cu compresia lui cu tot.
                return content;
            }

            Cv2.ImEncode(".jpg", cropped ?? upright, out byte[] encoded, [(int)ImwriteFlags.JpegQuality, 92]);
            return encoded;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Orice eșec de procesare înseamnă „las-o cum e", nu „pică dosarul".
            return content;
        }
    }

    /// <summary>
    /// Poza așa cum trebuie văzută: întâi eticheta EXIF (cum a ținut telefonul aparatul), apoi
    /// rotația actului în cadru (spusă de model). Se aplică amândouă la început, ca decupajul să
    /// lucreze pe forma finală.
    /// </summary>
    private static Mat Upright(Mat source, int orientation, int rotationDegrees)
    {
        using Mat exif = ExifRotated(source, orientation);

        int turn = (rotationDegrees % 360 + 360) % 360;

        return turn switch
        {
            90 => Rotated(exif, RotateFlags.Rotate90Clockwise),
            180 => Rotated(exif, RotateFlags.Rotate180),
            270 => Rotated(exif, RotateFlags.Rotate90Counterclockwise),
            _ => exif.Clone(),
        };
    }

    private static Mat ExifRotated(Mat source, int orientation) => orientation switch
    {
        3 or 4 => Rotated(source, RotateFlags.Rotate180),
        5 or 6 => Rotated(source, RotateFlags.Rotate90Clockwise),
        7 or 8 => Rotated(source, RotateFlags.Rotate90Counterclockwise),
        _ => source.Clone(),
    };

    private static Mat Rotated(Mat source, RotateFlags flags)
    {
        var result = new Mat();
        Cv2.Rotate(source, result, flags);
        return result;
    }

    /// <summary>
    /// Actul, decupat din cadru: întâi după muchii (poză de sus, cu perspectivă), altfel după
    /// diferența față de culoarea fundalului. Null când nu e nimic de câștigat.
    /// </summary>
    private static Mat? Crop(Mat image)
    {
        // Trei încercări, de la cea mai precisă la cea mai tolerantă. Se merge mai departe și când
        // una găsește ceva fără folos — un „act" cât toată poza nu e o descoperire, iar oprirea la
        // prima care răspunde lăsa buletinul pe masa de lemn neatins.
        Func<Mat, Point2f[]?>[] detectors =
        [
            FindDocumentQuad,
            FindDocumentAgainstBackground,
            FindDesaturatedDocument,
        ];

        foreach (Func<Mat, Point2f[]?> detector in detectors)
        {
            Point2f[]? quad = detector(image);
            if (quad is null)
            {
                continue;
            }

            Mat warped = Warp(image, quad);
            if (Gain(warped, image) >= MinGain)
            {
                return warped;
            }

            warped.Dispose();
        }

        return null;
    }

    /// <summary>Cât din suprafața pozei s-a dus — adică cât fundal am scos.</summary>
    private static double Gain(Mat result, Mat original) =>
        1 - (double)result.Width * result.Height / ((double)original.Width * original.Height);

    /// <summary>
    /// Conturul actului: muchii, închise cu o dilatare ca liniile întrerupte de umbră să se lege,
    /// apoi cel mai mare patrulater convex din cadru. Găsește actul fotografiat oblic, cu tot cu
    /// perspectivă.
    /// </summary>
    private static Point2f[]? FindDocumentQuad(Mat image)
    {
        using Mat work = Working(image, out double scale);

        using var gray = new Mat();
        Cv2.CvtColor(work, gray, ColorConversionCodes.BGR2GRAY);
        Cv2.GaussianBlur(gray, gray, new Size(5, 5), 0);

        using var edges = new Mat();
        Cv2.Canny(gray, edges, 60, 180);
        using Mat kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(5, 5));
        Cv2.Dilate(edges, edges, kernel);

        Cv2.FindContours(
            edges,
            out Point[][] contours,
            out _,
            RetrievalModes.External,
            ContourApproximationModes.ApproxSimple);

        double frame = (double)work.Width * work.Height;

        foreach (Point[] contour in contours.OrderByDescending(c => Cv2.ContourArea(c)))
        {
            double area = Cv2.ContourArea(contour);

            if (area < frame * MinDocumentArea)
            {
                // Ordonate descrescător: de aici încolo sunt doar mai mici.
                break;
            }

            if (area > frame * MaxDocumentArea)
            {
                continue;
            }

            Point[] approximated = Cv2.ApproxPolyDP(contour, 0.02 * Cv2.ArcLength(contour, true), true);
            if (approximated.Length != 4 || !Cv2.IsContourConvex(approximated))
            {
                continue;
            }

            return Scaled(approximated.Select(p => new Point2f(p.X, p.Y)), scale);
        }

        return null;
    }

    /// <summary>
    /// Actul, ca zonă care se deosebește de fundal.
    ///
    /// Fundalul îl aflăm din marginea pozei — masa pe care stă buletinul, geamul alb al
    /// scannerului. Ce diferă de el destul de tare e actul. Un singur criteriu acoperă amândouă
    /// situațiile, și nu depinde de care e mai luminos: pe lemn actul e mai deschis, pe geamul
    /// alb e mai închis.
    ///
    /// Ce s-a încercat înainte și nu ține: detecția pe muchii nu vede cardul pe lemn (grăunțul are
    /// linii la fel de puternice ca marginea lui), iar detecția pe cerneală rupe un act cu rânduri
    /// rare de text în benzi separate și păstrează doar una.
    /// </summary>
    private static Point2f[]? FindDocumentAgainstBackground(Mat image)
    {
        using Mat work = Working(image, out double scale);
        using Mat difference = DistanceFromBackground(work);

        using var mask = new Mat();
        Cv2.Threshold(difference, mask, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);

        return DocumentFrom(work, mask, scale);
    }

    /// <summary>
    /// Actul ca pată fără culoare — buletinul fotografiat pe masa de lemn.
    ///
    /// A treia încercare, pentru fundalul cu grăunte: lemnul are prea multă textură, deci fiecare
    /// pixel diferă de mediana marginii și pata de „act" se întinde peste toată masa. Măsurat pe
    /// poza reală: 73% din cadru, cu umplere 0.73. Saturația îl separă curat — hârtia e cenușie,
    /// lemnul e maro: 29% din cadru, umplere 0.93.
    /// </summary>
    private static Point2f[]? FindDesaturatedDocument(Mat image)
    {
        using Mat work = Working(image, out double scale);

        using var hsv = new Mat();
        Cv2.CvtColor(work, hsv, ColorConversionCodes.BGR2HSV);

        Mat[] channels = Cv2.Split(hsv);
        try
        {
            using var saturation = new Mat();
            Cv2.GaussianBlur(channels[1], saturation, new Size(7, 7), 0);

            using var mask = new Mat();
            Cv2.Threshold(saturation, mask, 0, 255, ThresholdTypes.BinaryInv | ThresholdTypes.Otsu);

            return DocumentFrom(work, mask, scale);
        }
        finally
        {
            foreach (Mat channel in channels)
            {
                channel.Dispose();
            }
        }
    }

    /// <summary>
    /// Cea mai mare pată din mască, dacă arată a act: de mărimea potrivită, dreptunghiulară și cu
    /// scris pe ea. Partea comună a celor două detecții pe zone.
    /// </summary>
    private static Point2f[]? DocumentFrom(Mat work, Mat mask, double scale)
    {
        // Kernel legat de mărimea pozei, nu fix: rândurile de text trebuie să se lege între ele,
        // altfel actul iese ca un teanc de benzi și păstrăm doar una.
        int span = Math.Max(9, Math.Min(work.Width, work.Height) / 20) | 1;
        using Mat kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(span, span));
        Cv2.MorphologyEx(mask, mask, MorphTypes.Close, kernel);
        Cv2.MorphologyEx(mask, mask, MorphTypes.Open, kernel);

        Cv2.FindContours(
            mask,
            out Point[][] contours,
            out _,
            RetrievalModes.External,
            ContourApproximationModes.ApproxSimple);

        if (contours.Length == 0)
        {
            return null;
        }

        Point[] biggest = contours.MaxBy(c => Cv2.ContourArea(c)) ?? contours[0];
        double area = Cv2.ContourArea(biggest);
        double frame = (double)work.Width * work.Height;

        if (area < frame * MinDocumentArea || area > frame * MaxDocumentArea)
        {
            return null;
        }

        Rect box = Cv2.BoundingRect(biggest);
        if (!HasText(work, box))
        {
            return null;
        }

        RotatedRect rect = Cv2.MinAreaRect(biggest);
        double rectArea = (double)rect.Size.Width * rect.Size.Height;

        // Pata umple dreptunghiul ei ⇒ e chiar un act, deci îl putem și îndrepta. Dacă nu, ne
        // mulțumim cu chenarul drept: mai bine tăiat corect decât rotit după o formă neclară.
        return rectArea > 0 && area / rectArea >= 0.85
            ? Scaled(rect.Points(), scale)
            : Scaled(Corners(Inflate(box, span / 2, work.Size())), scale);
    }

    /// <summary>O copie micșorată, pe care se face toată detecția.</summary>
    private static Mat Working(Mat image, out double scale)
    {
        scale = Math.Min(1, (double)WorkingSide / Math.Max(image.Width, image.Height));

        var work = new Mat();
        Cv2.Resize(image, work, new Size((int)(image.Width * scale), (int)(image.Height * scale)));
        return work;
    }

    private static Point2f[] Scaled(IEnumerable<Point2f> points, double scale) =>
        [.. points.Select(p => new Point2f((float)(p.X / scale), (float)(p.Y / scale)))];

    private static Point2f[] Corners(Rect box) =>
    [
        new(box.Left, box.Top),
        new(box.Right, box.Top),
        new(box.Right, box.Bottom),
        new(box.Left, box.Bottom),
    ];

    /// <summary>
    /// Cât de departe e fiecare pixel de culoarea fundalului, pe o scară de gri.
    ///
    /// Fundalul se ia ca mediană a unei rame de pe marginea pozei: acolo e masa sau geamul, nu
    /// actul. Mediană, nu medie, ca un colț de act intrat în ramă să nu mute reperul.
    /// </summary>
    private static Mat DistanceFromBackground(Mat work)
    {
        int band = Math.Max(2, Math.Min(work.Width, work.Height) / 50);

        using var border = new Mat(work.Rows, work.Cols, MatType.CV_8UC1, Scalar.All(0));
        Cv2.Rectangle(border, new Rect(0, 0, work.Width, work.Height), Scalar.All(255), band);

        using var background = new Mat(work.Rows, work.Cols, work.Type(), Median(work, border));

        using var blurred = new Mat();
        Cv2.GaussianBlur(work, blurred, new Size(5, 5), 0);

        using var difference = new Mat();
        Cv2.Absdiff(blurred, background, difference);

        var gray = new Mat();
        Cv2.CvtColor(difference, gray, ColorConversionCodes.BGR2GRAY);
        return gray;
    }

    /// <summary>Culoarea mediană a pixelilor din mască, pe fiecare canal.</summary>
    private static Scalar Median(Mat image, Mat mask)
    {
        Mat[] channels = Cv2.Split(image);
        try
        {
            double[] medians = new double[3];

            for (int c = 0; c < 3 && c < channels.Length; c++)
            {
                using var histogram = new Mat();
                Cv2.CalcHist([channels[c]], [0], mask, histogram, 1, [256], [[0f, 256f]]);
                histogram.GetArray(out float[] counts);

                double half = counts.Sum() / 2;
                double running = 0;

                for (int value = 0; value < counts.Length; value++)
                {
                    running += counts[value];
                    if (running >= half)
                    {
                        medians[c] = value;
                        break;
                    }
                }
            }

            return new Scalar(medians[0], medians[1], medians[2]);
        }
        finally
        {
            foreach (Mat channel in channels)
            {
                channel.Dispose();
            }
        }
    }

    /// <summary>
    /// Are scris pe ea? Plasa de siguranță a decupajului.
    ///
    /// Fără ea, o diplomă scanată pe hârtie galbenă ar putea fi tăiată exact pe dos: banda albă
    /// lăsată de scanner lângă act se deosebește și ea de fundal, deci ar trece drept „actul", iar
    /// diploma ar rămâne pe dinafară. Hârtia goală n-are cerneală; un act are.
    /// </summary>
    private static bool HasText(Mat work, Rect box)
    {
        using var gray = new Mat();
        Cv2.CvtColor(work, gray, ColorConversionCodes.BGR2GRAY);

        using var ink = new Mat();
        Cv2.Threshold(gray, ink, 0, 255, ThresholdTypes.BinaryInv | ThresholdTypes.Otsu);

        using var inside = new Mat(ink, box);

        return Cv2.CountNonZero(inside) > box.Width * box.Height * 0.015;
    }

    /// <summary>Îndreaptă patrulaterul într-un dreptunghi, la dimensiunea laturilor lui reale.</summary>
    private static Mat Warp(Mat image, Point2f[] quad)
    {
        Point2f[] ordered = OrderCorners(quad);

        float width = (float)Math.Max(Distance(ordered[0], ordered[1]), Distance(ordered[3], ordered[2]));
        float height = (float)Math.Max(Distance(ordered[0], ordered[3]), Distance(ordered[1], ordered[2]));

        Point2f[] target =
        [
            new(0, 0),
            new(width - 1, 0),
            new(width - 1, height - 1),
            new(0, height - 1),
        ];

        using Mat transform = Cv2.GetPerspectiveTransform(ordered, target);
        var result = new Mat();
        Cv2.WarpPerspective(image, result, transform, new Size((int)width, (int)height));
        return result;
    }

    /// <summary>Colțurile în ordinea stânga-sus, dreapta-sus, dreapta-jos, stânga-jos.</summary>
    private static Point2f[] OrderCorners(Point2f[] quad)
    {
        Point2f[] bySum = [.. quad.OrderBy(p => p.X + p.Y)];
        Point2f[] byDifference = [.. quad.OrderBy(p => p.Y - p.X)];

        return [bySum[0], byDifference[0], bySum[3], byDifference[3]];
    }

    private static double Distance(Point2f a, Point2f b) =>
        Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));

    private static Rect Inflate(Rect box, int padding, Size bounds)
    {
        int x = Math.Max(0, box.X - padding);
        int y = Math.Max(0, box.Y - padding);

        return new Rect(
            x,
            y,
            Math.Min(bounds.Width - x, box.Width + padding * 2),
            Math.Min(bounds.Height - y, box.Height + padding * 2));
    }
}
