# Implementation Task List (Derived from Specification)

1. Solution & Project Setup
   - Ensure Core, Service, Tests projects reference required packages (MinGit, Windows Service hosting, SQLite, PowerShell).

2. Domain Models & Interfaces
   - Define metadata models, parameter types, execution result & record, interfaces (IScriptProvider, IScriptParser, IScriptExecutor, IPowerShellExecutor, ISqlExecutor, IExecutionHistoryStore, IEmailNotifier, IUserEmailResolver).

3. Metadata Parsing
   - Implement robust parser for SCRIPT-METADATA block (support all parameter fields, enum values, optional SqlConnectionString).
   - Unit tests for parser.

4. Git Script Provider
   - Implement IScriptProvider using embedded MinGit (on-demand show/list retrieval without persistent clone; use temp work directory).
   - Config binding for ScriptRepo section & provider switching (AzureDevOps vs GitHub).

5. Temporary Storage & Cleanup
   - Implement temp folder lifecycle manager to create unique script folders and eagerly delete after execution.

6. Execution Engine Orchestration
   - Implement queue with MaxConcurrentExecutions & statuses (Queued, Running, etc.).
   - Service for accepting execute requests and delegating to proper executor (PS/SQL).

7. PowerShell Executor
   - Implement transient file creation, parameter passing, capture stdout/stderr/exit code.

8. SQL Executor
   - Implement parameter token replacement, multi-statement execution, optional transaction, connection string selection logic.

9. History Persistence (SQLite)
   - Implement IExecutionHistoryStore using Microsoft.Data.Sqlite, create table if not exists, CRUD operations.

10. Email Notifier
   - Implement SMTP email sending per config, formatting body.

11. User Email Resolver
   - Implement JSON config mapping & resolution logic with logging for misses.

12. REST API Endpoints
   - Implement endpoints for scripts, metadata, content, execute, history list/detail.
   - Enforce authentication & capture Windows identity.

13. Authentication & Hosting
   - Configure HTTP.sys hosting with Windows Authentication only, HTTPS enforcement.
   - Windows Service registration.

14. Vue 3 + DevExtreme Static UI
   - Serve index.html & assets from Service project.
   - Implement minimal JS consuming API endpoints (script list, parameter form, execution).

15. Output Management
   - Implement truncation logic configurable, full output downloadable endpoint.

16. Configuration Binding & Options
   - Strongly-typed options classes (ScriptRepoOptions, EmailOptions, SqlConnectionsOptions, ExecutionOptions).

17. Logging & Audit
   - Add structured logging for all operations (start/finish, queue transitions, cleanup, email attempts).

18. Cleanup & Hardening
   - Ensure scripts deleted after use, secure temp paths, validate parameters.

19. Setup Documentation File
   - Add SETUP.md with required steps (MinGit bundling, config provider switching, HTTPS cert binding, Windows Auth enabling, service install instructions).

20. Additional Tests
   - Parser edge cases, SQL parameter replacement, PowerShell execution (can be mocked), history store integration.

---
Implementation will proceed sequentially while allowing early vertical slices (parser + basic API) before executors.
