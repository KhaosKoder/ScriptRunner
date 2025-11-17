using Microsoft.Extensions.Logging;
using ScriptRunner.Core.Storage;
using ScriptRunner.Core.Configuration;

namespace ScriptRunner.Core.Execution;

public sealed class CompositeScriptExecutor : IScriptExecutor
{
    private readonly IPowerShellExecutor _ps;
    private readonly ISqlExecutor _sql;
    private readonly ITempScriptStorage _tempStorage;
    private readonly ILogger<CompositeScriptExecutor> _logger;
    private readonly SqlConnectionsOptions _sqlConnections;

    public CompositeScriptExecutor(IPowerShellExecutor ps, ISqlExecutor sql, ITempScriptStorage tempStorage, SqlConnectionsOptions sqlConnections, ILogger<CompositeScriptExecutor> logger)
    {
        _ps = ps;
        _sql = sql;
        _tempStorage = tempStorage;
        _sqlConnections = sqlConnections;
        _logger = logger;
    }

    public async Task<ScriptExecutionResult> ExecuteAsync(ScriptMetadata metadata, IDictionary<string, object?> parameters, string ranByUser, CancellationToken ct = default)
    {
        // Expect caller to have written temp file path into parameters with key "__tempPath" to avoid refetch in executor.
        if (!parameters.TryGetValue("__tempPath", out var tempObj) || tempObj is not string tempPath || string.IsNullOrWhiteSpace(tempPath))
        {
            return new ScriptExecutionResult { ExitCode = -1, StdOut = string.Empty, StdErr = "Temp path missing", Status = ScriptExecutionStatus.Failed };
        }
        if (metadata.IsSql)
        {
            var connection = metadata.SqlConnectionString ?? _sqlConnections.Connections.FirstOrDefault().Value;
            return await _sql.ExecuteSqlAsync(tempPath, metadata, parameters, connection ?? string.Empty, ct);
        }
        return await _ps.ExecutePowerShellAsync(tempPath, metadata, parameters, ct);
    }
}
