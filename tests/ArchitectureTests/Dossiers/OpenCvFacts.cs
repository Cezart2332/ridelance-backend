using OpenCvSharp;
using Xunit;

namespace ArchitectureTests.Dossiers;

/// <summary>
/// Teste care au nevoie de OpenCV — deci de biblioteci native, nu doar de pachetul NuGet.
///
/// Lipsa lor nu e o cădere de test, e o mașină neechipată: pe serverul de build sau pe laptopul
/// unui coleg poate lipsi <c>libIlmImf-2_5.so.25</c> sau <c>libtiff.so.5</c>, pe care
/// <c>libOpenCvSharpExtern.so</c> le cere la încărcare. Atunci testele se raportează SĂRITE, cu
/// motivul la vedere — nu trecute, ca să nu pară că au verificat ceva.
///
/// În producție bibliotecile există: le instalează Dockerfile-ul, iar CI-ul le aduce la fel.
/// </summary>
internal static class OpenCvRuntime
{
    private static readonly Lazy<string?> Probe = new(() =>
    {
        try
        {
            using var probe = new Mat(1, 1, MatType.CV_8UC1, Scalar.All(0));
            return null;
        }
        catch (Exception exception)
        {
            return $"OpenCV nu se poate încărca pe mașina asta: {exception.GetBaseException().Message}";
        }
    });

    /// <summary>Motivul săririi, sau null când OpenCV e utilizabil.</summary>
    public static string? MissingReason => Probe.Value;
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class OpenCvFactAttribute : FactAttribute
{
    public OpenCvFactAttribute() => Skip = OpenCvRuntime.MissingReason;
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class OpenCvTheoryAttribute : TheoryAttribute
{
    public OpenCvTheoryAttribute() => Skip = OpenCvRuntime.MissingReason;
}
