using ScriptRunner.Core;
using ScriptRunner.Core.Configuration;
using ScriptRunner.Core.Parsing;
using ScriptRunner.Core.Git;
using ScriptRunner.Core.Storage;
using ScriptRunner.Core.Execution;
using ScriptRunner.Core.History;
using ScriptRunner.Core.Users;
using ScriptRunner.Core.Email;
using ScriptRunner.Core.Validation;
using Microsoft.AspNetCore.Server.HttpSys;
using Microsoft.Extensions.Hosting.WindowsServices;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using ScriptRunner.Service.Controllers;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using System.Security.Principal;
using ScriptRunner.Service; // for InMemoryScriptProvider
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.Extensions.Logging;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

// Logging configuration
builder.Logging.ClearProviders();
builder.Logging.AddConsole(options =>
{
    options.IncludeScopes = true;
});
// Enrich with activity/trace details for better correlation
builder.Logging.Configure(options =>
{
    options.ActivityTrackingOptions = ActivityTrackingOptions.SpanId | ActivityTrackingOptions.TraceId | ActivityTrackingOptions.ParentId | ActivityTrackingOptions.Baggage | ActivityTrackingOptions.Tags;
});

var isDev = builder.Environment.IsDevelopment();

// Http logging (requests/responses)
builder.Services.AddHttpLogging(o =>
{
    o.LoggingFields = HttpLoggingFields.RequestPropertiesAndHeaders | HttpLoggingFields.ResponsePropertiesAndHeaders | HttpLoggingFields.RequestQuery | HttpLoggingFields.RequestBody | HttpLoggingFields.ResponseBody;
    o.RequestBodyLogLimit = 4096;
    o.ResponseBodyLogLimit = 4096;
    o.MediaTypeOptions.AddText("application/json");
    o.MediaTypeOptions.AddText("text/plain");
});

// JSON: serialize enums as strings so UI receives readable status values
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

// CORS (helpful when front-end is on a different origin during dev)
if (isDev)
{
    builder.Services.AddCors(options =>
    {
        options.AddDefaultPolicy(p => p
            .AllowAnyOrigin()
            .AllowAnyHeader()
            .AllowAnyMethod());
    });
}

// Options binding
var repoOptions = builder.Configuration.GetSection("ScriptRepo").Get<ScriptRepoOptions>() ?? new ScriptRepoOptions();
if (string.IsNullOrWhiteSpace(repoOptions.MinGitPath))
{
    var possible = Path.Combine(AppContext.BaseDirectory, "git.exe");
    if (File.Exists(possible)) repoOptions.MinGitPath = possible;
}
builder.Services.AddSingleton(repoOptions);

var sqlConnOptions = builder.Configuration.GetSection("SqlConnections").Get<SqlConnectionsOptions>() ?? new SqlConnectionsOptions();
builder.Services.AddSingleton(sqlConnOptions);
var execOptions = builder.Configuration.GetSection("Execution").Get<ExecutionOptions>() ?? new ExecutionOptions();
builder.Services.AddSingleton(execOptions);
var emailOptions = builder.Configuration.GetSection("Email").Get<EmailOptions>() ?? new EmailOptions();
builder.Services.AddSingleton(emailOptions);

var accessOptions = builder.Configuration.GetSection("AccessControl").Get<AccessControlOptions>() ?? new AccessControlOptions();
builder.Services.AddSingleton(accessOptions);

builder.Services.AddSingleton(_ => new SemaphoreSlim(execOptions.MaxConcurrentExecutions, execOptions.MaxConcurrentExecutions));

// Use HttpSys + Windows Service only outside development
if (!isDev)
{
    builder.WebHost.UseHttpSys(options =>
    {
        options.Authentication.Schemes = AuthenticationSchemes.Negotiate | AuthenticationSchemes.NTLM;
        options.Authentication.AllowAnonymous = false;
    });

    builder.Host.UseWindowsService();

    builder.Services.AddAuthentication(HttpSysDefaults.AuthenticationScheme);
    builder.Services.AddAuthorization(options =>
    {
        options.AddPolicy("CanRun", policy => policy.RequireAssertion(ctx =>
        {
            var wi = ctx.User.Identity as WindowsIdentity;
            if (wi == null) return false;
            return accessOptions.RunnerGroups.Any(g => ctx.User.IsInRole(g));
        }));
        options.AddPolicy("CanView", policy => policy.RequireAssertion(ctx =>
        {
            var wi = ctx.User.Identity as WindowsIdentity;
            if (wi == null) return false;
            return accessOptions.ViewerGroups.Any(g => ctx.User.IsInRole(g)) || accessOptions.RunnerGroups.Any(g => ctx.User.IsInRole(g));
        }));
    });
}
else
{
    // Dev: no auth requirements to simplify local testing with Kestrel
    builder.Services.AddAuthorization();
}

// HSTS (non-dev)
if (!isDev)
{
    builder.Services.AddHsts(o => { o.MaxAge = TimeSpan.FromDays(365); o.IncludeSubDomains = true; });
}

// Health checks
builder.Services.AddHealthChecks();

// Core services
builder.Services.AddSingleton<IScriptParser, ScriptMetadataParser>();

// Choose script provider based on environment and MinGit presence
bool minGitPresent = (!string.IsNullOrWhiteSpace(repoOptions.MinGitPath) && File.Exists(repoOptions.MinGitPath))
                     || File.Exists(Path.Combine(AppContext.BaseDirectory, "git.exe"))
                     || File.Exists(Path.Combine(AppContext.BaseDirectory, "Tools", "MinGit", "git.exe"));
if (isDev && !minGitPresent)
{
    builder.Services.AddSingleton<IScriptProvider, InMemoryScriptProvider>();
}
else
{
    builder.Services.AddSingleton<IScriptProvider, GitScriptProvider>();
}

builder.Services.AddSingleton<ITempScriptStorage, TempScriptStorage>();
builder.Services.AddSingleton<IPowerShellExecutor, SimpleScriptExecutor>();
builder.Services.AddSingleton<ISqlExecutor, SqlScriptExecutor>();
builder.Services.AddSingleton<IScriptExecutor, CompositeScriptExecutor>();
builder.Services.AddSingleton<IExecutionHistoryStore, SqliteExecutionHistoryStore>();
builder.Services.AddSingleton<IUserEmailResolver, UserEmailResolver>();
builder.Services.AddSingleton<IEmailNotifier, SmtpEmailNotifier>();
builder.Services.AddSingleton<ParameterValidator>();

builder.Services.AddOpenApi();

// Register background cleanup service
builder.Services.AddHostedService<TempCleanupService>();

var app = builder.Build();
var logger = app.Logger;

// HTTP logging middleware (built-in)
app.UseHttpLogging();

// CORS
if (isDev)
{
    app.UseCors();
}

// Correlation + request logging middleware
app.Use(async (ctx, next) =>
{
    var correlationId = ctx.Request.Headers.TryGetValue("X-Correlation-ID", out var existing) && !string.IsNullOrWhiteSpace(existing)
        ? existing.ToString()
        : Guid.NewGuid().ToString();
    ctx.Response.Headers["X-Correlation-ID"] = correlationId;

    var user = ctx.User?.Identity?.Name ?? "anonymous";
    var remoteIp = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    var ua = ctx.Request.Headers.UserAgent.ToString();

    var scopeState = new Dictionary<string, object?>
    {
        ["CorrelationId"] = correlationId,
        ["RequestId"] = ctx.TraceIdentifier,
        ["User"] = user,
        ["RemoteIp"] = remoteIp
    };

    var sw = System.Diagnostics.Stopwatch.StartNew();
    using (logger.BeginScope(scopeState))
    {
        logger.LogInformation("[REQ] {Method} {Path}{Query} from {RemoteIp} UA={UA}", ctx.Request.Method, ctx.Request.Path, ctx.Request.QueryString, remoteIp, ua);
        try
        {
            await next();
        }
        finally
        {
            sw.Stop();
            var length = ctx.Response.ContentLength?.ToString() ?? "?";
            logger.LogInformation("[RES] {Method} {Path} -> {Status} in {Elapsed} ms len={Length}", ctx.Request.Method, ctx.Request.Path, ctx.Response.StatusCode, sw.ElapsedMilliseconds, length);
        }
    }
});

// Global error envelope middleware
app.Use(async (ctx, next) =>
{
    try { await next(); }
    catch (Exception ex)
    {
        var traceId = ctx.Response.Headers.TryGetValue("X-Correlation-ID", out var cid) ? cid.ToString() : Guid.NewGuid().ToString();
        logger.LogError(ex, "Unhandled exception {TraceId}", traceId);
        ctx.Response.StatusCode = 500;
        await ctx.Response.WriteAsJsonAsync(new { traceId, message = "Internal Server Error", details = app.Environment.IsDevelopment() ? ex.Message : null });
    }
});

if (!isDev)
{
    app.UseHsts();
}

app.UseHttpsRedirection();
if (!isDev)
{
    app.UseAuthentication();
}
app.UseAuthorization();
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapFallbackToFile("/index.html");

// Health endpoints
app.MapHealthChecks("/health");
app.MapGet("/ready", () => Results.Ok(new { status = "ready" }));

// Helper to conditionally require auth
RouteHandlerBuilder Require(RouteHandlerBuilder b, string policy) => isDev ? b : b.RequireAuthorization(policy);

// Helper to get current trace/correlation id
string GetTraceId(HttpContext http)
{
    if (http.Response.Headers.TryGetValue("X-Correlation-ID", out var cid)) return cid.ToString();
    return http.TraceIdentifier;
}

// Script list & metadata
Require(app.MapGet("/api/scripts", async (IScriptProvider provider, HttpContext http, CancellationToken ct) =>
{
    var trace = GetTraceId(http);
    logger.LogDebug("Listing scripts traceId={TraceId}", trace);
    logger.LogInformation("[API] GET /api/scripts request received traceId={TraceId}", trace);
    var scripts = await provider.ListScriptsAsync(ct);
    logger.LogInformation("[API] GET /api/scripts returning {Count} items traceId={TraceId}", scripts.Count, trace);
    return Results.Ok(scripts);
}), "CanView");

Require(app.MapGet("/api/scripts/{id}", async (string id, IScriptProvider provider, HttpContext http, CancellationToken ct) =>
{
    var trace = GetTraceId(http);
    logger.LogInformation("[API] GET /api/scripts/{Id} traceId={TraceId}", id, trace);
    var meta = await provider.GetScriptMetadataAsync(id, ct);
    if (meta is null)
    {
        logger.LogWarning("Metadata not found for script id {Id} traceId={TraceId}", id, trace);
        return Results.NotFound(new { message = "Not Found", traceId = trace });
    }
    return Results.Ok(meta);
}), "CanView");

Require(app.MapGet("/api/scripts/{id}/content", async (string id, IScriptProvider provider, HttpContext http, CancellationToken ct) =>
{
    var trace = GetTraceId(http);
    logger.LogInformation("[API] GET /api/scripts/{Id}/content traceId={TraceId}", id, trace);
    var content = await provider.GetScriptContentAsync(id, ct);
    if (string.IsNullOrEmpty(content))
    {
        logger.LogWarning("Content not found for script id {Id} traceId={TraceId}", id, trace);
        return Results.NotFound(new { message = "Not Found", traceId = trace });
    }
    return Results.Ok(content);
}), "CanView");

// Execute script (queued)
Require(app.MapPost("/api/scripts/{id}/execute", async (
    string id,
    [FromBody] Dictionary<string, object?> body,
    IScriptProvider provider,
    IScriptExecutor executor,
    IExecutionHistoryStore history,
    IEmailNotifier email,
    ParameterValidator validator,
    HttpContext http,
    ExecutionOptions execOptions,
    SemaphoreSlim semaphore,
    CancellationToken ct) =>
{
    var traceId = GetTraceId(http);
    using var scope = logger.BeginScope(new Dictionary<string, object?>{["ExecutionTraceId"] = traceId});
    logger.LogInformation("[API] POST /api/scripts/{Id}/execute requested by {User} traceId={TraceId}", id, http.User?.Identity?.Name ?? "unknown", traceId);

    var meta = await provider.GetScriptMetadataAsync(id, ct);
    if (meta is null)
    {
        logger.LogWarning("Script metadata not found id={Id} traceId={TraceId}", id, traceId);
        return Results.NotFound(new { message = "Not Found", traceId });
    }

    var ranBy = http.User.Identity?.Name ?? "unknown";
    var parameters = body ?? new Dictionary<string, object?>();

    // Log provided parameters safely (truncate values)
    var safeParams = parameters.ToDictionary(kv => kv.Key, kv => kv.Value?.ToString() is string s ? (s.Length > 200 ? s.Substring(0, 200) + "..." : s) : kv.Value);
    logger.LogDebug("[API] Execute parameters for {Id}: {Params}", id, JsonSerializer.Serialize(safeParams));

    if (!validator.Validate(meta, parameters, out var validationErr))
    {
        logger.LogWarning("[API] Validation failed for {Id}: {Err} traceId={TraceId}", id, validationErr, traceId);
        return Results.BadRequest(new { message = validationErr, traceId });
    }

    var content = await provider.GetScriptContentAsync(meta.Id, ct);
    var ext = meta.SourcePath?.EndsWith(".sql", StringComparison.OrdinalIgnoreCase) == true ? ".sql" : ".ps1";
    var tempStorage = app.Services.GetRequiredService<ITempScriptStorage>();
    var tempPath = await tempStorage.WriteTempScriptAsync(content, ext, ct);
    parameters["__tempPath"] = tempPath;

    var started = DateTime.UtcNow;
    var record = new ScriptExecutionRecord
    {
        ScriptId = meta.Id,
        ScriptName = meta.Name,
        StartedAtUtc = started,
        FinishedAtUtc = started,
        ParametersJson = System.Text.Json.JsonSerializer.Serialize(parameters),
        Status = ScriptExecutionStatus.Queued,
        ExitCode = 0,
        StdOut = string.Empty,
        StdErr = string.Empty,
        RanByUser = ranBy,
        EmailSent = false,
    };
    await history.StoreAsync(record, ct);

    logger.LogInformation("[API] Script {Id} queued executionId={ExecId} traceId={TraceId}", meta.Id, record.ExecutionId, traceId);

    _ = Task.Run(async () =>
    {
        await semaphore.WaitAsync(ct);
        using var execScope = logger.BeginScope(new Dictionary<string, object?>{["ExecutionId"] = record.ExecutionId});
        try
        {
            // mark running
            var runningRecord = new ScriptExecutionRecord
            {
                ExecutionId = record.ExecutionId,
                ScriptId = record.ScriptId,
                ScriptName = record.ScriptName,
                StartedAtUtc = record.StartedAtUtc,
                FinishedAtUtc = record.StartedAtUtc,
                ParametersJson = record.ParametersJson,
                Status = ScriptExecutionStatus.Running,
                ExitCode = 0,
                StdOut = string.Empty,
                StdErr = string.Empty,
                RanByUser = record.RanByUser,
                EmailSent = false
            };
            await history.UpdateAsync(runningRecord, ct);
            logger.LogDebug("[API] Script {Id} now Running executionId={ExecId}", record.ScriptId, record.ExecutionId);
            try
            {
                var execResult = await executor.ExecuteAsync(meta, parameters, ranBy, ct);
                var finishedRecord = new ScriptExecutionRecord
                {
                    ExecutionId = record.ExecutionId,
                    ScriptId = record.ScriptId,
                    ScriptName = record.ScriptName,
                    StartedAtUtc = record.StartedAtUtc,
                    FinishedAtUtc = DateTime.UtcNow,
                    ParametersJson = record.ParametersJson,
                    Status = execResult.Status,
                    ExitCode = execResult.ExitCode,
                    StdOut = Truncate(execResult.StdOut, execOptions.OutputTruncationThreshold),
                    StdErr = Truncate(execResult.StdErr, execOptions.OutputTruncationThreshold),
                    RanByUser = record.RanByUser,
                    EmailSent = false
                };
                await history.UpdateAsync(finishedRecord, ct);
                logger.LogInformation("[API] Script {Id} finished status={Status} executionId={ExecId}", finishedRecord.ScriptId, finishedRecord.Status, finishedRecord.ExecutionId);
                if (execResult.Status != ScriptExecutionStatus.Queued && execResult.Status != ScriptExecutionStatus.Running)
                {
                    var sent = await email.SendResultsAsync(finishedRecord, ct);
                    if (sent)
                    {
                        var emailedRecord = new ScriptExecutionRecord
                        {
                            ExecutionId = finishedRecord.ExecutionId,
                            ScriptId = finishedRecord.ScriptId,
                            ScriptName = finishedRecord.ScriptName,
                            StartedAtUtc = finishedRecord.StartedAtUtc,
                            FinishedAtUtc = finishedRecord.FinishedAtUtc,
                            ParametersJson = finishedRecord.ParametersJson,
                            Status = finishedRecord.Status,
                            ExitCode = finishedRecord.ExitCode,
                            StdOut = finishedRecord.StdOut,
                            StdErr = finishedRecord.StdErr,
                            RanByUser = finishedRecord.RanByUser,
                            EmailSent = true
                        };
                        await history.UpdateAsync(emailedRecord, ct);
                        logger.LogInformation("[API] Notification email sent executionId={ExecId}", finishedRecord.ExecutionId);
                    }
                }
            }
            catch (Exception ex)
            {
                var failedRecord = new ScriptExecutionRecord
                {
                    ExecutionId = record.ExecutionId,
                    ScriptId = record.ScriptId,
                    ScriptName = record.ScriptName,
                    StartedAtUtc = record.StartedAtUtc,
                    FinishedAtUtc = DateTime.UtcNow,
                    ParametersJson = record.ParametersJson,
                    Status = ScriptExecutionStatus.Failed,
                    ExitCode = -1,
                    StdOut = string.Empty,
                    StdErr = ex.Message,
                    RanByUser = record.RanByUser,
                    EmailSent = false
                };
                await history.UpdateAsync(failedRecord, ct);
                logger.LogError(ex, "[API] Script {Id} failed executionId={ExecId}", record.ScriptId, record.ExecutionId);
            }
        }
        finally
        {
            semaphore.Release();
        }
    }, ct);

    return Results.Accepted($"/api/history/{record.ExecutionId}", new { record.ExecutionId, traceId });
}), "CanRun");

// History list
Require(app.MapGet("/api/history", async (IExecutionHistoryStore history, HttpContext http, CancellationToken ct) =>
{
    var trace = GetTraceId(http);
    var list = await history.QueryAsync(100, ct);
    logger.LogInformation("[API] GET /api/history -> {Count} traceId={TraceId}", list.Count, trace);
    return Results.Ok(list);
}), "CanView");

// History detail
Require(app.MapGet("/api/history/{executionId}", async (Guid executionId, IExecutionHistoryStore history, HttpContext http, CancellationToken ct) =>
{
    var trace = GetTraceId(http);
    var rec = await history.GetAsync(executionId, ct);
    logger.LogInformation("[API] GET /api/history/{ExecutionId} found={Found} traceId={TraceId}", executionId, rec != null, trace);
    return rec is null ? Results.NotFound(new { message = "Not Found", traceId = trace }) : Results.Ok(rec);
}), "CanView");

// Full output
Require(app.MapGet("/api/history/{executionId}/output", async (Guid executionId, IExecutionHistoryStore history, HttpContext http, CancellationToken ct) =>
{
    var trace = GetTraceId(http);
    var rec = await history.GetAsync(executionId, ct);
    if (rec is null)
    {
        logger.LogWarning("[API] output not found for {ExecutionId} traceId={TraceId}", executionId, trace);
        return Results.NotFound(new { message = "Not Found", traceId = trace });
    }
    var full = $"STDOUT:\n{rec.StdOut}\n\nSTDERR:\n{rec.StdErr}";
    logger.LogInformation("[API] GET /api/history/{ExecutionId}/output len={Len} traceId={TraceId}", executionId, full.Length, trace);
    return Results.File(System.Text.Encoding.UTF8.GetBytes(full), "text/plain", $"{executionId}-output.txt");
}), "CanView");

app.Run();

static string Truncate(string value, int max)
{
    if (value.Length <= max) return value;
    return value.Substring(0, max) + "\n...[truncated]";
}
