using System.Diagnostics;
using Microsoft.Extensions.Logging;
using ScriptRunner.Core.Storage;
using ScriptRunner.Core.Configuration;

namespace ScriptRunner.Core.Execution;

public sealed class SimpleScriptExecutor : IScriptExecutor, IPowerShellExecutor
{
    private readonly ITempScriptStorage _tempStorage;
    private readonly ILogger<SimpleScriptExecutor> _logger;
    private readonly ExecutionOptions _options;

    public SimpleScriptExecutor(ITempScriptStorage tempStorage, ExecutionOptions options, ILogger<SimpleScriptExecutor> logger)
    {
        _tempStorage = tempStorage;
        _options = options;
        _logger = logger;
    }

    public Task<ScriptExecutionResult> ExecuteAsync(ScriptMetadata metadata, IDictionary<string, object?> parameters, string ranByUser, CancellationToken ct = default)
    {
        // Delegate based on script type using provided temp path
        if (metadata.IsSql)
        {
            return Task.FromResult(new ScriptExecutionResult { ExitCode = -1, StdOut = string.Empty, StdErr = "SQL executor not implemented here", Status = ScriptExecutionStatus.Failed });
        }
        var path = parameters.TryGetValue("__tempPath", out var p) ? p as string : null;
        if (string.IsNullOrWhiteSpace(path))
        {
            return Task.FromResult(new ScriptExecutionResult { ExitCode = -1, StdOut = string.Empty, StdErr = "Temp script path missing", Status = ScriptExecutionStatus.Failed });
        }
        return ExecutePowerShellAsync(path!, metadata, parameters, ct);
    }

    public async Task<ScriptExecutionResult> ExecutePowerShellAsync(string scriptPath, ScriptMetadata metadata, IDictionary<string, object?> parameters, CancellationToken ct = default)
    {
        try
        {
            var shellPath = ResolvePowerShellPath();
            var psi = new ProcessStartInfo
            {
                FileName = shellPath,
                Arguments = BuildArguments(shellPath, scriptPath, metadata, parameters),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            _logger.LogInformation("[PS] Executing script path={Path} shell={Shell} with {ParamCount} parameters", scriptPath, shellPath, metadata.Parameters.Count);
            using var proc = Process.Start(psi)!;
            var stdoutTask = proc.StandardOutput.ReadToEndAsync();
            var stderrTask = proc.StandardError.ReadToEndAsync();
            await Task.WhenAll(stdoutTask, stderrTask);
            await proc.WaitForExitAsync(ct);
            _logger.LogInformation("[PS] Completed script path={Path} exitCode={Code} stdoutLen={OutLen} stderrLen={ErrLen}", scriptPath, proc.ExitCode, stdoutTask.Result.Length, stderrTask.Result.Length);
            return new ScriptExecutionResult
            {
                ExitCode = proc.ExitCode,
                StdOut = stdoutTask.Result,
                StdErr = stderrTask.Result,
                Status = proc.ExitCode == 0 ? ScriptExecutionStatus.Succeeded : ScriptExecutionStatus.Failed
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PowerShell execution failed");
            return new ScriptExecutionResult { ExitCode = -1, StdOut = string.Empty, StdErr = ex.Message, Status = ScriptExecutionStatus.Failed };
        }
        finally
        {
            // Ensure deletion of the passed-in temp file and its directory
            _tempStorage.DeleteTempPath(scriptPath);
        }
    }

    private string ResolvePowerShellPath()
    {
        if (!string.IsNullOrWhiteSpace(_options.PowerShellPath) && File.Exists(_options.PowerShellPath))
        {
            return _options.PowerShellPath;
        }
        // Prefer pwsh if available
        var pwsh = FindOnPath("pwsh.exe");
        if (!string.IsNullOrEmpty(pwsh)) return pwsh;
        // Fall back to Windows PowerShell if allowed and on Windows
        if (OperatingSystem.IsWindows() && _options.FallbackToWindowsPowerShell)
        {
            var winPs = FindOnPath("powershell.exe");
            if (!string.IsNullOrEmpty(winPs)) return winPs;
            // Typical system32 path as last resort
            var sys = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
            if (File.Exists(sys)) return sys;
        }
        // Non-Windows or no PS found
        throw new FileNotFoundException("No PowerShell executable found. Install PowerShell 7 (pwsh) or enable fallback to powershell.exe.");
    }

    private static string? FindOnPath(string exe)
    {
        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var dir in pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                var candidate = Path.Combine(dir, exe);
                if (File.Exists(candidate)) return candidate;
            }
            catch { }
        }
        return null;
    }

    private static string BuildArguments(string shellPath, string path, ScriptMetadata metadata, IDictionary<string, object?> parameters)
    {
        var args = new List<string>();
        var isPwsh = Path.GetFileName(shellPath).Equals("pwsh.exe", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(shellPath).Equals("pwsh", StringComparison.OrdinalIgnoreCase);
        if (isPwsh)
        {
            args.AddRange(new[]{"-NoProfile","-ExecutionPolicy","Bypass","-File", Quote(path) });
        }
        else
        {
            // Windows PowerShell
            args.AddRange(new[]{"-NoProfile","-ExecutionPolicy","Bypass","-File", Quote(path) });
        }
        foreach (var def in metadata.Parameters)
        {
            if (def.Name.StartsWith("__")) continue;
            if (!parameters.TryGetValue(def.Name, out var val)) continue;
            var str = FormatValue(val, def.Type);
            args.Add("-" + def.Name);
            args.Add(Quote(str));
        }
        return string.Join(' ', args);
    }

    private static string FormatValue(object? value, ScriptParameterType type)
    {
        return type switch
        {
            ScriptParameterType.Bool => (value is bool b) ? (b ? "$true" : "$false") : (value?.ToString()?.Equals("true", StringComparison.OrdinalIgnoreCase) == true ? "$true" : "$false"),
            ScriptParameterType.DateTime => value is DateTime dt ? dt.ToString("o") : value?.ToString() ?? string.Empty,
            _ => value?.ToString() ?? string.Empty
        };
    }

    private static string Quote(string v) => '"' + v.Replace("\"", "\\\"") + '"';
}
