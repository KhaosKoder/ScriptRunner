namespace ScriptRunner.Core.Configuration;

public sealed class ExecutionOptions
{
    public int MaxConcurrentExecutions { get; set; } = 2;
    public int OutputTruncationThreshold { get; set; } = 10000; // characters

    // Optional path to PowerShell executable. If not set, the executor will try pwsh then powershell on Windows.
    public string? PowerShellPath { get; set; }
    // When true (default), fall back to Windows PowerShell (powershell.exe) if pwsh is not available on Windows
    public bool FallbackToWindowsPowerShell { get; set; } = true;

    // New: keep temp scripts after execution for debugging
    public bool KeepTempScripts { get; set; } = false;
}
