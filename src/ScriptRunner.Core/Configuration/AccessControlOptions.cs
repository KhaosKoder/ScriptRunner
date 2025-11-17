namespace ScriptRunner.Core.Configuration;

public sealed class AccessControlOptions
{
    public string[] RunnerGroups { get; set; } = Array.Empty<string>();
    public string[] ViewerGroups { get; set; } = Array.Empty<string>();
}
