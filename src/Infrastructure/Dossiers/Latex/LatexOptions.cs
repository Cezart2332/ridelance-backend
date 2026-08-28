namespace Infrastructure.Dossiers.Latex;

/// <summary>Motorul LaTeX. Secțiunea <c>Latex</c> din configurație.</summary>
/// <remarks>
/// Are valori implicite care funcționează pe imaginea de rulare; se configurează doar pe mașinile
/// unde TeX stă în altă parte.
/// </remarks>
public sealed class LatexOptions
{
    public const string SectionName = "Latex";

    /// <summary>Programul care compilează. XeLaTeX, pentru că citește UTF-8 și fonturi de sistem.</summary>
    public string Engine { get; set; } = "xelatex";

    /// <summary>
    /// Cât așteptăm o compilare. Un document de flotă se compilează în sub o secundă; limita e
    /// pentru cazul în care motorul rămâne blocat, nu pentru documente mari.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 60;
}
