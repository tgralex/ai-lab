using System.Text.Json.Nodes;

namespace AiLab.Infrastructure.Gemini;

/// <summary>
/// Gemini's `responseSchema` is not full JSON Schema — it's a restricted proto-backed subset
/// (Google's own "Schema" message), confirmed against a real 400 from the live API:
/// - No `additionalProperties` ("Unknown name \"additionalProperties\": Cannot find field").
/// - `type` must be a single value, never an array — the common JSON Schema nullable-union
///   pattern (`"type": ["string", "null"]`) fails with "Proto field is not repeating, cannot
///   start list". Nullability is instead a separate `"nullable": true` sibling field.
/// This app's `StructuredOutputSchema` is always plain JSON Schema (it's sent as-is to OpenAI/
/// Grok, which both accept it natively) — so unlike those two providers, Gemini needs this
/// translation pass before the schema can be sent.
/// </summary>
public static class GeminiSchemaConverter
{
    public static JsonNode? Convert(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
            {
                var result = new JsonObject();
                foreach (var (key, value) in obj)
                {
                    if (key is "additionalProperties" or "$schema" or "$id")
                    {
                        continue;
                    }

                    if (key == "type" && value is JsonArray typeArray)
                    {
                        var types = typeArray.Select(t => t?.GetValue<string>()).ToList();
                        var nonNullType = types.FirstOrDefault(t => t != "null");
                        if (nonNullType is not null)
                        {
                            result["type"] = nonNullType;
                        }

                        if (types.Contains("null"))
                        {
                            result["nullable"] = true;
                        }

                        continue;
                    }

                    result[key] = Convert(value);
                }

                return result;
            }

            case JsonArray arr:
            {
                var result = new JsonArray();
                foreach (var item in arr)
                {
                    result.Add(Convert(item));
                }

                return result;
            }

            case JsonValue value:
                return value.DeepClone();

            default:
                return null;
        }
    }
}
