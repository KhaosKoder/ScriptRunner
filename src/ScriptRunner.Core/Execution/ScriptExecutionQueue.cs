using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace ScriptRunner.Core.Execution;

public sealed class ScriptExecutionQueue
{
    private readonly SemaphoreSlim _semaphore;
    private readonly ConcurrentQueue<Func<CancellationToken, Task>> _queue = new();
    private readonly ILogger<ScriptExecutionQueue> _logger;

    public ScriptExecutionQueue(int maxConcurrent, ILogger<ScriptExecutionQueue> logger)
    {
        _semaphore = new SemaphoreSlim(maxConcurrent, maxConcurrent);
        _logger = logger;
    }

    public void Enqueue(Func<CancellationToken, Task> work)
    {
        _queue.Enqueue(work);
        _ = ProcessNextAsync();
    }

    private async Task ProcessNextAsync()
    {
        if (!_queue.TryDequeue(out var work)) return;
        await _semaphore.WaitAsync();
        try
        {
            await work(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Execution error");
        }
        finally
        {
            _semaphore.Release();
            // Trigger next
            _ = ProcessNextAsync();
        }
    }
}
