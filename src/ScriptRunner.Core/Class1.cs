namespace ScriptRunner.Core;

public enum ScriptParameterType
{
    String,
    Int,
    Decimal,
    Bool,
    DateTime,
    Enum
}

public sealed class ScriptParameterDefinition
{
    public required string Name { get; init; }
    public required ScriptParameterType Type { get; init; }
    public bool Required { get; init; }
    public string? DisplayName { get; init; }
    public string? Default { get; init; }
    public string? HelpText { get; init; }
    public string[]? EnumValues { get; init; }
}

public sealed class ScriptMetadata
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Category { get; init; }
    public string? Description { get; init; }
    public IReadOnlyList<ScriptParameterDefinition> Parameters { get; init; } = Array.Empty<ScriptParameterDefinition>();
    public string? SqlConnectionString { get; init; }
    public string? SourcePath { get; init; } // relative path inside repo
    public bool IsSql => (SourcePath?.EndsWith(".sql", StringComparison.OrdinalIgnoreCase) ?? false) || !string.IsNullOrWhiteSpace(SqlConnectionString);
}

public interface IScriptProvider
{
    Task<IReadOnlyList<ScriptMetadata>> ListScriptsAsync(CancellationToken ct = default);
    Task<ScriptMetadata?> GetScriptMetadataAsync(string id, CancellationToken ct = default);
    Task<string> GetScriptContentAsync(string id, CancellationToken ct = default);
}

public interface IScriptParser
{
    ScriptMetadata Parse(string rawContent);
}

public interface IScriptExecutor
{
    Task<ScriptExecutionResult> ExecuteAsync(ScriptMetadata metadata, IDictionary<string, object?> parameters, string ranByUser, CancellationToken ct = default);
}

public interface IPowerShellExecutor
{
    Task<ScriptExecutionResult> ExecutePowerShellAsync(string scriptPath, ScriptMetadata metadata, IDictionary<string, object?> parameters, CancellationToken ct = default);
}

public interface ISqlExecutor
{
    Task<ScriptExecutionResult> ExecuteSqlAsync(string scriptPath, ScriptMetadata metadata, IDictionary<string, object?> parameters, string connectionString, CancellationToken ct = default);
}

public interface IExecutionHistoryStore
{
    Task StoreAsync(ScriptExecutionRecord record, CancellationToken ct = default);
    Task UpdateAsync(ScriptExecutionRecord record, CancellationToken ct = default);
    Task<IReadOnlyList<ScriptExecutionRecord>> QueryAsync(int take = 100, CancellationToken ct = default);
    Task<ScriptExecutionRecord?> GetAsync(Guid executionId, CancellationToken ct = default);
}

public interface IEmailNotifier
{
    Task<bool> SendResultsAsync(ScriptExecutionRecord record, CancellationToken ct = default);
}

public interface IUserEmailResolver
{
    string? Resolve(string windowsIdentityName);
}

public interface ITempScriptStorage
{
    Task<string> WriteTempScriptAsync(string content, string extension, CancellationToken ct = default);
    void DeleteTempPath(string path);
}

public enum ScriptExecutionStatus
{
    Queued,
    Running,
    Succeeded,
    Failed
}

public sealed class ScriptExecutionResult
{
    public required int ExitCode { get; init; }
    public required string StdOut { get; init; }
    public required string StdErr { get; init; }
    public required ScriptExecutionStatus Status { get; init; }
}

public sealed class ScriptExecutionRecord
{
    public Guid ExecutionId { get; init; } = Guid.NewGuid();
    public required string ScriptId { get; init; }
    public required string ScriptName { get; init; }
    public DateTime StartedAtUtc { get; init; }
    public DateTime FinishedAtUtc { get; init; }
    public string ParametersJson { get; init; } = "{}";
    public ScriptExecutionStatus Status { get; init; }
    public int ExitCode { get; init; }
    public string StdOut { get; init; } = string.Empty;
    public string StdErr { get; init; } = string.Empty;
    public required string RanByUser { get; init; }
    public bool EmailSent { get; init; }
}
