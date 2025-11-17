using ScriptRunner.Core.Configuration;
using ScriptRunner.Core.Parsing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Caching.Memory;

namespace ScriptRunner.Core.Git;

public sealed class GitScriptProvider : IScriptProvider
{
    private readonly ScriptRepoOptions _options;
    private readonly MinGitProcess _git;
    private readonly IScriptParser _parser;
    private readonly ILogger<GitScriptProvider> _logger;
    private readonly IMemoryCache _cache;

    public GitScriptProvider(ScriptRepoOptions options, IScriptParser parser, ILogger<GitScriptProvider> logger)
    {
        _options = options;
        _parser = parser;
        _logger = logger;
        _git = new MinGitProcess(options, logger as ILogger<MinGitProcess>);
        _cache = new MemoryCache(new MemoryCacheOptions());
    }

    public async Task<IReadOnlyList<ScriptMetadata>> ListScriptsAsync(CancellationToken ct = default)
    {
        if (_cache.TryGetValue("script_list", out IReadOnlyList<ScriptMetadata> cached))
            return cached;
        var tempDir = CreateTempWorkDir();
        try
        {
            var repoUrl = _options.ActiveProvider.RepoUrl;
            var branch = _options.ActiveProvider.Branch;
            var cloneArgs = $"clone --depth=1 --no-checkout --filter=blob:none {Escape(repoUrl)} .";
            var (code, _, err) = await _git.RunAsync(cloneArgs, tempDir, ct);
            if (code != 0) throw new InvalidOperationException("git clone failed: " + err);
            _logger.LogInformation("[GitProvider] Clone depth=1 completed for {Repo} branch={Branch}", repoUrl, branch);
            // List tracked files in branch tree
            var (revCode, revStd, revErr) = await _git.RunAsync($"rev-parse {Escape(branch)}", tempDir, ct);
            if (revCode != 0) throw new InvalidOperationException("rev-parse failed: " + revErr);
            var commit = revStd.Trim();
            var (treeCode, treeStd, treeErr) = await _git.RunAsync($"ls-tree -r --name-only {commit}", tempDir, ct);
            if (treeCode != 0) throw new InvalidOperationException("ls-tree failed: " + treeErr);
            var filePaths = treeStd.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            _logger.LogInformation("[GitProvider] Enumerated {Count} repository paths", filePaths.Length);
            var metas = new List<ScriptMetadata>();
            foreach (var path in filePaths)
            {
                if (!IsScript(path)) continue;
                try
                {
                    var meta = await FetchMetadataAsync(path, branch, tempDir, ct);
                    if (meta != null)
                    {
                        metas.Add(new ScriptMetadata
                        {
                            Id = meta.Id,
                            Name = meta.Name,
                            Category = meta.Category,
                            Description = meta.Description,
                            Parameters = meta.Parameters,
                            SqlConnectionString = meta.SqlConnectionString,
                            SourcePath = path
                        });
                        _logger.LogDebug("[GitProvider] Parsed metadata Id={Id} from path={Path}", meta.Id, path);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed parsing metadata for {Path}", path);
                }
            }
            _logger.LogInformation("[GitProvider] Returning {Count} script metadata entries", metas.Count);
            _cache.Set("script_list", metas, TimeSpan.FromSeconds(30));
            return metas;
        }
        finally
        {
            SafeDelete(tempDir);
        }
    }

    public async Task<ScriptMetadata?> GetScriptMetadataAsync(string id, CancellationToken ct = default)
    {
        var all = await ListScriptsAsync(ct);
        return all.FirstOrDefault(m => m.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<string> GetScriptContentAsync(string id, CancellationToken ct = default)
    {
        if (_cache.TryGetValue("content_" + id, out string cached)) return cached;
        var all = await ListScriptsAsync(ct);
        var meta = all.FirstOrDefault(m => m.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (meta == null || meta.SourcePath == null) return string.Empty;
        var tempDir = CreateTempWorkDir();
        try
        {
            await CloneBare(tempDir, ct);
            var branch = _options.ActiveProvider.Branch;
            var raw = await ShowFileAsync(meta.SourcePath, branch, tempDir, ct);
            _cache.Set("content_" + id, raw, TimeSpan.FromSeconds(30));
            return raw;
        }
        finally
        {
            SafeDelete(tempDir);
        }
    }

    private async Task<ScriptMetadata?> FetchMetadataAsync(string path, string branch, string tempDir, CancellationToken ct)
    {
        var raw = await ShowFileAsync(path, branch, tempDir, ct);
        return _parser.Parse(raw);
    }

    private async Task<string> ShowFileAsync(string path, string branch, string tempDir, CancellationToken ct)
    {
        var (code, stdout, err) = await _git.RunAsync($"show {Escape(branch)}:{Escape(path)}", tempDir, ct);
        if (code != 0) throw new InvalidOperationException("git show failed for " + path + ": " + err);
        return stdout;
    }

    private async Task CloneBare(string tempDir, CancellationToken ct)
    {
        var repoUrl = _options.ActiveProvider.RepoUrl;
        var cloneArgs = $"clone --depth=1 --no-checkout --filter=blob:none {Escape(repoUrl)} .";
        var (code, _, err) = await _git.RunAsync(cloneArgs, tempDir, ct);
        if (code != 0) throw new InvalidOperationException("git clone failed: " + err);
    }

    private async Task<IReadOnlyList<string>> ListPaths(string tempDir, string branch, CancellationToken ct)
    {
        var (revCode, revStd, revErr) = await _git.RunAsync($"rev-parse {Escape(branch)}", tempDir, ct);
        if (revCode != 0) throw new InvalidOperationException("rev-parse failed: " + revErr);
        var commit = revStd.Trim();
        var (treeCode, treeStd, treeErr) = await _git.RunAsync($"ls-tree -r --name-only {commit}", tempDir, ct);
        if (treeCode != 0) throw new InvalidOperationException("ls-tree failed: " + treeErr);
        return treeStd.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static bool IsScript(string path)
    {
        return path.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".sql", StringComparison.OrdinalIgnoreCase);
    }

    private static string CreateTempWorkDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ScriptRunnerGit", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void SafeDelete(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, true);
        }
        catch { /* swallow */ }
    }

    private static string Escape(string value) => value.Replace("\"", "\\\"");
}
