using System.Globalization;
using System.Text;
using System.Text.Json;

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
}