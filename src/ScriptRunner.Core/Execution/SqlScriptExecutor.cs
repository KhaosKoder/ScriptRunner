using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using ScriptRunner.Core.Configuration;
using ScriptRunner.Core.Storage;
using System.Text.RegularExpressions;

namespace ScriptRunner.Core.Execution;

public sealed class SqlScriptExecutor : ISqlExecutor
{
    private readonly ITempScriptStorage _tempStorage;
    private readonly ILogger<SqlScriptExecutor> _logger;
    private readonly SqlConnectionsOptions _connections;

    public SqlScriptExecutor(ITempScriptStorage tempStorage, SqlConnectionsOptions connections, ILogger<SqlScriptExecutor> logger)
    {
        _tempStorage = tempStorage;
        _logger = logger;
        _connections = connections;
    }

    public async Task<ScriptExecutionResult> ExecuteSqlAsync(string scriptPath, ScriptMetadata metadata, IDictionary<string, object?> parameters, string connectionString, CancellationToken ct = default)
    {
        string content = await File.ReadAllTextAsync(scriptPath, ct);
        var replaced = ReplaceTokens(content, parameters);
        var connStr = metadata.SqlConnectionString ?? connectionString ?? string.Empty;
        if (string.IsNullOrWhiteSpace(connStr))
        {
            return new ScriptExecutionResult { ExitCode = -1, StdOut = string.Empty, StdErr = "Connection string missing", Status = ScriptExecutionStatus.Failed };
        }
        try
        {
            _logger.LogInformation("[SQL] Beginning execution for script {Id} on connection='{Conn}'", metadata.Id, connStr);
            using var conn = new SqliteConnection(connStr);
            await conn.OpenAsync(ct);
            using var cmd = conn.CreateCommand();
            cmd.CommandTimeout = 60;
            using var tx = (SqliteTransaction)await conn.BeginTransactionAsync(ct);
            var statements = SplitStatements(replaced);
            var stdout = new List<string>();
            foreach (var stmt in statements)
            {
                if (string.IsNullOrWhiteSpace(stmt)) continue;
                cmd.Transaction = tx;
                cmd.CommandText = stmt;
                var affected = await cmd.ExecuteNonQueryAsync(ct);
                stdout.Add($"[OK] {affected} rows affected");
                _logger.LogDebug("[SQL] Statement executed rowsAffected={Rows} snippet={Snippet}", affected, stmt.Length > 120 ? stmt.Substring(0,120)+"..." : stmt);
            }
            await tx.CommitAsync(ct);
            _logger.LogInformation("[SQL] Transaction committed for script {Id}", metadata.Id);
            return new ScriptExecutionResult { ExitCode = 0, StdOut = string.Join('\n', stdout), StdErr = string.Empty, Status = ScriptExecutionStatus.Succeeded };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SQL execution failed");
            return new ScriptExecutionResult { ExitCode = -1, StdOut = string.Empty, StdErr = ex.Message, Status = ScriptExecutionStatus.Failed };
        }
        finally
        {
            _tempStorage.DeleteTempPath(scriptPath);
        }
    }

    private static string ReplaceTokens(string content, IDictionary<string, object?> parameters)
    {
        // Support $(VarName) and {{VarName}}
        foreach (var kv in parameters)
        {
            var token1 = "$(" + kv.Key + ")";
            var token2 = "{{" + kv.Key + "}}";
            var value = kv.Value?.ToString() ?? string.Empty;
            content = content.Replace(token1, value).Replace(token2, value);
        }
        return content;
    }

    private static IEnumerable<string> SplitStatements(string script)
    {
        // naive split on ';' not inside quotes
        var parts = Regex.Split(script, @";\s*\n", RegexOptions.Multiline);
        return parts;
    }
}
