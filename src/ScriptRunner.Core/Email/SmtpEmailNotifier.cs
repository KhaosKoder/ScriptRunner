using Microsoft.Extensions.Logging;
using ScriptRunner.Core.Configuration;
using System.Net;
using System.Net.Mail;
using System.Text;

namespace ScriptRunner.Core.Email;

public sealed class SmtpEmailNotifier : IEmailNotifier
{
    private readonly EmailOptions _options;
    private readonly IUserEmailResolver _resolver;
    private readonly ILogger<SmtpEmailNotifier> _logger;

    public SmtpEmailNotifier(EmailOptions options, IUserEmailResolver resolver, ILogger<SmtpEmailNotifier> logger)
    {
        _options = options;
        _resolver = resolver;
        _logger = logger;
    }

    public async Task<bool> SendResultsAsync(ScriptExecutionRecord record, CancellationToken ct = default)
    {
        var to = _resolver.Resolve(record.RanByUser);
        if (string.IsNullOrWhiteSpace(to))
        {
            _logger.LogWarning("Skipping email: no recipient for {User}", record.RanByUser);
            return false;
        }
        try
        {
            using var client = new SmtpClient(_options.SmtpHost, _options.Port)
            {
                EnableSsl = _options.UseSsl
            };
            if (!string.IsNullOrWhiteSpace(_options.Username))
            {
                client.Credentials = new NetworkCredential(_options.Username, ResolvePassword(_options.Password));
            }
            var msg = new MailMessage(_options.From, to)
            {
                Subject = $"Script Run Results – {record.ScriptName}",
                Body = BuildBody(record),
                IsBodyHtml = false
            };
            await client.SendMailAsync(msg, ct);
            _logger.LogInformation("Email sent to {To} for execution {Exec}", to, record.ExecutionId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed sending email for execution {Exec}", record.ExecutionId);
            return false;
        }
    }

    private static string BuildBody(ScriptExecutionRecord r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"User: {r.RanByUser}");
        sb.AppendLine($"Timestamp: {r.FinishedAtUtc:o}");
        sb.AppendLine($"Script: {r.ScriptName} ({r.ScriptId})");
        sb.AppendLine($"Exit Code: {r.ExitCode}");
        sb.AppendLine($"Status: {r.Status}");
        sb.AppendLine("Parameters:");
        sb.AppendLine(r.ParametersJson);
        sb.AppendLine("StdOut:");
        sb.AppendLine(r.StdOut);
        sb.AppendLine("StdErr:");
        sb.AppendLine(r.StdErr);
        return sb.ToString();
    }

    private static string ResolvePassword(string pw)
    {
        if (string.IsNullOrWhiteSpace(pw)) return string.Empty;
        if (pw.StartsWith("env:", StringComparison.OrdinalIgnoreCase))
        {
            var envName = pw.Substring(4);
            return Environment.GetEnvironmentVariable(envName) ?? string.Empty;
        }
        return pw;
    }
}
