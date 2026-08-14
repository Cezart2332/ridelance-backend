namespace SharedKernel;

public enum ErrorType
{
    Failure = 0,
    Validation = 1,
    Problem = 2,
    NotFound = 3,
    Conflict = 4,

    /// <summary>
    /// Cererea e bine formată, dar starea dosarului nu o permite încă — 422. Diferă de
    /// <see cref="Validation"/>, care e despre payload greșit: aici payload-ul e corect, doar
    /// precondițiile nu sunt îndeplinite.
    /// </summary>
    Unprocessable = 5
}
