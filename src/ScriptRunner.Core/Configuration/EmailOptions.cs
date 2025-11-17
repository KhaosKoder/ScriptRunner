namespace ScriptRunner.Core.Configuration;

public sealed class EmailOptions
{
    public string SmtpHost { get; set; } = string.Empty;
    public int Port { get; set; } = 25;
    public string From { get; set; } = string.Empty;
    public bool UseSsl { get; set; } = true;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty; // Prefer env: style for security
}
