using System.Text.RegularExpressions;
using ScriptRunner.Core; // ensure access to interfaces & models

namespace ScriptRunner.Core.Parsing;

public sealed class ScriptMetadataParser : IScriptParser
{
    private static readonly Regex MetadataBlockRegex = new("SCRIPT-METADATA:(.*?)END-SCRIPT-METADATA", RegexOptions.Singleline | RegexOptions.Compiled);

    public ScriptMetadata Parse(string rawContent)
    {
        if (rawContent is null) throw new ArgumentNullException(nameof(rawContent));
        var match = MetadataBlockRegex.Match(rawContent);
        if (!match.Success)
            throw new InvalidOperationException("Metadata block missing");

        var block = match.Groups[1].Value; // inner content between markers
        var lines = block.Split('\n').Select(l => l.TrimEnd('\r')).ToList();

        string? id = null, name = null, category = null, description = null, sqlConnectionString = null;
        var parameters = new List<ScriptParameterDefinition>();
        ScriptParameterDefinitionBuilder? currentParam = null;

        void CommitParam()
        {
            if (currentParam != null)
            {
                parameters.Add(currentParam.Build());
                currentParam = null;
            }
        }

        foreach (var rawLine in lines.Select(l => l.Trim()))
        {
            if (string.IsNullOrWhiteSpace(rawLine)) continue;

            if (rawLine.StartsWith("- Name:", StringComparison.OrdinalIgnoreCase))
            {
                CommitParam();
                var nameVal = rawLine.Split(':', 2)[1].Trim();
                currentParam = new ScriptParameterDefinitionBuilder { Name = nameVal };
                continue;
            }

            if (rawLine.StartsWith("SqlConnectionString:", StringComparison.OrdinalIgnoreCase))
            {
                sqlConnectionString = rawLine.Split(':', 2)[1].Trim().Trim('"');
                continue;
            }

            // Top-level scalar fields
            if (currentParam == null)
            {
                if (rawLine.StartsWith("Id:", StringComparison.OrdinalIgnoreCase)) id = Value(rawLine);
                else if (rawLine.StartsWith("Name:", StringComparison.OrdinalIgnoreCase)) name = Value(rawLine);
                else if (rawLine.StartsWith("Category:", StringComparison.OrdinalIgnoreCase)) category = Value(rawLine);
                else if (rawLine.StartsWith("Description:", StringComparison.OrdinalIgnoreCase)) description = Value(rawLine);
                continue;
            }

            // Parameter attributes
            if (rawLine.StartsWith("Type:", StringComparison.OrdinalIgnoreCase))
            {
                var t = Value(rawLine);
                if (Enum.TryParse<ScriptParameterType>(t, true, out var parsed)) currentParam.Type = parsed; else currentParam.Type = ScriptParameterType.String;
            }
            else if (rawLine.StartsWith("Required:", StringComparison.OrdinalIgnoreCase))
            {
                currentParam.Required = Value(rawLine).Equals("true", StringComparison.OrdinalIgnoreCase);
            }
            else if (rawLine.StartsWith("DisplayName:", StringComparison.OrdinalIgnoreCase))
            {
                currentParam.DisplayName = Value(rawLine);
            }
            else if (rawLine.StartsWith("Default:", StringComparison.OrdinalIgnoreCase))
            {
                currentParam.Default = Value(rawLine).Trim('"');
            }
            else if (rawLine.StartsWith("HelpText:", StringComparison.OrdinalIgnoreCase))
            {
                currentParam.HelpText = Value(rawLine).Trim('"');
            }
            else if (rawLine.StartsWith("EnumValues:", StringComparison.OrdinalIgnoreCase) || rawLine.StartsWith("Values:", StringComparison.OrdinalIgnoreCase))
            {
                var vals = Value(rawLine);
                currentParam.EnumValues = vals.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            }
        }
        CommitParam();

        if (id is null || name is null || category is null)
            throw new InvalidOperationException("Mandatory metadata fields (Id, Name, Category) missing");

        return new ScriptMetadata
        {
            Id = id,
            Name = name,
            Category = category,
            Description = description,
            Parameters = parameters,
            SqlConnectionString = sqlConnectionString
        };
    }

    private static string Value(string line) => line.Split(':', 2)[1].Trim();

    private sealed class ScriptParameterDefinitionBuilder
    {
        public required string Name { get; init; }
        public ScriptParameterType Type { get; set; } = ScriptParameterType.String;
        public bool Required { get; set; }
        public string? DisplayName { get; set; }
        public string? Default { get; set; }
        public string? HelpText { get; set; }
        public string[]? EnumValues { get; set; }
        public ScriptParameterDefinition Build() => new()
        {
            Name = Name,
            Type = Type,
            Required = Required,
            DisplayName = DisplayName,
            Default = Default,
            HelpText = HelpText,
            EnumValues = EnumValues
        };
    }
}
