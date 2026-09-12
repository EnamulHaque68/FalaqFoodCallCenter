using CallCenter.Application.Recordings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CallCenter.Infrastructure.Recordings;

public sealed class RecordingRetentionWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IOptions<RecordingStorageOptions> _options;
    private readonly ILogger<RecordingRetentionWorker> _logger;

    public RecordingRetentionWorker(
        IServiceProvider serviceProvider,
        IOptions<RecordingStorageOptions> options,
        ILogger<RecordingRetentionWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Value.EnableRetentionCleanupWorker)
        {
            _logger.LogInformation("Recording retention cleanup worker is disabled by configuration.");
            return;
        }

        var interval = TimeSpan.FromHours(Math.Max(1, _options.Value.CleanupIntervalHours));
        _logger.LogInformation("Recording retention cleanup worker started. Interval: {Interval}", interval);

        // Run an initial quick pass after a brief startup delay
        await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var retentionService = scope.ServiceProvider.GetRequiredService<IRecordingRetentionService>();
                var result = await retentionService.ProcessExpiredRecordingsAsync(stoppingToken);

                if (result.PurgedCount > 0)
                {
                    _logger.LogInformation("Recording retention worker executed successfully: {Message}", result.Message);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in recording retention background worker.");
            }

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("Recording retention worker stopped.");
    }
}
