using System.Text.Json;
using AiLab.Core.Execution;
using Xunit;

namespace AiLab.Tests.Execution;

public class InputBindingResolverTests
{
    [Fact]
    public void Resolve_WorkspaceVariable_Substitutes()
    {
        var context = new BindingResolutionContext
        {
            WorkspaceVariables = new Dictionary<string, string> { ["resume"] = "John Doe resume text" },
        };

        var result = InputBindingResolver.Resolve("Context: {{workspace.resume}}", context);

        Assert.Equal("Context: John Doe resume text", result.ResolvedText);
        Assert.Equal("John Doe resume text", result.ResolvedBindings["workspace.resume"]);
        Assert.Empty(result.UnresolvedExpressions);
    }

    [Fact]
    public void Resolve_PriorRequestOutput_Substitutes()
    {
        var context = new BindingResolutionContext
        {
            PriorRequestOutputs = new Dictionary<string, PriorRequestOutput>
            {
                ["ParseResume"] = new() { RawOutputText = "parsed resume output" },
            },
        };

        var result = InputBindingResolver.Resolve("{{ParseResume.output}}", context);

        Assert.Equal("parsed resume output", result.ResolvedText);
    }

    [Fact]
    public void Resolve_PriorRequestJsonPath_NavigatesToPropertyValue()
    {
        using var doc = JsonDocument.Parse("""{"skills": ["C#", "Angular"], "years": 5}""");
        var context = new BindingResolutionContext
        {
            PriorRequestOutputs = new Dictionary<string, PriorRequestOutput>
            {
                ["ParseResume"] = new() { RawOutputText = doc.RootElement.GetRawText(), ParsedJson = doc.RootElement.Clone() },
            },
        };

        var result = InputBindingResolver.Resolve("{{ParseResume.json.skills[0]}}", context);

        Assert.Equal("C#", result.ResolvedText);
    }

    [Fact]
    public void Resolve_JsonPathScalarNumber_ReturnsRawText()
    {
        using var doc = JsonDocument.Parse("""{"years": 5}""");
        var context = new BindingResolutionContext
        {
            PriorRequestOutputs = new Dictionary<string, PriorRequestOutput>
            {
                ["ParseResume"] = new() { RawOutputText = "{}", ParsedJson = doc.RootElement.Clone() },
            },
        };

        var result = InputBindingResolver.Resolve("{{ParseResume.json.years}}", context);

        Assert.Equal("5", result.ResolvedText);
    }

    [Fact]
    public void Resolve_UnknownExpression_LeavesLiteralPlaceholderAndReportsUnresolved()
    {
        var context = new BindingResolutionContext();

        var result = InputBindingResolver.Resolve("{{workspace.missing}}", context);

        Assert.Equal("{{workspace.missing}}", result.ResolvedText);
        Assert.Contains("workspace.missing", result.UnresolvedExpressions);
    }

    [Fact]
    public void Resolve_NoBindings_ReturnsTextUnchanged()
    {
        var result = InputBindingResolver.Resolve("plain text, no bindings", new BindingResolutionContext());

        Assert.Equal("plain text, no bindings", result.ResolvedText);
        Assert.Empty(result.ResolvedBindings);
    }

    [Fact]
    public void Resolve_MultipleBindingsInOneString_AllSubstituted()
    {
        var context = new BindingResolutionContext
        {
            WorkspaceVariables = new Dictionary<string, string> { ["a"] = "1", ["b"] = "2" },
        };

        var result = InputBindingResolver.Resolve("{{workspace.a}} and {{workspace.b}}", context);

        Assert.Equal("1 and 2", result.ResolvedText);
    }
}
