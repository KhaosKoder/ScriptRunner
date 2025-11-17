using Microsoft.Extensions.Logging;

namespace ScriptRunner.Core.Validation;

public sealed class ParameterValidator
{
    private readonly ILogger<ParameterValidator> _logger;
    public ParameterValidator(ILogger<ParameterValidator> logger) => _logger = logger;

    public bool Validate(ScriptMetadata meta, IDictionary<string, object?> provided, out string error)
    {
        foreach (var p in meta.Parameters)
        {
            if (!provided.ContainsKey(p.Name) || provided[p.Name] is null || string.IsNullOrWhiteSpace(provided[p.Name]?.ToString()))
            {
                if (!string.IsNullOrWhiteSpace(p.Default))
                {
                    provided[p.Name] = p.Default;
                    // When applying default
                    _logger.LogDebug("[Params] Applied default for {Name} value={Value}", p.Name, p.Default);
                }
            }
            if (p.Required && (!provided.ContainsKey(p.Name) || provided[p.Name] is null || string.IsNullOrWhiteSpace(provided[p.Name]?.ToString())))
            {
                error = $"Missing required parameter {p.Name}";
                _logger.LogWarning(error);
                return false;
            }
            if (!provided.ContainsKey(p.Name)) continue;
            var val = provided[p.Name];
            if (p.Type == ScriptParameterType.String)
            {
                var s = val?.ToString() ?? string.Empty;
                if (s.Length > 4000) { error = $"Parameter {p.Name} too long"; return false; }
            }
            if (!TryCoerce(p, val, out var coerced, out error))
            {
                _logger.LogWarning(error);
                return false;
            }
            provided[p.Name] = coerced;
            // After successful coercion
            _logger.LogTrace("[Params] Coerced parameter {Name} -> {Value}", p.Name, provided[p.Name]);
        }
        error = string.Empty;
        return true;
    }

    private static bool TryCoerce(ScriptParameterDefinition p, object? input, out object? output, out string error)
    {
        try
        {
            switch (p.Type)
            {
                case ScriptParameterType.Int:
                    if (input is int) { output = input; error = string.Empty; return true; }
                    output = Convert.ToInt32(input);
                    error = string.Empty; return true;
                case ScriptParameterType.Decimal:
                    if (input is decimal) { output = input; error = string.Empty; return true; }
                    output = Convert.ToDecimal(input);
                    error = string.Empty; return true;
                case ScriptParameterType.Bool:
                    if (input is bool) { output = input; error = string.Empty; return true; }
                    output = Convert.ToBoolean(input);
                    error = string.Empty; return true;
                case ScriptParameterType.DateTime:
                    if (input is DateTime dt) { output = dt; error = string.Empty; return true; }
                    output = DateTime.Parse(input?.ToString() ?? string.Empty);
                    error = string.Empty; return true;
                case ScriptParameterType.Enum:
                    var s = input?.ToString() ?? string.Empty;
                    if (p.EnumValues is { Length: > 0 } && !p.EnumValues.Contains(s, StringComparer.OrdinalIgnoreCase))
                    { output = null; error = $"Invalid enum value '{s}' for {p.Name}"; return false; }
                    output = s; error = string.Empty; return true;
                default:
                    output = input?.ToString(); error = string.Empty; return true;
            }
        }
        catch (Exception ex)
        {
            output = null; error = $"Parameter {p.Name} value conversion failed: {ex.Message}"; return false;
        }
    }
}
