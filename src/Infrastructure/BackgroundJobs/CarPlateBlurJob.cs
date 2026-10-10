using Application.Abstractions.Messaging;
using Application.Cars.Commands.PaidExtras;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharedKernel;

namespace Infrastructure.BackgroundJobs;

/// <summary>
/// La fiecare 20 de secunde: blurează numărul în pozele mașinilor care au plătit „număr ascuns”
/// după ce își urcaseră pozele. Blurarea durează (un model rulat pe fiecare poză), deci nu stă în
/// webhookul Stripe, care trebuie să răspundă repede.
/// </summary>
internal sealed partial class CarPlateBlurJob(IServiceScopeFactory scopeFactory, ILogger<CarPlateBlurJob> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(20);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using IServiceScope scope = scopeFactory.CreateScope();
                Result<int> blurred = await scope.ServiceProvider
                    .GetRequiredService<ICommandHandler<BlurHiddenPlatePhotosCommand, int>>()
                    .Handle(new BlurHiddenPlatePhotosCommand(), stoppingToken);
                if (blurred.IsSuccess && blurred.Value > 0)
                {
                    LogBlurred(logger, blurred.Value);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogFailed(logger, exception);
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Număr ascuns: {Count} poze blurate după plată.")]
    private static partial void LogBlurred(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "Eroare în CarPlateBlurJob.")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
