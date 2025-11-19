using System.Diagnostics;
using Microsoft.Extensions.Logging;
using ScriptRunner.Core.Storage;
using ScriptRunner.Core.Configuration;
using System.Globalization;
using System.Text;

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
            var psi = new ProcessStartInfo(shellPath)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            // Build a single -Command with invocation expression: & 'script.ps1' -Name 'value' -Bool:$false
            var command = BuildPowerShellCommand(scriptPath, metadata, parameters);
            psi.ArgumentList.Add("-NoProfile");
            psi.ArgumentList.Add("-ExecutionPolicy");
            psi.ArgumentList.Add("Bypass");
            psi.ArgumentList.Add("-Command");
            psi.ArgumentList.Add(command);

            var manualCmd = '"' + shellPath + '"' + " -NoProfile -ExecutionPolicy Bypass -Command " + QuoteForCmd(command);
            _logger.LogInformation("[PS] Executing via -Command keepTemp={Keep}\n[PS] ManualCommand (copy/paste):\n{Manual}", _options.KeepTempScripts, manualCmd);

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
            if (!_options.KeepTempScripts)
            {
                _tempStorage.DeleteTempPath(scriptPath);
            }
            else
            {
                _logger.LogInformation("[PS] Keeping temp script for debugging: {Path}", scriptPath);
            }
        }
    }

    private string BuildPowerShellCommand(string scriptPath, ScriptMetadata metadata, IDictionary<string, object?> parameters)
    {
        var sb = new StringBuilder();
        sb.Append("& ").Append(Sq(scriptPath));
        foreach (var def in metadata.Parameters)
        {
            if (def.Name.StartsWith("__")) continue;
            if (!parameters.TryGetValue(def.Name, out var raw) || raw is null) continue;
            switch (def.Type)
            {
                case ScriptParameterType.Bool:
                    var b = CoerceBool(raw);
                    sb.Append(' ').Append('-').Append(def.Name).Append(':').Append(b ? "$true" : "$false");
                    break;
                case ScriptParameterType.Int:
                    var i = CoerceInt(raw);
                    sb.Append(' ').Append('-').Append(def.Name).Append(' ').Append(i.ToString(CultureInfo.InvariantCulture));
                    break;
                case ScriptParameterType.Decimal:
                    var d = CoerceDecimal(raw);
                    sb.Append(' ').Append('-').Append(def.Name).Append(' ').Append(d.ToString(CultureInfo.InvariantCulture));
                    break;
                case ScriptParameterType.DateTime:
                    var dt = CoerceDateTime(raw);
                    sb.Append(' ').Append('-').Append(def.Name).Append(' ').Append(Sq(dt.ToString("o")));
                    break;
                default:
                    var s = raw.ToString() ?? string.Empty;
                    sb.Append(' ').Append('-').Append(def.Name).Append(' ').Append(Sq(s));
                    break;
            }
        }
        return sb.ToString();
    }

    private static string Sq(string v) => "'" + v.Replace("'", "''") + "'";
    private static string QuoteForCmd(string v)
    {
        // Wrap -Command payload for cmd.exe and powershell.exe invocation: use double quotes, escape embedded quotes
        return '"' + v.Replace("\"", "\\\"") + '"';
    }

    private static bool CoerceBool(object raw)
    {
        if (raw is bool b) return b;
        if (raw is string s)
        {
            if (bool.TryParse(s, out var parsed)) return parsed;
            if (s.Equals("1") || s.Equals("yes", StringComparison.OrdinalIgnoreCase) || s.Equals("on", StringComparison.OrdinalIgnoreCase)) return true;
            if (s.Equals("0") || s.Equals("no", StringComparison.OrdinalIgnoreCase) || s.Equals("off", StringComparison.OrdinalIgnoreCase)) return false;
        }
        try { return Convert.ToBoolean(raw); } catch { return false; }
    }
    private static int CoerceInt(object raw)
    {
        if (raw is int i) return i;
        if (raw is string s && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)) return parsed;
        try { return Convert.ToInt32(raw, CultureInfo.InvariantCulture); } catch { return 0; }
    }
    private static decimal CoerceDecimal(object raw)
    {
        if (raw is decimal d) return d;
        if (raw is string s && decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)) return parsed;
        try { return Convert.ToDecimal(raw, CultureInfo.InvariantCulture); } catch { return 0m; }
    }
    private static DateTime CoerceDateTime(object raw)
    {
        if (raw is DateTime dt) return dt;
        if (raw is string s && DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsed)) return parsed;
        try { return Convert.ToDateTime(raw, CultureInfo.InvariantCulture); } catch { return DateTime.UtcNow; }
    }

    private string ResolvePowerShellPath()
    {
        if (!string.IsNullOrWhiteSpace(_options.PowerShellPath) && File.Exists(_options.PowerShellPath))
        {
            return _options.PowerShellPath;
        }
        var pwsh = FindOnPath("pwsh.exe");
        if (!string.IsNullOrEmpty(pwsh)) return pwsh;
        if (OperatingSystem.IsWindows() && _options.FallbackToWindowsPowerShell)
        {
            var winPs = FindOnPath("powershell.exe");
            if (!string.IsNullOrEmpty(winPs)) return winPs;
            var sys = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
            if (File.Exists(sys)) return sys;
        }
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
}
