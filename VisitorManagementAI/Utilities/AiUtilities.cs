using System.Globalization;
using System.Text;
using System.Text.Json;
using VisitorManagementAI.Models;

namespace VisitorManagementAI.Utilities;

public static class AiUtilities
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    public static int CalculateDistance(string? s, string? t)
    {
        var source = s?.ToLowerInvariant().Replace("_", "") ?? string.Empty;
        var target = t?.ToLowerInvariant().Replace("_", "") ?? string.Empty;

        if (string.IsNullOrEmpty(source)) return target.Length;
        if (string.IsNullOrEmpty(target)) return source.Length;

        var n = source.Length;
        var m = target.Length;

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

    private static string ProcessSingleObject(JsonElement element)
    {
        var sb = new StringBuilder();
        var isFirst = true;

        foreach (var prop in element.EnumerateObject())
        {
            if (prop.Name.Equals("siteId", StringComparison.OrdinalIgnoreCase)) continue;

            if (!isFirst) sb.Append(", ");
            isFirst = false;

            sb.Append(prop.Name).Append(": ");
            
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

    public static (string, Dictionary<string, object> arguments) ParseNativeToolOutput(string response)
    {
        try
        {
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
            
            var propsElement = paramsProp.TryGetProperty("Properties", out var propsProp) ? propsProp : paramsProp;
            arguments = ExtractArguments(propsElement);

            return (toolName ?? string.Empty, arguments);
        }
        catch
        {
            return (string.Empty, new Dictionary<string, object>());
        }
    }

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