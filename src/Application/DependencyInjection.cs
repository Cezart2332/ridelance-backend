using Application.Abstractions.Behaviors;
using Application.Abstractions.Messaging;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel;

namespace Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.Scan(scan => scan.FromAssembliesOf(typeof(DependencyInjection))
            .AddClasses(classes => classes.AssignableTo(typeof(IQueryHandler<,>)), publicOnly: false)
                .AsImplementedInterfaces()
                .WithScopedLifetime()
            .AddClasses(classes => classes.AssignableTo(typeof(ICommandHandler<>)), publicOnly: false)
                .AsImplementedInterfaces()
                .WithScopedLifetime()
            .AddClasses(classes => classes.AssignableTo(typeof(ICommandHandler<,>)), publicOnly: false)
                .AsImplementedInterfaces()
                .WithScopedLifetime());

        services.Decorate(typeof(ICommandHandler<,>), typeof(ValidationDecorator.CommandHandler<,>));
        services.Decorate(typeof(ICommandHandler<>), typeof(ValidationDecorator.CommandBaseHandler<>));

        services.Decorate(typeof(IQueryHandler<,>), typeof(LoggingDecorator.QueryHandler<,>));
        services.Decorate(typeof(ICommandHandler<,>), typeof(LoggingDecorator.CommandHandler<,>));
        services.Decorate(typeof(ICommandHandler<>), typeof(LoggingDecorator.CommandBaseHandler<>));

        services.Scan(scan => scan.FromAssembliesOf(typeof(DependencyInjection))
            .AddClasses(classes => classes.AssignableTo(typeof(IDomainEventHandler<>)), publicOnly: false)
            .AsImplementedInterfaces()
            .WithScopedLifetime());

        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly, includeInternalTypes: true);

        services.AddScoped<Documents.ExtractedFields.IExtractedFieldApplier,
            Documents.ExtractedFields.ExtractedFieldApplier>();

        services.AddScoped<PfaRegistrations.Onboarding.OnboardingStateService>();

        // Reia în dosarul de înființare datele citite din buletin la pasul de eligibilitate.
        services.AddScoped<PfaRegistrations.Onboarding.CompanyFormation.CompanyFormationPrefillService>();

        // Anunțurile interne: dosar generat, plată încasată.
        services.AddScoped<PfaRegistrations.Onboarding.Notifications.OnboardingOpsNotifier>();
        services.AddScoped<PfaRegistrations.Onboarding.CompanyFormation.ConsultoDossierSender>();

        // Poarta uneltelor de dezvoltare. Se înregistrează mereu: ea însăși e cea care răspunde
        // „nu" în producție, deci nu depinde de o decizie luată la pornire.
        services.AddScoped<PfaRegistrations.Onboarding.DevTools.OnboardingDevToolsGate>();

        // Scorul „Recomandate": pur și fără stare, deci un singleton ajunge.
        services.AddSingleton<Cars.Scoring.IRecommendationScoreCalculator,
            Cars.Scoring.RecommendationScoreCalculator>();

        services.AddScoped<Cars.Scoring.ListingScoreService>();

        // Aduce și decriptează credențialele Oblio ale proprietarului, într-un singur loc.
        services.AddScoped<Invoicing.OwnerOblioResolver>();

        services.AddScoped<Banking.BankAccountSyncService>();
        services.AddScoped<Banking.BankConnectionClaimService>();

        return services;
    }
}
