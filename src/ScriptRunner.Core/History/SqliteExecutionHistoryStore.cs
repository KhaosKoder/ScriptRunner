using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace ScriptRunner.Core.History;

public sealed class SqliteExecutionHistoryStore : IExecutionHistoryStore
{
    private readonly string _dbPath;
    private readonly ILogger<SqliteExecutionHistoryStore> _logger;

    public SqliteExecutionHistoryStore(ILogger<SqliteExecutionHistoryStore> logger)
    {
        _logger = logger;
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var dir = Path.Combine(appData, "ScriptRunner");
        Directory.CreateDirectory(dir);
        _dbPath = Path.Combine(dir, "history.db");
        EnsureSchema();
    }

    private void EnsureSchema()
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"CREATE TABLE IF NOT EXISTS ScriptExecution (
            ExecutionId TEXT PRIMARY KEY,
            ScriptId TEXT,
            ScriptName TEXT,
            StartedAtUtc TEXT,
            FinishedAtUtc TEXT,
            ParametersJson TEXT,
            Status TEXT,
            ExitCode INTEGER,
            StdOut TEXT,
            StdErr TEXT,
            RanByUser TEXT,
            EmailSent INTEGER
        );";
        cmd.ExecuteNonQuery();
    }

    private const int MaxStoredOutput = 20000;

    private static string Truncate(string s) => s.Length <= MaxStoredOutput ? s : s.Substring(0, MaxStoredOutput);

    public async Task StoreAsync(ScriptExecutionRecord record, CancellationToken ct = default)
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync(ct);
        await PurgeOldAsync(conn, ct);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"INSERT OR REPLACE INTO ScriptExecution (ExecutionId, ScriptId, ScriptName, StartedAtUtc, FinishedAtUtc, ParametersJson, Status, ExitCode, StdOut, StdErr, RanByUser, EmailSent)
                           VALUES ($id,$sid,$sname,$start,$finish,$params,$status,$exit,$out,$err,$user,$email);";
        var r = Sanitize(record);
        FillParams(cmd, r);
        await cmd.ExecuteNonQueryAsync(ct);
        _logger.LogInformation("[History] Stored execution record {Id} status={Status}", record.ExecutionId, record.Status);
    }

    public async Task UpdateAsync(ScriptExecutionRecord record, CancellationToken ct = default)
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync(ct);
        await PurgeOldAsync(conn, ct);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"UPDATE ScriptExecution SET ScriptId=$sid, ScriptName=$sname, StartedAtUtc=$start, FinishedAtUtc=$finish, ParametersJson=$params, Status=$status, ExitCode=$exit, StdOut=$out, StdErr=$err, RanByUser=$user, EmailSent=$email WHERE ExecutionId=$id";
        var r = Sanitize(record);
        FillParams(cmd, r);
        await cmd.ExecuteNonQueryAsync(ct);
        _logger.LogInformation("[History] Updated execution record {Id} status={Status}", record.ExecutionId, record.Status);
    }

    private ScriptExecutionRecord Sanitize(ScriptExecutionRecord record) => new()
    {
        ExecutionId = record.ExecutionId,
        ScriptId = record.ScriptId,
        ScriptName = record.ScriptName,
        StartedAtUtc = record.StartedAtUtc,
        FinishedAtUtc = record.FinishedAtUtc,
        ParametersJson = Truncate(record.ParametersJson ?? string.Empty),
        Status = record.Status,
        ExitCode = record.ExitCode,
        StdOut = Truncate(record.StdOut ?? string.Empty),
        StdErr = Truncate(record.StdErr ?? string.Empty),
        RanByUser = record.RanByUser,
        EmailSent = record.EmailSent
    };

    private async Task PurgeOldAsync(SqliteConnection conn, CancellationToken ct)
    {
        using var purge = conn.CreateCommand();
        purge.CommandText = @"DELETE FROM ScriptExecution WHERE StartedAtUtc < $cutoff";
        purge.Parameters.AddWithValue("$cutoff", DateTime.UtcNow.AddDays(-30).ToString("o"));
        var rows = await purge.ExecuteNonQueryAsync(ct);
        _logger.LogDebug("[History] Purged {Rows} old execution records", rows);
    }

    private static void FillParams(SqliteCommand cmd, ScriptExecutionRecord record)
    {
        cmd.Parameters.AddWithValue("$id", record.ExecutionId.ToString());
        cmd.Parameters.AddWithValue("$sid", record.ScriptId);
        cmd.Parameters.AddWithValue("$sname", record.ScriptName);
        cmd.Parameters.AddWithValue("$start", record.StartedAtUtc.ToString("o"));
        cmd.Parameters.AddWithValue("$finish", record.FinishedAtUtc.ToString("o"));
        cmd.Parameters.AddWithValue("$params", record.ParametersJson);
        cmd.Parameters.AddWithValue("$status", record.Status.ToString());
        cmd.Parameters.AddWithValue("$exit", record.ExitCode);
        cmd.Parameters.AddWithValue("$out", record.StdOut);
        cmd.Parameters.AddWithValue("$err", record.StdErr);
        cmd.Parameters.AddWithValue("$user", record.RanByUser);
        cmd.Parameters.AddWithValue("$email", record.EmailSent ? 1 : 0);
    }

    public async Task<IReadOnlyList<ScriptExecutionRecord>> QueryAsync(int take = 100, CancellationToken ct = default)
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync(ct);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT ExecutionId, ScriptId, ScriptName, StartedAtUtc, FinishedAtUtc, ParametersJson, Status, ExitCode, StdOut, StdErr, RanByUser, EmailSent
                            FROM ScriptExecution ORDER BY StartedAtUtc DESC LIMIT $take";
        cmd.Parameters.AddWithValue("$take", take);
        var list = new List<ScriptExecutionRecord>();
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(ReadRecord(reader));
        }
        return list;
    }

    public async Task<ScriptExecutionRecord?> GetAsync(Guid executionId, CancellationToken ct = default)
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync(ct);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT ExecutionId, ScriptId, ScriptName, StartedAtUtc, FinishedAtUtc, ParametersJson, Status, ExitCode, StdOut, StdErr, RanByUser, EmailSent
                            FROM ScriptExecution WHERE ExecutionId = $id";
        cmd.Parameters.AddWithValue("$id", executionId.ToString());
        using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct)) return ReadRecord(reader);
        return null;
    }

    private static ScriptExecutionRecord ReadRecord(SqliteDataReader reader)
    {
        return new ScriptExecutionRecord
        {
            ExecutionId = Guid.Parse(reader.GetString(0)),
            ScriptId = reader.GetString(1),
            ScriptName = reader.GetString(2),
            StartedAtUtc = DateTime.Parse(reader.GetString(3)),
            FinishedAtUtc = DateTime.Parse(reader.GetString(4)),
            ParametersJson = reader.GetString(5),
            Status = Enum.Parse<ScriptExecutionStatus>(reader.GetString(6)),
            ExitCode = reader.GetInt32(7),
            StdOut = reader.GetString(8),
            StdErr = reader.GetString(9),
            RanByUser = reader.GetString(10),
            EmailSent = reader.GetInt32(11) == 1
        };
    }
}
