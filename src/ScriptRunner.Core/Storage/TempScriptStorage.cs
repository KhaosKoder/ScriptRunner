using Microsoft.Extensions.Logging;

namespace ScriptRunner.Core.Storage;

public sealed class TempScriptStorage : ITempScriptStorage
{
    private readonly ILogger<TempScriptStorage> _logger;
    private readonly string _root;

    public TempScriptStorage(ILogger<TempScriptStorage> logger)
    {
        _logger = logger;
        _root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ScriptRunner", "TempScripts");
        Directory.CreateDirectory(_root);
    }

    public async Task<string> WriteTempScriptAsync(string content, string extension, CancellationToken ct = default)
    {
        var dir = Path.Combine(_root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "script" + extension);
        await File.WriteAllTextAsync(file, content, ct);
        _logger.LogDebug("Temp script written {File}", file);
        return file;
    }

    public void DeleteTempPath(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var dir = Path.GetDirectoryName(path)!;
                File.Delete(path);
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
                _logger.LogDebug("Deleted temp script path {Path}", path);
                return;
            }
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
                _logger.LogDebug("Deleted temp directory {Path}", path);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed deleting temp path {Path}", path);
        }
    }
}
