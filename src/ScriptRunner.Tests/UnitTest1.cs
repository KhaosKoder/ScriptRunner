using ScriptRunner.Core;
using ScriptRunner.Core.Parsing;

namespace ScriptRunner.Tests;

public class MetadataParserTests
{
    private readonly ScriptMetadataParser _parser = new();

    [Fact]
    public void ParsesBasicMetadata()
    {
        var raw = """<#\nSCRIPT-METADATA:\n  Id: sample\n  Name: Sample Script\n  Category: Demo\n  Description: Just a test.\n\n  Parameters:\n    - Name: Value\n      Type: int\n      Required: true\n      DisplayName: Value\n      Default: 5\n      HelpText: \"A number\"\nEND-SCRIPT-METADATA\n#>\nWrite-Host $Value""";
        var meta = _parser.Parse(raw);
        Assert.Equal("sample", meta.Id);
        Assert.Single(meta.Parameters);
        var p = meta.Parameters[0];
        Assert.Equal("Value", p.Name);
        Assert.Equal(ScriptParameterType.Int, p.Type);
        Assert.True(p.Required);
        Assert.Equal("Value", p.DisplayName);
        Assert.Equal("5", p.Default);
        Assert.Equal("A number", p.HelpText);
    }

    [Fact]
    public void ParsesEnumValues()
    {
        var raw = """<#\nSCRIPT-METADATA:\n  Id: enum-test\n  Name: Enum Test\n  Category: Demo\n  Description: Test enum parsing.\n\n  Parameters:\n    - Name: Mode\n      Type: enum\n      Required: true\n      EnumValues: Fast,Slow,Medium\nEND-SCRIPT-METADATA\n#>""";
        var meta = _parser.Parse(raw);
        var p = meta.Parameters.Single();
        Assert.Equal(ScriptParameterType.Enum, p.Type);
        Assert.Equal(new[]{"Fast","Slow","Medium"}, p.EnumValues);
    }

    [Fact]
    public void ThrowsOnMissingMandatoryFields()
    {
        var raw = """<#\nSCRIPT-METADATA:\n  Name: Missing Id\n  Category: Demo\nEND-SCRIPT-METADATA\n#>""";
        Assert.Throws<InvalidOperationException>(() => _parser.Parse(raw));
    }
}
