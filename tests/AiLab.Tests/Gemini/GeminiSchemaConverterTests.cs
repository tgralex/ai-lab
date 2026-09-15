using System.Text.Json.Nodes;
using AiLab.Infrastructure.Gemini;
using Xunit;

namespace AiLab.Tests.Gemini;

public class GeminiSchemaConverterTests
{
    [Fact]
    public void Convert_StripsAdditionalProperties()
    {
        var schema = JsonNode.Parse("""{"type": "object", "additionalProperties": false, "properties": {}}""");

        var result = GeminiSchemaConverter.Convert(schema);

        Assert.Null(result!["additionalProperties"]);
        Assert.Equal("object", result["type"]!.GetValue<string>());
    }

    [Fact]
    public void Convert_NullableUnionType_BecomesSingleTypeWithNullableFlag()
    {
        // The real 400 this fixes: "Proto field is not repeating, cannot start list" — Gemini's
        // Schema rejects `"type": [...]` outright.
        var schema = JsonNode.Parse("""{"type": ["string", "null"]}""");

        var result = GeminiSchemaConverter.Convert(schema);

        Assert.Equal("string", result!["type"]!.GetValue<string>());
        Assert.True(result["nullable"]!.GetValue<bool>());
    }

    [Fact]
    public void Convert_PlainScalarType_PassesThroughUnchanged()
    {
        var schema = JsonNode.Parse("""{"type": "string"}""");

        var result = GeminiSchemaConverter.Convert(schema);

        Assert.Equal("string", result!["type"]!.GetValue<string>());
        Assert.Null(result["nullable"]);
    }

    [Fact]
    public void Convert_RecursesIntoNestedPropertiesAndArrayItems()
    {
        var schema = JsonNode.Parse("""
            {
              "type": "object",
              "additionalProperties": false,
              "properties": {
                "tags": {
                  "type": "array",
                  "items": { "type": ["string", "null"], "additionalProperties": false }
                }
              }
            }
            """);

        var result = GeminiSchemaConverter.Convert(schema);

        var items = result!["properties"]!["tags"]!["items"];
        Assert.Equal("string", items!["type"]!.GetValue<string>());
        Assert.True(items["nullable"]!.GetValue<bool>());
        Assert.Null(items["additionalProperties"]);
        Assert.Null(result["additionalProperties"]);
    }

    [Fact]
    public void Convert_PreservesRequiredEnumAndDescription()
    {
        var schema = JsonNode.Parse("""
            {
              "type": "object",
              "required": ["status"],
              "properties": {
                "status": { "type": "string", "enum": ["ok", "error"], "description": "result status" }
              }
            }
            """);

        var result = GeminiSchemaConverter.Convert(schema);

        Assert.Equal("status", result!["required"]![0]!.GetValue<string>());
        Assert.Equal("ok", result["properties"]!["status"]!["enum"]![0]!.GetValue<string>());
        Assert.Equal("result status", result["properties"]!["status"]!["description"]!.GetValue<string>());
    }
}
