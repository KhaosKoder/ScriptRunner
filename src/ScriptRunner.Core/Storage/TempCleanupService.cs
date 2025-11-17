using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ScriptRunner.Core.Storage;

public sealed class TempCleanupService : BackgroundService
{
    private readonly ILogger<TempCleanupService> _logger;
    private readonly string _root;

    public TempCleanupService(ILogger<TempCleanupService> logger)
    {
        _logger = logger;
        _root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ScriptRunner", "TempScripts");
        Directory.CreateDirectory(_root);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                CleanupOld();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Temp cleanup failed");
            }
            await Task.Delay(TimeSpan.FromMinutes(15), stoppingToken);
        }
    }

    private void CleanupOld()
    {
        if (!Directory.Exists(_root)) return;
        foreach (var dir in Directory.GetDirectories(_root))
        {
            try
            {
                var di = new DirectoryInfo(dir);
                if (di.LastWriteTimeUtc < DateTime.UtcNow.AddHours(-1))
                {
                    di.Delete(true);
                    _logger.LogInformation("Deleted orphan temp folder {Dir}", dir);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete temp folder {Dir}", dir);
            }
        }
    }
}
