# ScriptRunner User Guide

Welcome to ScriptRunner. This guide explains how to use the web UI and API to list and run scripts securely.

## Accessing the App

- Navigate to `https://<host>:<port>/` in a Windows-authenticated browser session.
- Your Windows identity is used for access control and auditing.

## Roles

- Viewers (CanView): can see scripts and history.
- Runners (CanRun): can execute scripts.

If you lack permissions, contact your administrator.

## The UI

- Left panel: scripts grouped by category with a filter box.
- Right panel: shows metadata, parameters, Run button, output, and recent history.

### Parameters

- The UI generates a form based on per-script metadata.
- Supported types:
  - String, Int, Decimal, Bool, DateTime, Enum (select list)
- Required parameters are enforced; defaults may be pre-populated.

### Running a Script

1. Select a script from the list.
2. Fill out the parameter form.
3. Click Run.
4. The UI indicates queued/running state; you may cancel if needed.
5. Upon completion, output (stdout/stderr) and exit code appear.
6. An email with the results is sent to you if your email is mapped.

### History

- The Recent History grid shows past executions.
- Click a row to view its output.
- Download the full output via the link provided.

## API Quick Reference

All endpoints require Windows Authentication and HTTPS.

- `GET /api/scripts` — list scripts (metadata only)
- `GET /api/scripts/{id}` — get script metadata
- `GET /api/scripts/{id}/content` — get raw script
- `POST /api/scripts/{id}/execute` — queue execution; returns 202 with executionId
  - Request body: JSON object of parameter name/value pairs
- `POST /api/cancel/{executionId}` — cancel a queued/running execution
- `GET /api/history` — recent executions
- `GET /api/history/{executionId}` — execution details
- `GET /api/history/{executionId}/output` — full output download
- `GET /health` — service health
- `GET /ready` — readiness check

## Tips

- Use meaningful parameter values; avoid extremely large inputs.
- If the script requires database access, ensure the correct connection string is configured.
- For long-running scripts, monitor status in the UI or via `/api/history/{id}`.

## Troubleshooting

- If a script fails immediately, verify parameter types and required fields.
- If email is not received, check with your admin that your Windows user is mapped in `UserEmailMap`.
- For Git errors, ensure PAT environment variables are set for the service account.
- For HTTPS/auth issues, confirm your browser used Windows Authentication and the certificate is valid.

## Support

Please contact your system administrator for configuration or access issues. For software defects, file an issue with logs and the executionId.
