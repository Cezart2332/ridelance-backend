using Infrastructure.Dossiers;
using OpenCvSharp;
using Shouldly;
using Xunit;

namespace ArchitectureTests.Dossiers;

/// <summary>
/// Curățarea pozelor de act înainte să intre în dosar.
///
/// Miza, pe pozele reale din dosarul unui client: buletinul fotografiat pe masa de lemn ocupa sub
/// jumătate din cadru, iar diploma scanată avea o treime bandă albă lângă ea. Puse pe A4 ieșeau
/// mici, înconjurate de fundal.
///
/// Fixturile de aici sunt sintetice, dar reproduc exact cele două situații: un act cenușiu pe
/// fundal colorat și un act pe hârtie colorată lângă o bandă albă.
/// </summary>
public sealed class DocumentImageTests
{
    [OpenCvFact]
    public void PhotoOnAColouredTable_KeepsOnlyTheDocument()
    {
        // Un act cenușiu, cu scris, pe o masă maro — buletinul fotografiat acasă.
        byte[] photo = Encode(Scene(1000, 1400, background: new Scalar(40, 90, 150), document: new Rect(150, 400, 700, 450)));

        byte[] tidied = DocumentImage.Tidy(photo);

        using Mat result = Decode(tidied);
        // Actul ocupa 22% din cadru; după decupaj trebuie să fie aproape tot.
        result.Width.ShouldBeInRange(650, 780);
        result.Height.ShouldBeInRange(400, 530);
    }

    [OpenCvFact]
    public void ScanWithABlankBand_LosesTheBand()
    {
        // Actul pe hârtie gălbuie, cu o treime din geam rămasă albă lângă el — scanul de diplomă.
        using Mat scan = Scene(1500, 1000, background: new Scalar(250, 250, 250), document: new Rect(450, 40, 1010, 920), paper: new Scalar(150, 220, 250));

        byte[] tidied = DocumentImage.Tidy(Encode(scan));

        using Mat result = Decode(tidied);
        result.Width.ShouldBeLessThan(1150, $"dimensiune {result.Width}x{result.Height}");
        result.Height.ShouldBeGreaterThan(800, $"dimensiune {result.Width}x{result.Height}");
    }

    /// <summary>
    /// Banda albă nu poate trece drept act.
    ///
    /// Detecția pe saturație o vede la fel de „fără culoare" ca hârtia unui act, deci fără o plasă
    /// de siguranță ar tăia exact pe dos: ar păstra banda și ar arunca diploma. Plasa e scrisul —
    /// hârtia goală n-are cerneală.
    /// </summary>
    [OpenCvFact]
    public void BlankPaperNextToTheDocument_IsNotMistakenForIt()
    {
        using Mat scan = Scene(1500, 1000, background: new Scalar(250, 250, 250), document: new Rect(450, 40, 1010, 920), paper: new Scalar(150, 220, 250));

        byte[] tidied = DocumentImage.Tidy(Encode(scan));

        using Mat result = Decode(tidied);
        // Ce rămâne e actul galben, nu banda albă din stânga lui.
        int size = Math.Min(20, Math.Min(result.Width, result.Height) / 4);
        using var middle = new Mat(result, new Rect((result.Width - size) / 2, (result.Height - size) / 2, size, size));
        Cv2.Mean(middle).Val0.ShouldBeLessThan(220);
    }

    [OpenCvTheory]
    [InlineData(90)]
    [InlineData(270)]
    public void QuarterTurn_SwapsTheSides(int degrees)
    {
        using Mat scan = Scene(1200, 800, background: new Scalar(250, 250, 250), document: new Rect(40, 40, 1120, 720), paper: new Scalar(150, 220, 250));
        byte[] source = Encode(scan);

        using Mat straight = Decode(DocumentImage.Tidy(source));
        using Mat turned = Decode(DocumentImage.Tidy(source, rotationDegrees: degrees));

        turned.Width.ShouldBe(straight.Height);
        turned.Height.ShouldBe(straight.Width);
    }

    [OpenCvFact]
    public void HalfTurn_KeepsTheSides()
    {
        using Mat scan = Scene(1200, 800, background: new Scalar(250, 250, 250), document: new Rect(40, 40, 1120, 720), paper: new Scalar(150, 220, 250));
        byte[] source = Encode(scan);

        using Mat straight = Decode(DocumentImage.Tidy(source));
        using Mat turned = Decode(DocumentImage.Tidy(source, rotationDegrees: 180));

        turned.Width.ShouldBe(straight.Width);
        turned.Height.ShouldBe(straight.Height);
    }

    /// <summary>Un fișier pe care nu-l putem citi rămâne cum a venit — dosarul nu pică pentru atât.</summary>
    [OpenCvFact]
    public void UnreadableContent_IsReturnedUntouched()
    {
        byte[] rubbish = [0x00, 0x01, 0x02, 0x03, 0x04];

        DocumentImage.Tidy(rubbish).ShouldBe(rubbish);
    }

    /// <summary>
    /// O poză din care nu e nimic de scos nu se recomprimă degeaba: actul umple deja cadrul, cu
    /// scrisul până aproape de margine.
    /// </summary>
    [OpenCvFact]
    public void DocumentThatAlreadyFillsTheFrame_IsLeftAlone()
    {
        byte[] photo = Encode(Scene(
            1000,
            700,
            background: new Scalar(250, 250, 250),
            document: new Rect(0, 0, 1000, 700),
            paper: new Scalar(250, 250, 250),
            textInset: 6));

        DocumentImage.Tidy(photo).ShouldBe(photo);
    }

    /// <summary>Un act cu scris pe el, așezat într-un cadru mai mare.</summary>
    private static Mat Scene(
        int width,
        int height,
        Scalar background,
        Rect document,
        Scalar? paper = null,
        int textInset = 30)
    {
        var scene = new Mat(height, width, MatType.CV_8UC3, background);

        Cv2.Rectangle(scene, document, paper ?? new Scalar(235, 240, 245), -1);

        // Rânduri de „text": fără ele, actul e o pată fără cerneală și e tratat ca hârtie goală.
        for (int y = document.Y + textInset; y < document.Bottom - textInset - 14; y += 40)
        {
            Cv2.Rectangle(
                scene,
                new Rect(document.X + textInset, y, document.Width - textInset * 2, 14),
                new Scalar(30, 30, 30),
                -1);
        }

        return scene;
    }

    private static byte[] Encode(Mat image)
    {
        using (image)
        {
            Cv2.ImEncode(".jpg", image, out byte[] encoded, [(int)ImwriteFlags.JpegQuality, 95]);
            return encoded;
        }
    }

    private static Mat Decode(byte[] content) => Cv2.ImDecode(content, ImreadModes.Color);
}
