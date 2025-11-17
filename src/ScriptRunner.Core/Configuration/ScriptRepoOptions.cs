namespace ScriptRunner.Core.Configuration;

public sealed class ScriptRepoOptions
{
    public string Provider { get; set; } = "GitHub"; // or AzureDevOps
    public string MinGitPath { get; set; } = string.Empty;
    public ProviderDetail AzureDevOps { get; set; } = new();
    public ProviderDetail GitHub { get; set; } = new();

    public ProviderDetail ActiveProvider => Provider switch
    {
        "AzureDevOps" => AzureDevOps,
        _ => GitHub
    };

    public sealed class ProviderDetail
    {
        public string RepoUrl { get; set; } = string.Empty;
        public string Branch { get; set; } = "main";
        public string PAT { get; set; } = string.Empty; // "env:VAR" indirection
        public string Proxy { get; set; } = string.Empty;

        public string? ResolvePat()
        {
            if (string.IsNullOrWhiteSpace(PAT)) return null;
            if (PAT.StartsWith("env:", StringComparison.OrdinalIgnoreCase))
            {
                var varName = PAT.Substring(4);
                return Environment.GetEnvironmentVariable(varName);
            }
            return PAT; // direct value (discouraged, but supported)
        }
    }
}
