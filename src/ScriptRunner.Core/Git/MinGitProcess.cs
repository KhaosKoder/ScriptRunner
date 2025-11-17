using System.Diagnostics;
using ScriptRunner.Core.Configuration;
using Microsoft.Extensions.Logging;

namespace ScriptRunner.Core.Git;

public sealed class MinGitProcess
{
    private readonly ScriptRepoOptions _options;
    private readonly ILogger<MinGitProcess>? _logger;

    public MinGitProcess(ScriptRepoOptions options, ILogger<MinGitProcess>? logger = null)
    {
        _options = options;
        _logger = logger;
    }

    private string ResolveGitPath()
    {
        if (!string.IsNullOrWhiteSpace(_options.MinGitPath) && File.Exists(_options.MinGitPath)) return _options.MinGitPath;
        // Attempt to resolve from application base directory (e.g., if package content copied)
        var baseDir = AppContext.BaseDirectory;
        var local = Path.Combine(baseDir, "git.exe");
        if (File.Exists(local)) return local;
        // Try Tools\MinGit\git.exe relative to base
        var tools = Path.Combine(baseDir, "Tools", "MinGit", "git.exe");
        if (File.Exists(tools)) return tools;
        throw new InvalidOperationException("MinGit git.exe not found. Configure ScriptRepo:MinGitPath or deploy git.exe next to the service.");
    }

    public async Task<(int exitCode, string stdout, string stderr)> RunAsync(string args, string? workingDir = null, CancellationToken ct = default)
    {
        var gitPath = ResolveGitPath();
        var attempt = 0;
        Exception? lastEx = null;
        while (attempt < 3)
        {
            try
            {
                _logger?.LogDebug("[Git] Starting command attempt {Attempt}: {Args} (cwd={Cwd})", attempt + 1, args, workingDir ?? Environment.CurrentDirectory);
                var psi = new ProcessStartInfo
                {
                    FileName = gitPath,
                    Arguments = args,
                    WorkingDirectory = workingDir ?? Environment.CurrentDirectory,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                psi.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
                psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
                var pat = _options.ActiveProvider.ResolvePat();
                if (!string.IsNullOrEmpty(pat)) psi.Environment["GIT_HTTP_PASSWORD"] = pat;
                if (!string.IsNullOrWhiteSpace(_options.ActiveProvider.Proxy)) psi.Environment["HTTPS_PROXY"] = _options.ActiveProvider.Proxy;

                using var proc = new Process { StartInfo = psi };
                proc.Start();
                var stdoutTask = proc.StandardOutput.ReadToEndAsync();
                var stderrTask = proc.StandardError.ReadToEndAsync();
                await Task.WhenAll(stdoutTask, stderrTask);
                await proc.WaitForExitAsync(ct);
                var stdout = stdoutTask.Result;
                var stderr = stderrTask.Result;
                var sample = stdout.Length > 300 ? stdout.Substring(0, 300) + "..." : stdout;
                _logger?.LogDebug("[Git] Completed command (code={Code}) stdoutLen={Len} stderrLen={ErrLen} sample=\n{Sample}", proc.ExitCode, stdout.Length, stderr.Length, sample);
                if (proc.ExitCode != 0)
                    _logger?.LogWarning("[Git] Non-zero exit ({Code}) for args {Args}. stderr=\n{Err}", proc.ExitCode, args, stderr);
                return (proc.ExitCode, stdout, stderr);
            }
            catch (Exception ex)
            {
                lastEx = ex;
                _logger?.LogWarning(ex, "[Git] Attempt {Attempt} failed for args: {Args}", attempt + 1, args);
                await Task.Delay(TimeSpan.FromMilliseconds(200 * (attempt + 1)), ct);
                attempt++;
            }
        }
        _logger?.LogError(lastEx, "[Git] All attempts failed for args: {Args}", args);
        throw new InvalidOperationException("MinGit execution failed after retries", lastEx);
    }
}
