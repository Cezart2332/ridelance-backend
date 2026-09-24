using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.PfaRegistrations;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.PfaRegistrations.Onboarding.Answers;

/// <summary>Un răspuns din onboarding, cu textele de pe ecran (întrebarea, varianta aleasă).</summary>
public sealed record SaveOnboardingAnswerCommand(
    Guid UserId,
    string StepKey,
    string QuestionId,
    string Question,
    string Value,
    string ValueLabel) : ICommand;

/// <summary>Ultimul răspuns al userului curent la fiecare întrebare: fluxul îl reia după refresh.</summary>
public sealed record GetMyOnboardingAnswersQuery(Guid UserId) : IQuery<IReadOnlyList<OnboardingAnswerValue>>;

public sealed record OnboardingAnswerValue(string StepKey, string QuestionId, string Value);

/// <summary>Toate răspunsurile unui dosar, pentru admin.</summary>
public sealed record GetOnboardingAnswersForRegistrationQuery(Guid RegistrationId) : IQuery<IReadOnlyList<OnboardingAnswerResponse>>;

/// <param name="PreviousLabels">Răspunsurile date înainte, de la primul la penultimul, când s-au schimbat.</param>
public sealed record OnboardingAnswerResponse(
    string StepKey,
    string QuestionId,
    string Question,
    string Value,
    string ValueLabel,
    DateTime AnsweredAtUtc,
    IReadOnlyList<string> PreviousLabels);

internal static class OnboardingAnswerErrors
{
    public static readonly Error Invalid =
        Error.Problem("OnboardingAnswer.Invalid", "Răspunsul nu e valid.");
}

internal sealed class SaveOnboardingAnswerCommandHandler(IApplicationDbContext context, IDateTimeProvider clock)
    : ICommandHandler<SaveOnboardingAnswerCommand>
{
    public async Task<Result> Handle(SaveOnboardingAnswerCommand command, CancellationToken cancellationToken)
    {
        string stepKey = command.StepKey?.Trim() ?? string.Empty;
        string questionId = command.QuestionId?.Trim() ?? string.Empty;
        string question = command.Question?.Trim() ?? string.Empty;
        string value = command.Value ?? string.Empty;
        string label = command.ValueLabel?.Trim() ?? string.Empty;

        if (stepKey.Length is 0 or > 32
            || questionId.Length is 0 or > 96
            || question.Length > OnboardingAnswerLimits.Question
            || value.Length > OnboardingAnswerLimits.Value
            || label.Length > OnboardingAnswerLimits.Value)
        {
            return Result.Failure(OnboardingAnswerErrors.Invalid);
        }

        // Același răspuns dat din nou (revenire pe ecran, reapăsare) nu e o schimbare.
        string? latest = await context.OnboardingAnswers
            .AsNoTracking()
            .Where(a => a.UserId == command.UserId && a.QuestionId == questionId)
            .OrderByDescending(a => a.AnsweredAtUtc)
            .Select(a => a.Value)
            .FirstOrDefaultAsync(cancellationToken);
        if (latest == value)
        {
            return Result.Success();
        }

        context.OnboardingAnswers.Add(new OnboardingAnswer
        {
            Id = Guid.NewGuid(),
            UserId = command.UserId,
            StepKey = stepKey,
            QuestionId = questionId,
            Question = question.Length > 0 ? question : questionId,
            Value = value,
            ValueLabel = label.Length > 0 ? label : value,
            AnsweredAtUtc = clock.UtcNow,
        });
        await context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

internal sealed class GetMyOnboardingAnswersQueryHandler(IApplicationDbContext context)
    : IQueryHandler<GetMyOnboardingAnswersQuery, IReadOnlyList<OnboardingAnswerValue>>
{
    public async Task<Result<IReadOnlyList<OnboardingAnswerValue>>> Handle(
        GetMyOnboardingAnswersQuery query,
        CancellationToken cancellationToken)
    {
        List<OnboardingAnswer> rows = await context.OnboardingAnswers
            .AsNoTracking()
            .Where(a => a.UserId == query.UserId)
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(a => a.QuestionId)
            .Select(g => g.OrderByDescending(a => a.AnsweredAtUtc).First())
            .Select(a => new OnboardingAnswerValue(a.StepKey, a.QuestionId, a.Value))
            .ToList();
    }
}

internal sealed class GetOnboardingAnswersForRegistrationQueryHandler(IApplicationDbContext context)
    : IQueryHandler<GetOnboardingAnswersForRegistrationQuery, IReadOnlyList<OnboardingAnswerResponse>>
{
    public async Task<Result<IReadOnlyList<OnboardingAnswerResponse>>> Handle(
        GetOnboardingAnswersForRegistrationQuery query,
        CancellationToken cancellationToken)
    {
        Guid? userId = await context.PfaRegistrations
            .AsNoTracking()
            .Where(p => p.Id == query.RegistrationId)
            .Select(p => (Guid?)p.UserId)
            .SingleOrDefaultAsync(cancellationToken);

        if (userId is null)
        {
            return Result.Failure<IReadOnlyList<OnboardingAnswerResponse>>(
                Error.NotFound("Pfa.NotFound", "Înregistrarea PFA nu a fost găsită."));
        }

        List<OnboardingAnswer> rows = await context.OnboardingAnswers
            .AsNoTracking()
            .Where(a => a.UserId == userId)
            .ToListAsync(cancellationToken);

        // În ordinea în care a răspuns prima dată la fiecare întrebare: exact parcursul lui.
        return rows
            .GroupBy(a => a.QuestionId)
            .Select(g =>
            {
                var ordered = g.OrderBy(a => a.AnsweredAtUtc).ToList();
                OnboardingAnswer last = ordered[^1];
                return (First: ordered[0].AnsweredAtUtc, Answer: new OnboardingAnswerResponse(
                    last.StepKey,
                    last.QuestionId,
                    last.Question,
                    last.Value,
                    last.ValueLabel,
                    last.AnsweredAtUtc,
                    ordered.Take(ordered.Count - 1).Select(a => a.ValueLabel).ToList()));
            })
            .OrderBy(x => x.First)
            .Select(x => x.Answer)
            .ToList();
    }
}
