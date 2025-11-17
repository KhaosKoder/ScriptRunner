namespace ScriptRunner.Core.Configuration;

public sealed class SqlConnectionsOptions
{
    public Dictionary<string,string> Connections { get; set; } = new();
}
