# ScriptRunner Setup Guide (Updated)

This guide covers installation, configuration, and operation of the ScriptRunner service.

## 1. Build and Install as Windows Service

1. Publish the service:
   - `dotnet publish ScriptRunner.Service -c Release -r win-x64 --self-contained false`
2. Install service:
   - `sc create ScriptRunner binPath= "C:\\Path\\To\\ScriptRunner.Service.exe" start= auto`
3. Grant the service account (recommended: a dedicated domain/service account) access to:
   - Network (Git, SMTP, DB)
   - Local temp folder `%ProgramData%\ScriptRunner\TempScripts`
   - HTTP reservation and certificate private key

## 2. HTTP.sys + Windows Authentication + HTTPS

ScriptRunner uses HTTP.sys and Windows Auth only.

- Bind certificate:
  - `netsh http add sslcert ipport=0.0.0.0:5001 certhash=<thumbprint> appid={AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE}`
- Reserve URL (if needed):
  - `netsh http add urlacl url=https://+:5001/ user=DOMAIN\ServiceAccount`
- HSTS is enabled in non-development automatically. Only https endpoints should be used.

## 3. Configuration (appsettings.json)

All configuration lives in `ScriptRunner.Service/appsettings.json`. Example:

```json
{
  "Logging": { "LogLevel": { "Default": "Information", "Microsoft.AspNetCore": "Warning" } },
  "AllowedHosts": "*",
  "ScriptRepo": {
    "Provider": "GitHub",
    "MinGitPath": "C:\\ScriptRunner\\Tools\\MinGit\\git.exe",
    "GitHub": { "RepoUrl": "https://github.com/KhaosKoder/ScriptRunner.git", "Branch": "main", "PAT": "env:GITHUB_PAT", "Proxy": "" },
    "AzureDevOps": { "RepoUrl": "https://dev.azure.com/yourorg/yourproject/_git/repo", "Branch": "main", "PAT": "env:AZDO_PAT", "Proxy": "" }
  },
  "SqlConnections": { "Connections": { "ReportingDB": "Data Source=C:\\Data\\reporting.db", "ProdDB": "Data Source=C:\\Data\\prod.db" } },
  "Execution": { "MaxConcurrentExecutions": 2, "OutputTruncationThreshold": 10000 },
  "Email": { "SmtpHost": "smtp.server.com", "Port": 587, "From": "scriptrunner@company.com", "UseSsl": true, "Username": "", "Password": "env:SMTP_PASSWORD" },
  "UserEmailMap": { "DOMAIN\\jdoe": "jdoe@example.com", "DOMAIN\\asmith": "asmith@example.com" },
  "AccessControl": { "RunnerGroups": ["DOMAIN\\ScriptRunners"], "ViewerGroups": ["DOMAIN\\ScriptViewers"] }
}
```

Notes:
- `MinGitPath`: If omitted, the service tries to find `git.exe` in the app folder or `Tools\\MinGit\\git.exe`.
- `PAT` supports `env:` indirection (recommended). Set `GITHUB_PAT` / `AZDO_PAT` on the service account.
- SQLite is used for history; DB file at `%ProgramData%\ScriptRunner\history.db`.

## 4. MinGit Bundling

- Place MinGit under `Tools\MinGit\git.exe` in the deployment folder or specify the absolute path in `MinGitPath`.
- The service uses per-process Git env: `GIT_CONFIG_NOSYSTEM=1`, `GIT_TERMINAL_PROMPT=0`. No user/global Git config is modified.

## 5. Script Repository Access

- Scripts are fetched on-demand from the configured branch using MinGit `ls-tree` and `show`.
- No persistent clones are kept; temp work directories are deleted after use.
- A short in-memory cache reduces repeated Git calls (30 seconds).

## 6. PowerShell & SQL Execution

- PowerShell 7 (`pwsh.exe`) must be available on PATH for the service account.
- The backend writes scripts to `%ProgramData%\ScriptRunner\TempScripts\<guid>\script.ps1` or `.sql` and deletes them after execution.
- SQL execution uses SQLite via connection strings configured in `SqlConnections` or per-script metadata.

## 7. Execution Concurrency & Queueing

- Configured via `Execution:MaxConcurrentExecutions`.
- A semaphore limits concurrent runs; queued jobs return HTTP 202 with a link to `/api/history/{executionId}`.
- A cancel endpoint exists: `POST /api/cancel/{executionId}` (Windows Auth required).

## 8. Security & Access Control

- Windows Authentication only; Anonymous disabled.
- Authorization policies:
  - `CanView`: members of ViewerGroups or RunnerGroups.
  - `CanRun`: members of RunnerGroups.
- Configure groups under `AccessControl` section.

## 9. Email Notifications

- Results are emailed to the Windows user if an address is resolved via `UserEmailMap`.
- SMTP credentials can be provided via environment variable indirection.

## 10. Health & Readiness

- Health: `GET /health` returns 200.
- Readiness: `GET /ready` returns 200 with status.

## 11. UI

- Open `https://host:port/` to access the Vue 3 UI.
- Parameter forms are generated dynamically based on script metadata (types, enum values).
- Running state indicator and Cancel button appear for queued/running executions.
- Full output download available from history.

## 12. Logging & Auditing

- Structured logs with execution correlation scopes (`ExecutionTraceId`, `ExecutionId`).
- History recorded in SQLite; retention default purges entries older than 30 days.

## 13. Retention and Cleanup

- History retention: 30 days (hard-coded in this build; make configurable if needed).
- Output truncation stored in DB (`Execution:OutputTruncationThreshold` controls per-response truncation).
- Background temp cleanup removes orphan folders older than 1 hour.

## 14. Upgrades

1. `sc stop ScriptRunner`
2. Replace binaries/appsettings.
3. `sc start ScriptRunner`

## 15. Troubleshooting

- Check Windows Event Log and application logs.
- Git failures: confirm PAT env vars and outbound network.
- HTTPS binding issues: re-validate `netsh` bindings and certificate permissions.
- Email: verify SMTP connectivity, credentials, and `UserEmailMap` mapping.