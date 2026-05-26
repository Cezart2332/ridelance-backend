using SharedKernel;

namespace Application.Expenses;

internal static class ExpenseErrors
{
    public static Error NotFound(Guid id) =>
        Error.NotFound("Expense.NotFound", $"Cheltuiala deductibilă {id} nu a fost găsită.");

    public static readonly Error AccessDenied =
        Error.Failure("Expense.AccessDenied", "Nu ai permisiunea de a gestiona aceste cheltuieli.");

    public static readonly Error PfaNotFound =
        Error.NotFound("Expense.PfaNotFound", "Înregistrarea PFA nu a fost găsită.");
}
