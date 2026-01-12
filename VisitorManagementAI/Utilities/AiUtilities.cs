using System.Globalization;
using System.Text;
using System.Text.Json;
using VisitorManagementAI.Models;

namespace VisitorManagementAI.Utilities;

/// <summary>
/// Provides utility methods for AI operations, including string distance calculation, JSON formatting, and prompt building.
/// </summary>
public static class AiUtilities
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    /// <summary>
    /// Calculates the Levenshtein distance between two strings.
    /// Used for fuzzy matching tool names if the AI hallucinates a slightly incorrect name.
    /// </summary>
    /// <param name="s">The first string.</param>
    /// <param name="t">The second string.</param>
    /// <returns>The Levenshtein distance.</returns>
    public static int CalculateDistance(string? s, string? t)
    {
        var source = s?.ToLowerInvariant().Replace("_", "") ?? string.Empty;
        var target = t?.ToLowerInvariant().Replace("_", "") ?? string.Empty;

        if (string.IsNullOrEmpty(source)) return target.Length;
        if (string.IsNullOrEmpty(target)) return source.Length;

        var n = source.Length;
        var m = target.Length;

        // Optimized to use O(m) space instead of O(n*m)
        var prevRow = new int[m + 1];
        var currRow = new int[m + 1];

        for (var j = 0; j <= m; j++) prevRow[j] = j;

        for (var i = 1; i <= n; i++)
        {
            currRow[0] = i;
            for (var j = 1; j <= m; j++)
            {
                var cost = target[j - 1] == source[i - 1] ? 0 : 1;
                currRow[j] = Math.Min(
                    Math.Min(currRow[j - 1] + 1, prevRow[j] + 1),
                    prevRow[j - 1] + cost);
            }
            Array.Copy(currRow, prevRow, m + 1);
        }

        return prevRow[m];
    }

    /// <summary>
    /// Formats a JSON string into a more human-readable format for AI consumption.
    /// This helps the AI understand the data better than raw JSON.
    /// </summary>
    /// <param name="json">The input JSON string.</param>
    /// <returns>A formatted string representation of the JSON data.</returns>
    public static string FormatJsonForAi(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return json;

        var span = json.AsSpan().Trim();
        if (!span.StartsWith("[") && !span.StartsWith("{")) 
            return json;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var sb = new StringBuilder();
            var root = doc.RootElement;

            switch (root.ValueKind)
            {
                case JsonValueKind.Array:
                    foreach (var item in root.EnumerateArray())
                    {
                        sb.Append("- ").AppendLine(ProcessSingleObject(item));
                    }
                    break;
                case JsonValueKind.Object:
                    sb.AppendLine(ProcessSingleObject(root));
                    break;
            }

            return sb.ToString();
        }
        catch
        {
            return json;
        }
    }

    /// <summary>
    /// Processes a single JSON object, formatting its properties into a key-value string.
    /// Filters out 'siteId' and formats dates.
    /// </summary>
    /// <param name="element">The JSON element to process.</param>
    /// <returns>A formatted string of the object's properties.</returns>
    private static string ProcessSingleObject(JsonElement element)
    {
        var sb = new StringBuilder();
        var isFirst = true;

        foreach (var prop in element.EnumerateObject())
        {
            // Hide siteId from the AI output
            if (prop.Name.Equals("siteId", StringComparison.OrdinalIgnoreCase)) continue;

            if (!isFirst) sb.Append(", ");
            isFirst = false;

            sb.Append(prop.Name).Append(": ");
            
            // Format dates nicely
            if (prop.Value.ValueKind == JsonValueKind.String && 
                DateTime.TryParse(prop.Value.GetString(), out var dateValue))
            {
                sb.Append(dateValue.ToString("f", CultureInfo.InvariantCulture));
            }
            else
            {
                sb.Append(prop.Value);
            }
        }

        return sb.ToString();
    }
    
    /// <summary>
    /// Builds the system prompt for the AI, including the list of available tools.
    /// Filters out the 'siteId' parameter from tool definitions so the AI doesn't try to provide it.
    /// </summary>
    /// <param name="tools">The list of available MCP tools.</param>
    /// <param name="now">The current system time string.</param>
    /// <returns>The constructed system prompt.</returns>
    public static string BuildNativeSystemPrompt(List<McpTool> tools, string now)
    {
        var toolsForAi = tools.Select(t => new
        {
            name = t.Name,
            description = t.Description,
            parameters = new 
            { 
                Properties = t.InputSchema.Properties
                    .Where(p => !p.Key.Equals("siteId", StringComparison.OrdinalIgnoreCase))
                    .ToDictionary(p => p.Key, p => p.Value) 
            }
        });

        var jsonTools = JsonSerializer.Serialize(toolsForAi, JsonOptions);

        return $"""
                You are a visitor management assistant connected to a real-time system. Current time: {now}.

                <|tool|>
                {jsonTools}
                <|/tool|>

                If the user asks a question that requires external data, call the appropriate tool.
                """;
    }

    /// <summary>
    /// Parses the AI's response to extract the tool name and arguments.
    /// Handles the specific JSON format the AI is trained to output for tool calls.
    /// </summary>
    /// <param name="response">The raw response from the AI.</param>
    /// <returns>A tuple containing the tool name and a dictionary of arguments.</returns>
    public static (string, Dictionary<string, object> arguments) ParseNativeToolOutput(string response)
    {
        try
        {
            // Look for JSON array brackets in the response
            var jsonStart = response.IndexOf('[');
            var jsonEnd = response.LastIndexOf(']');

            if (jsonStart == -1 || jsonEnd == -1 || jsonEnd < jsonStart)
                return (string.Empty, new Dictionary<string, object>());

            var json = response.Substring(jsonStart, jsonEnd - jsonStart + 1);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0)
                return (string.Empty, new Dictionary<string, object>());

            var toolObj = root[0];
            
            if (!toolObj.TryGetProperty("name", out var nameProp))
                return (string.Empty, new Dictionary<string, object>());

            var toolName = nameProp.GetString();
            
            var arguments = new Dictionary<string, object>();

            if (!toolObj.TryGetProperty("parameters", out var paramsProp)) return (toolName ?? string.Empty, arguments);
            
            // Handle nested "Properties" object if present, otherwise use parameters directly
            var propsElement = paramsProp.TryGetProperty("Properties", out var propsProp) ? propsProp : paramsProp;
            arguments = ExtractArguments(propsElement);

            return (toolName ?? string.Empty, arguments);
        }
        catch
        {
            return (string.Empty, new Dictionary<string, object>());
        }
    }

    /// <summary>
    /// Extracts arguments from a JSON element into a dictionary.
    /// Converts JSON types to appropriate C# types (int, double, bool, string).
    /// </summary>
    /// <param name="element">The JSON element containing arguments.</param>
    /// <returns>A dictionary of arguments.</returns>
    private static Dictionary<string, object> ExtractArguments(JsonElement element)
    {
        var result = new Dictionary<string, object>();
        
        if (element.ValueKind != JsonValueKind.Object)
            return result;

        foreach (var prop in element.EnumerateObject())
        {
            if (prop.Name.Equals("siteId", StringComparison.OrdinalIgnoreCase))
                continue;

            switch (prop.Value.ValueKind)
            {
                case JsonValueKind.String:
                    result[prop.Name] = prop.Value.GetString() ?? "";
                    break;
                case JsonValueKind.Number:
                    if (prop.Value.TryGetInt32(out var i))
                        result[prop.Name] = i;
                    else
                        result[prop.Name] = prop.Value.GetDouble();
                    break;
                case JsonValueKind.True:
                    result[prop.Name] = true;
                    break;
                case JsonValueKind.False:
                    result[prop.Name] = false;
                    break;
                case JsonValueKind.Null:
                    result[prop.Name] = "";
                    break;
                default:
                    result[prop.Name] = prop.Value.ToString();
                    break;
            }
        }
        
        return result;
    }
}