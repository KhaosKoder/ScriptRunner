# ScriptRunner Service – Full Requirements & Specification (v2)

## 1. Overview

ScriptRunner is a **secure Windows-hosted automation service** that provides:

* A **REST API** hosted on **HTTPS** using **HTTP.sys** + **Windows Authentication**.
* A professional **Vue 3** web UI served from the ASP.NET app.
* Dynamic discovery, display, and execution of:

  * **PowerShell scripts**
  * **SQL scripts**
* On-demand retrieval of scripts from a **remote Git repository** (Azure DevOps or GitHub).
* Automatic cleanup: Scripts are **never stored permanently** on the machine.
* Full execution output (stdout/stderr) is:

  * Returned to the UI
  * Saved in execution history
  * **Emailed** to the user who executed it

Security is a top priority:
Only authenticated Windows users can access the application, and all requests must go through HTTPS.

---

# 2. Hosting & Security Requirements

## 2.1 Windows Service Host

* Runs as a **Windows Service** using `.UseWindowsService()`.
* Default identity: **LocalSystem** (but configurable).
* The service account must have:

  * Rights to execute PowerShell & SQL scripts
  * Network access to Git
  * Rights to open the HTTPS port

## 2.2 API Hosting: HTTP.sys + Windows Authentication

* The API **must use HTTP.sys**, not Kestrel:

  * Supports Windows Authentication natively.
  * Uses OS-level HTTPS certificate bindings.
* Enable:

  * **HTTPS ONLY**
  * **Windows Authentication ONLY**
  * No anonymous, no basic auth.

### 2.2.1 User Identity

* Every API call exposes:

  * `HttpContext.User.Identity.Name`
* This Windows username is used to:

  * Identify who ran a script
  * Lookup the user’s email address

## 2.3 User Email Mapping

* A simple config file (JSON) maps Windows usernames → email addresses:

```json
{
  "UserEmailMap": {
    "DOMAIN\\jdoe": "jdoe@example.com",
    "DOMAIN\\a.smith": "asmith@example.com"
  }
}
```

* Lookup rules:

  1. Exact match on DOMAIN\username
  2. If not found → log warning → skip email

---

# 3. Git Integration (Azure DevOps + GitHub)

## 3.1 No Local Script Storage

**Scripts must not live on disk persistently.**
Instead:

1. API lists scripts via Git API / Git tree requests.
2. When a user views or runs a script:

   * The system **downloads on demand**:

     * Script content
     * Metadata header
3. Script is saved to a **temp folder**:

   * e.g. `%ProgramData%\ScriptRunner\TempScripts\<guid>`
4. Script is deleted:

   * Immediately after execution
   * OR when the user moves to another script
   * OR when the app determines the script is no longer needed

## 3.2 Bundled Minimal Git Client (MinGit)

* Bundle **MinGit** within the application distribution:

  * e.g. `Tools\MinGit\git.exe`
* NEVER use system-installed Git.
* NEVER modify the user’s `.gitconfig`.
* Git operations MUST use:

  * Per-process environment variables
  * Internal config files isolated from user Git

## 3.3 Switching Git Providers

Config block:

```json
{
  "ScriptRepo": {
    "Provider": "AzureDevOps", // or GitHub
    "MinGitPath": "C:\\ScriptRunner\\Tools\\MinGit\\git.exe",

    "AzureDevOps": {
      "RepoUrl": "https://dev.azure.com/org/project/_git/repo",
      "Branch": "main",
      "PAT": "env:AZDO_PAT",
      "Proxy": ""
    },

    "GitHub": {
      "RepoUrl": "https://github.com/org/repo.git",
      "Branch": "main",
      "PAT": "env:GITHUB_PAT",
      "Proxy": ""
    }
  }
}
```

## 3.4 Git Operations (On-Demand)

* **List scripts**:

  * Use `git ls-remote` or REST API for repo tree.
* **Fetch script content**:

  * `git show origin/<branch>:path/to/script.ps1`

---

# 4. Script Metadata & Parameter Definition

## 4.1 Metadata Block Format (for both PowerShell & SQL)

**Every script must start with a structured metadata block**:

```powershell
<#
SCRIPT-METADATA:
  Id: restart-iis
  Name: Restart IIS Website
  Category: IIS
  Description: Restarts a specific IIS website.

  Parameters:
    - Name: WebsiteName
      Type: string
      Required: true
      DisplayName: Website name
      Default: ""
      HelpText: "Name of the IIS website."

    - Name: Force
      Type: bool
      Required: false
      DisplayName: Force restart
      Default: false
      HelpText: "Restart even if users are connected."

  SqlConnectionString: ""  # optional; SQL only
END-SCRIPT-METADATA
#>
```

### 4.1.1 Supported Types

* `string`
* `int`
* `decimal`
* `bool`
* `datetime`
* `enum` (with list of allowed values)

### 4.1.2 SQL Connection String Rules

1. If `SqlConnectionString` is present → **use it**.
2. If missing → fall back to config:

```json
{
  "SqlConnections": {
    "ReportingDB": "Server=...;Database=...;",
    "ProdDB": "Server=...;Database=...;"
  }
}
```

The UI must let the user select a connection from the fallback list.

---

# 5. Script Types

## 5.1 PowerShell Script Execution

* Temporary script file written to disk.
* Executed via:

```
pwsh.exe -NoProfile -ExecutionPolicy Bypass -File <tempfile> <param1> <param2> ...
```

* Capture:

  * `stdout`
  * `stderr`
  * Exit code

## 5.2 SQL Script Execution

* Temporary `.sql` file written.
* Use `SqlConnection` + `SqlCommand` in C#.
* Support:

  * Parameter replacement (e.g., `$(VarName)` or `{{VarName}}`).
  * Multiple statements.
  * Transaction optional (configurable).

---

# 6. Execution Engine

## 6.1 Workflow

1. User selects script in UI.
2. Backend fetches script metadata only.
3. User fills parameter form.
4. When user clicks “Run”:

   * Script is downloaded fresh.
   * Script is saved to a unique temp folder.
   * Execution is performed.
   * Output is captured.
   * Script file is deleted.
5. History record is created.
6. Email is sent to user.
7. Results displayed in UI.

## 6.2 Concurrency

* Configurable `MaxConcurrentExecutions`.
* Everything else waits in queue (status = Queued).

## 6.3 Output Capture

* Full stdout/stderr captured.
* Truncation threshold configurable.
* Full output downloadable.

---

# 7. Execution History

Stored in SQLite.

### Table: ScriptExecution

| Column           | Type          |
| ---------------- | ------------- |
| ExecutionId (PK) | GUID          |
| ScriptId         | NVARCHAR      |
| ScriptName       | NVARCHAR      |
| StartedAtUtc     | DATETIME2     |
| FinishedAtUtc    | DATETIME2     |
| ParametersJson   | NVARCHAR(MAX) |
| Status           | NVARCHAR(20)  |
| ExitCode         | INT           |
| StdOut           | NVARCHAR(MAX) |
| StdErr           | NVARCHAR(MAX) |
| RanByUser        | NVARCHAR      |
| EmailSent        | BIT           |

---

# 8. Emailing Results

## 8.1 SMTP Config

```json
{
  "Email": {
    "SmtpHost": "smtp.server.com",
    "Port": 587,
    "From": "scriptrunner@company.com",
    "UseSsl": true,
    "Username": "",
    "Password": ""
  }
}
```

## 8.2 Email Content

Subject:

```
Script Run Results – <ScriptName>
```

Body:

* User who ran it
* Timestamp
* Parameters
* Exit code
* StdOut (formatted)
* StdErr (formatted)

---

# 9. Web UI (Vue 3 + DevExtreme from CDN)

## 9.1 General

* Pure JavaScript (no build system).
* Vue 3 global build via CDN.
* DevExtreme via CDN.
* Professional, uncluttered layout.

## 9.2 Layout

### Left Panel

* Script list
* Filter box
* Grouping by category

### Right Panel

* Parameter form (DevExtreme dxForm)
* Run button
* Execution history grid
* Output display (stdout+stderr)

---

# 10. REST API Specification

### `GET /api/scripts`

List scripts (metadata only).

### `GET /api/scripts/{id}`

Get full metadata.

### `GET /api/scripts/{id}/content`

Download script content (server fetches fresh from Git).

### `POST /api/scripts/{id}/execute`

Triggers execution.

### `GET /api/history`

Query history.

### `GET /api/history/{executionId}`

Get execution details.

---

# 11. Setup Document Requirements

A separate included document must explain:

1. How the service bundles MinGit.
2. How to configure Git provider switching via config.
3. How the Git client uses **per-process config** so:

   * User’s Git is untouched
   * No global config modified
4. How HTTPS and certificates must be bound via:

   * `netsh http add sslcert`
5. How to configure Windows Authentication for HTTP.sys.

---

# 12. Extensibility & SOLID Design

Primary interfaces:

* `IScriptProvider` (fetch metadata + content from Git)
* `IScriptParser` (parse metadata block)
* `IScriptExecutor` (PS + SQL implementations)
* `ISqlExecutor`
* `IPowerShellExecutor`
* `IExecutionHistoryStore`
* `IEmailNotifier`
* `IUserEmailResolver`

All injected via DI.

---

# 13. Non-Functional Requirements

* HTTPS required.
* Scripts must be deleted immediately after use.
* No script permanently stored on disk.
* Avoid interfering with user’s Git config or environment.
* System must handle:

  * High reliability
  * Concurrency
  * Logging
  * Auditability

---
