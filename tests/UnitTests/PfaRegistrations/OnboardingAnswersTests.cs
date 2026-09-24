using Application.PfaRegistrations.Onboarding.Answers;
using Domain.PfaRegistrations;
using Domain.Users;
using Infrastructure.Database;
using Infrastructure.DomainEvents;
using Microsoft.EntityFrameworkCore;
using SharedKernel;
using Shouldly;
using Xunit;

namespace UnitTests.PfaRegistrations;

/// <summary>Răspunsurile din onboarding: salvate la fiecare schimbare, văzute de admin cu tot parcursul.</summary>
public sealed class OnboardingAnswersTests
{
    [Fact]
    public async Task Acelasi_raspuns_nu_se_dubleaza_iar_schimbarea_ramane_in_istoric()
    {
        await using ApplicationDbContext db = NewDb();
        var clock = new StepClock();
        var user = new User { Id = Guid.NewGuid(), Email = "a@test.ro", FirstName = "Ion", LastName = "Pop", Role = UserRole.Client };
        var pfa = new PfaRegistration { Id = Guid.NewGuid(), UserId = user.Id, User = user, Cui = "1", LegalName = "POP ION PFA" };
        db.Users.Add(user);
        db.PfaRegistrations.Add(pfa);
        await db.SaveChangesAsync();

        var save = new SaveOnboardingAnswerCommandHandler(db, clock);
        await save.Handle(new SaveOnboardingAnswerCommand(user.Id, "eligibility", "age", "Ai împlinit 21 de ani?", "no", "Nu"), default);
        await save.Handle(new SaveOnboardingAnswerCommand(user.Id, "eligibility", "age", "Ai împlinit 21 de ani?", "no", "Nu"), default);
        await save.Handle(new SaveOnboardingAnswerCommand(user.Id, "eligibility", "age", "Ai împlinit 21 de ani?", "yes", "Da"), default);
        await save.Handle(new SaveOnboardingAnswerCommand(user.Id, "fiscal", "tva", "Deții certificat de TVA intracomunitar?", "no", "Nu"), default);

        (await db.OnboardingAnswers.CountAsync()).ShouldBe(3);

        IReadOnlyList<OnboardingAnswerValue> mine = (await new GetMyOnboardingAnswersQueryHandler(db)
            .Handle(new GetMyOnboardingAnswersQuery(user.Id), default)).Value;
        mine.Single(a => a.QuestionId == "age").Value.ShouldBe("yes");

        IReadOnlyList<OnboardingAnswerResponse> admin = (await new GetOnboardingAnswersForRegistrationQueryHandler(db)
            .Handle(new GetOnboardingAnswersForRegistrationQuery(pfa.Id), default)).Value;
        admin.Select(a => a.QuestionId).ShouldBe(["age", "tva"]);
        admin[0].ValueLabel.ShouldBe("Da");
        admin[0].PreviousLabels.ShouldBe(["Nu"]);
        admin[1].PreviousLabels.ShouldBeEmpty();
    }

    [Fact]
    public async Task Raspuns_fara_intrebare_sau_prea_lung_e_refuzat()
    {
        await using ApplicationDbContext db = NewDb();
        var save = new SaveOnboardingAnswerCommandHandler(db, new StepClock());

        (await save.Handle(new SaveOnboardingAnswerCommand(Guid.NewGuid(), "eligibility", "", "?", "yes", "Da"), default)).IsFailure.ShouldBeTrue();
        (await save.Handle(new SaveOnboardingAnswerCommand(Guid.NewGuid(), "eligibility", "age", "?", new string('x', 2001), "x"), default))
            .IsFailure.ShouldBeTrue();
    }

    private static ApplicationDbContext NewDb() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
        new Events());

    /// <summary>Fiecare citire e cu un minut mai târziu: ordinea răspunsurilor e sigură.</summary>
    private sealed class StepClock : IDateTimeProvider
    {
        private DateTime _now = new(2026, 9, 24, 8, 0, 0, DateTimeKind.Utc);

        public DateTime UtcNow => _now = _now.AddMinutes(1);
    }

    private sealed class Events : IDomainEventsDispatcher
    {
        public Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
