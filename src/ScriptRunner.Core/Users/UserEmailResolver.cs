using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using System.Text.Json;

namespace ScriptRunner.Core.Users;

public sealed class UserEmailResolver : IUserEmailResolver
{
    private readonly ILogger<UserEmailResolver> _logger;
    private readonly Dictionary<string,string> _map;

    public UserEmailResolver(ILogger<UserEmailResolver> logger, IConfiguration config)
    {
        _logger = logger;
        _map = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        var section = config.GetSection("UserEmailMap");
        if (section.Exists())
        {
            foreach (var child in section.GetChildren())
            {
                _map[child.Key] = child.Value ?? string.Empty;
            }
        }
    }

    public string? Resolve(string windowsIdentityName)
    {
        if (_map.TryGetValue(windowsIdentityName, out var email) && !string.IsNullOrWhiteSpace(email))
        {
            return email;
        }
        _logger.LogWarning("Email mapping not found for user {User}", windowsIdentityName);
        return null;
    }
}
