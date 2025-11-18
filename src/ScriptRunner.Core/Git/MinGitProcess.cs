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
        if (!string.IsNullOrWhiteSpace(_options.MinGitPath) && File.Exists(_options.MinGitPath))
        {
            _logger?.LogDebug("[Git] Using configured MinGitPath: {Path}", _options.MinGitPath);
            return _options.MinGitPath;
        }
        // Attempt to resolve from application base directory (e.g., if package content copied)
        var baseDir = AppContext.BaseDirectory;
        var local = Path.Combine(baseDir, "git.exe");
        if (File.Exists(local))
        {
            _logger?.LogDebug("[Git] Using git.exe next to app: {Path}", local);
            return local;
        }
        // Try Tools\MinGit\git.exe relative to base
        var toolsMinGit = Path.Combine(baseDir, "Tools", "MinGit", "git.exe");
        if (File.Exists(toolsMinGit))
        {
            _logger?.LogDebug("[Git] Using Tools/MinGit/git.exe: {Path}", toolsMinGit);
            return toolsMinGit;
        }
        // Try Tools\PortableGit\cmd\git.exe relative to base (portable Git layout)
        var toolsPortableGit = Path.Combine(baseDir, "Tools", "PortableGit", "cmd", "git.exe");
        if (File.Exists(toolsPortableGit))
        {
            _logger?.LogDebug("[Git] Using Tools/PortableGit/cmd/git.exe: {Path}", toolsPortableGit);
            return toolsPortableGit;
        }
        throw new InvalidOperationException("MinGit git.exe not found. Configure ScriptRepo:MinGitPath or deploy git.exe next to the service.");
    }

    public async Task<(int exitCode, string stdout, string stderr)> RunAsync(string args, string? workingDir = null, CancellationToken ct = default)
    {
        var gitPath = ResolveGitPath();
        var cmdPreview = BuildCommandPreview(gitPath, args, workingDir);
        var attempt = 0;
        Exception? lastEx = null;
        while (attempt < 3)
        {
            try
            {
                _logger?.LogInformation("[Git] Attempt {Attempt} executing: {Cmd}", attempt + 1, cmdPreview);
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
                var patSet = !string.IsNullOrEmpty(pat);
                if (patSet) psi.Environment["GIT_HTTP_PASSWORD"] = pat!;
                if (!string.IsNullOrWhiteSpace(_options.ActiveProvider.Proxy)) psi.Environment["HTTPS_PROXY"] = _options.ActiveProvider.Proxy;
                _logger?.LogDebug("[Git] Env: GIT_CONFIG_NOSYSTEM=1, GIT_TERMINAL_PROMPT=0, PAT set={PatSet}, Proxy set={ProxySet}", patSet, !string.IsNullOrWhiteSpace(_options.ActiveProvider.Proxy));

                using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
                var sw = Stopwatch.StartNew();
                proc.Start();
                _logger?.LogDebug("[Git] Started process pid={Pid}", proc.Id);

                using var reg = ct.Register(() =>
                {
                    try
                    {
                        if (!proc.HasExited)
                        {
                            _logger?.LogWarning("[Git] Cancelling git process pid={Pid}; killing tree", proc.Id);
                            proc.Kill(entireProcessTree: true);
                        }
                    }
                    catch (Exception killEx)
                    {
                        _logger?.LogWarning(killEx, "[Git] Failed to kill git process pid={Pid}", proc.Id);
                    }
                });

                var stdoutTask = proc.StandardOutput.ReadToEndAsync();
                var stderrTask = proc.StandardError.ReadToEndAsync();
                await Task.WhenAll(stdoutTask, stderrTask);
                await proc.WaitForExitAsync(ct);
                sw.Stop();

                var stdout = stdoutTask.Result;
                var stderr = stderrTask.Result;
                var sample = stdout.Length > 300 ? stdout.Substring(0, 300) + "..." : stdout;
                _logger?.LogDebug("[Git] Completed command (code={Code}) in {Ms} ms, stdoutLen={Len} stderrLen={ErrLen} sample=\n{Sample}", proc.ExitCode, sw.ElapsedMilliseconds, stdout.Length, stderr.Length, sample);
                if (proc.ExitCode != 0)
                {
                    _logger?.LogWarning("[Git] Non-zero exit ({Code}) for: {Cmd}. stderr=\n{Err}", proc.ExitCode, cmdPreview, stderr);
                }
                return (proc.ExitCode, stdout, stderr);
            }
            catch (Exception ex)
            {
                lastEx = ex;
                _logger?.LogWarning(ex, "[Git] Attempt {Attempt} failed for: {Cmd}", attempt + 1, cmdPreview);
                await Task.Delay(TimeSpan.FromMilliseconds(200 * (attempt + 1)), ct);
                attempt++;
            }
        }
        _logger?.LogError(lastEx, "[Git] All attempts failed for: {Cmd}", cmdPreview);
        throw new InvalidOperationException("MinGit execution failed after retries", lastEx);
    }

    private static string BuildCommandPreview(string gitPath, string args, string? cwd)
    {
        var quotedGit = '"' + gitPath + '"';
        var cmd = $"{quotedGit} {args}";
        if (!string.IsNullOrWhiteSpace(cwd))
        {
            return $"cd \"{cwd}\" && {cmd}";
        }
        return cmd;
    }
}
