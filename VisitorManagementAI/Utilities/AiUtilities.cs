using System.Globalization;
using System.Text;
using System.Text.Json;
using VisitorManagementAI.Models;

namespace VisitorManagementAI.Utilities;

public static class AiUtilities
{
    public static int CalculateDistance(string s, string t)
    {
        s = s.ToLowerInvariant().Replace("_", "");
        t = t.ToLowerInvariant().Replace("_", "");

        if (string.IsNullOrEmpty(s)) return string.IsNullOrEmpty(t) ? 0 : t.Length;
        if (string.IsNullOrEmpty(t)) return s.Length;

        var d = new int[s.Length + 1, t.Length + 1];

        for (var i = 0; i <= s.Length; i++) d[i, 0] = i;
        for (var j = 0; j <= t.Length; j++) d[0, j] = j;

        for (var i = 1; i <= s.Length; i++)
        {
            for (var j = 1; j <= t.Length; j++)
            {
                var cost = (t[j - 1] == s[i - 1]) ? 0 : 1;
                d[i, j] = Math.Min(
                    Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + cost);
            }
        }
        return d[s.Length, t.Length];
    }

    public static string FormatJsonForAi(string json)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(json) || (!json.Trim().StartsWith("[") && !json.Trim().StartsWith("{"))) 
                return json;

            using var doc = JsonDocument.Parse(json);
            var sb = new StringBuilder();
            var root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in root.EnumerateArray())
                {
                    sb.AppendLine("- " + ProcessSingleObject(item));
                }
            }
            else if (root.ValueKind == JsonValueKind.Object)
            {
                sb.AppendLine(ProcessSingleObject(root));
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
        var parts = new List<string>();

        foreach (var prop in element.EnumerateObject())
        {
            if (prop.Name.Equals("siteId", StringComparison.OrdinalIgnoreCase)) continue;

            string valueStr;
            
            if (prop.Value.ValueKind == JsonValueKind.String && 
                DateTime.TryParse(prop.Value.GetString(), out var dateValue))
            {
                valueStr = dateValue.ToString("f", CultureInfo.InvariantCulture);
            }
            else
            {
                valueStr = prop.Value.ToString();
            }

            parts.Add($"{prop.Name}: {valueStr}");
        }

        return string.Join(", ", parts);
    }
    
    public static string BuildNativeSystemPrompt(List<McpTool> tools, string now)
    {
        var toolsForAi = tools.Select(t =>
        {
            var filteredProperties = t.InputSchema.Properties
                .Where(p => !p.Key.Equals("siteId", StringComparison.OrdinalIgnoreCase))
                .ToDictionary(p => p.Key, p => p.Value);

            return new
            {
                name = t.Name,
                description = t.Description,
                parameters = new { Properties = filteredProperties }
            };
        });

        var jsonTools = JsonSerializer.Serialize(toolsForAi, new JsonSerializerOptions { WriteIndented = false });

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

            if (toolObj.TryGetProperty("parameters", out var paramsProp)) 
            {
                // Handle both direct parameters and nested Properties
                var propsElement = paramsProp.TryGetProperty("Properties", out var propsProp) ? propsProp : paramsProp;
                arguments = ExtractArguments(propsElement);
            }
            
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