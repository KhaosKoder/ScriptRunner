using ScriptRunner.Core;
using ScriptRunner.Core.Parsing;

namespace ScriptRunner.Service;

internal sealed class InMemoryScriptProvider : IScriptProvider
{
    private readonly IScriptParser _parser;
    private readonly Dictionary<string, string> _scripts = new();

    public InMemoryScriptProvider(IScriptParser parser)
    {
        _parser = parser;
        var content = @"<#
SCRIPT-METADATA:
  Id: demo-hello
  Name: Demo Hello
  Category: Demo
  Description: Says hello from PowerShell.

  Parameters:
    - Name: Name
      Type: String
      Required: false
      Default: ""World""
      DisplayName: Your name
      HelpText: ""Optional addressee""
END-SCRIPT-METADATA
#>
Write-Output ""Hello $Name""";
        _scripts["demo-hello"] = content;
    }

    public Task<IReadOnlyList<ScriptMetadata>> ListScriptsAsync(CancellationToken ct = default)
    {
        var list = _scripts.Values.Select(c => _parser.Parse(c)).ToList();
        return Task.FromResult<IReadOnlyList<ScriptMetadata>>(list);
    }

    public Task<ScriptMetadata?> GetScriptMetadataAsync(string id, CancellationToken ct = default)
    {
        if (_scripts.TryGetValue(id, out var content))
        {
            return Task.FromResult<ScriptMetadata?>(_parser.Parse(content));
        }
        return Task.FromResult<ScriptMetadata?>(null);
    }

    public Task<string> GetScriptContentAsync(string id, CancellationToken ct = default)
    {
        if (_scripts.TryGetValue(id, out var content))
        {
            return Task.FromResult(content);
        }
        return Task.FromResult(string.Empty);
    }
}
