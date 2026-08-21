using System.Text;
using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Notifications;
using Application.Abstractions.Services;
using Application.Abstractions.Ai;
using Infrastructure.Ai;
using Infrastructure.Authentication;
using Infrastructure.Authorization;
using Infrastructure.Database;
using Infrastructure.DomainEvents;
using Infrastructure.Notifications;
using Infrastructure.Services;
using Infrastructure.Banking;
using Infrastructure.Email;
using Infrastructure.Invoicing;
using Infrastructure.Payments;
using Application.Abstractions;
using Application.Abstractions.Dossiers;
using Application.Abstractions.Security;
using Application.Abstractions.Settings;
using Infrastructure.AppSettings;
using Infrastructure.Dossiers;
using Resend;
using Infrastructure.Time;
using Infrastructure.BackgroundJobs;
using Infrastructure.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using SharedKernel;

namespace Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration) =>
        services
            .AddServices(configuration)
            .AddDatabase(configuration)
            .AddHealthChecks(configuration)
            .AddAuthenticationInternal(configuration)
            .AddAuthorizationInternal();

    private static IServiceCollection AddServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IDateTimeProvider, DateTimeProvider>();

        services.AddTransient<IDomainEventsDispatcher, DomainEventsDispatcher>();

        // File services
        services.Configure<EncryptionSettings>(configuration.GetSection("Encryption"));
        services.Configure<FileStorageSettings>(configuration.GetSection("FileStorage"));
        services.AddScoped<IFileEncryptionService, FileEncryptionService>();
        services.AddScoped<ISecretProtector, SecretProtector>();
        services.AddScoped<IFileStorageService, LocalFileStorageService>();

        // Parametri comerciali/operaționali configurabili (cache 60s peste app_settings)
        services.AddScoped<IAppSettings, AppSettingsService>();

        // Cotele fiscale afișate pe dashboard vin din configurare, nu din codul frontend.
        services.Configure<Application.PfaDashboard.FiscalPolicyOptions>(
            configuration.GetSection(Application.PfaDashboard.FiscalPolicyOptions.SectionName));

        // Ponderile sortării „Recomandate" se reglează din configurare, fără deploy (spec §5.2).
        services.Configure<Application.Cars.Scoring.RecommendationScoringOptions>(
            configuration.GetSection(Application.Cars.Scoring.RecommendationScoringOptions.SectionName));

        // Generarea dosarelor PDF (QuestPDF — licență Community, venit anual < 1M USD)
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
        services.AddScoped<IDossierGenerator, ArrDossierGenerator>();
        services.AddScoped<ICompanyFormationPdfGenerator, CompanyFormationPdfGenerator>();

        // Email Service
        services.AddHttpClient<IResend, ResendClient>();
        string? resendApiKey = configuration["Resend:ApiKey"] ?? Environment.GetEnvironmentVariable("Resend__ApiKey");
        if (!string.IsNullOrEmpty(resendApiKey))
        {
            services.Configure<ResendClientOptions>(o => o.ApiToken = resendApiKey);
        }
        services.AddTransient<IEmailService, ResendEmailService>();
        services.AddSingleton<IMjmlRenderer, MjmlRendererAdapter>();
        services.AddSingleton<ILicensePlateDetectionService, LicensePlateDetectionService>();

        // Stripe
        services.AddScoped<IStripeService, StripeService>();

        // Oblio (facturare)
        services.Configure<OblioOptions>(configuration.GetSection(OblioOptions.SectionName));
        services.AddHttpClient<IOblioService, OblioService>();
        services.AddScoped<IInvoiceGenerator, InvoiceGenerator>();

        // Bolt
        services.AddHttpClient();
        services.AddScoped<IBoltService, BoltService>();

        // AI (prevalidarea documentelor prin OpenRouter / Gemini)
        services.Configure<OpenRouterOptions>(configuration.GetSection(OpenRouterOptions.SectionName));
        string? openRouterApiKey = configuration["OpenRouter:ApiKey"] ??
                                   Environment.GetEnvironmentVariable("OpenRouter__ApiKey");
        if (!string.IsNullOrWhiteSpace(openRouterApiKey))
        {
            services.Configure<OpenRouterOptions>(o => o.ApiKey = openRouterApiKey);
        }
        services.AddHttpClient<IDocumentAiAnalyzer, OpenRouterDocumentAiAnalyzer>(client =>
            client.Timeout = TimeSpan.FromMinutes(3));

        // Un singur provider de open banking. Alegerea prin config a dispărut odată cu
        // Enable Banking și GoCardless — cine vrea altul îl înregistrează aici.
        services.Configure<FintableOptions>(configuration.GetSection(FintableOptions.SectionName));
        services.AddHttpClient<IBankDataProvider, FintableBankDataProvider>();

        // Background Jobs
        services.AddHostedService<MondayAccessGrantJob>();
        services.AddHostedService<RecurringDocumentationNotificationJob>();
        services.AddHostedService<DocumentExpiryCheckJob>();
        services.AddHostedService<PfaIncomePeriodInitializationJob>();
        services.AddHostedService<BoltSyncJob>();
        // Prospețimea scorului nu are eveniment care s-o declanșeze — doar trecerea timpului.
        services.AddHostedService<ListingFreshnessScoreJob>();
        services.AddHostedService<BankSyncJob>();
        services.AddHostedService<DocumentAiVerificationJob>();
        services.AddHostedService<CompanyFormationDraftPurgeJob>();

        return services;
    }

    private static IServiceCollection AddDatabase(this IServiceCollection services, IConfiguration configuration)
    {
        string? connectionString = configuration.GetConnectionString("Database");

        services.AddDbContext<ApplicationDbContext>(
            options => options
                .UseNpgsql(connectionString, npgsqlOptions =>
                    npgsqlOptions.MigrationsHistoryTable(HistoryRepository.DefaultTableName, Schemas.Default))
                .UseSnakeCaseNamingConvention()
                // Migrations are handwritten and the model snapshot is not maintained,
                // so EF's pending-model-changes validation would always abort Migrate().
                .ConfigureWarnings(warnings =>
                    warnings.Ignore(RelationalEventId.PendingModelChangesWarning)));

        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<ApplicationDbContext>());

        services.AddScoped<IWebPushService, WebPushService>();

        return services;
    }

    private static IServiceCollection AddHealthChecks(this IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddHealthChecks()
            .AddNpgSql(configuration.GetConnectionString("Database")!);

        return services;
    }

    private static IServiceCollection AddAuthenticationInternal(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(o =>
            {
                o.RequireHttpsMetadata = false;
                o.TokenValidationParameters = new TokenValidationParameters
                {
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(configuration["Jwt:Secret"]!)),
                    ValidIssuer = configuration["Jwt:Issuer"],
                    ValidAudience = configuration["Jwt:Audience"],
                    ClockSkew = TimeSpan.Zero
                };

                // Allow SignalR to receive JWT from query string
                o.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        string? accessToken = context.Request.Query["access_token"];

                        PathString path = context.HttpContext.Request.Path;
                        if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                        {
                            context.Token = accessToken;
                        }

                        return Task.CompletedTask;
                    }
                };
            });

        services.AddHttpContextAccessor();
        services.AddScoped<IUserContext, UserContext>();
        services.AddSingleton<IPasswordHasher, PasswordHasher>();
        services.AddSingleton<ITokenProvider, TokenProvider>();

        return services;
    }

    private static IServiceCollection AddAuthorizationInternal(this IServiceCollection services)
    {
        services.AddAuthorization();

        services.AddScoped<PermissionProvider>();

        services.AddTransient<IAuthorizationHandler, PermissionAuthorizationHandler>();

        services.AddTransient<IAuthorizationPolicyProvider, PermissionAuthorizationPolicyProvider>();

        return services;
    }
}
