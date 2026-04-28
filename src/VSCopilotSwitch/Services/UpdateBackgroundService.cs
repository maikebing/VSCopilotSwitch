using Microsoft.Extensions.Options;

namespace VSCopilotSwitch.Services;

public sealed class UpdateBackgroundService : BackgroundService
{
    private readonly IUpdateService _updateService;
    private readonly IOptionsMonitor<UpdateOptions> _options;
    private readonly ILogger<UpdateBackgroundService> _logger;

    public UpdateBackgroundService(
        IUpdateService updateService,
        IOptionsMonitor<UpdateOptions> options,
        ILogger<UpdateBackgroundService> logger)
    {
        _updateService = updateService;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var options = _options.CurrentValue;
            if (options.Enabled && options.AutoDownload)
            {
                await TryDownloadLatestAsync(stoppingToken);
            }

            var interval = Math.Clamp(options.CheckIntervalHours, 1, 168);
            await Task.Delay(TimeSpan.FromHours(interval), stoppingToken);
        }
    }

    private async Task TryDownloadLatestAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await _updateService.DownloadLatestAsync(new UpdateDownloadRequest(), cancellationToken);
            if (result.Downloaded)
            {
                _logger.LogInformation(
                    "Downloaded VSCopilotSwitch update {Version} from {Source} to local cache.",
                    result.Release?.Version,
                    result.Release?.SourceName);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // 自动更新失败只影响缓存下载，不能影响本地代理和主窗口生命周期。
            _logger.LogWarning(ex, "VSCopilotSwitch automatic update download failed.");
        }
    }
}
