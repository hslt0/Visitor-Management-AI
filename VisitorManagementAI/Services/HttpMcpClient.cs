using System.Net.Http.Headers;
using System.Text.Json;
using VisitorManagementAI.Models;

namespace VisitorManagementAI.Services;

public interface IMcpClient
{
    Task<string> GetToolsDescriptionAsync();
    Task<string> CallToolAsync(string toolName, string queryValue, int siteId);
}

public class HttpMcpClient : IMcpClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<HttpMcpClient> _logger;
    private readonly string _mcpUrl;
    
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    public HttpMcpClient(HttpClient httpClient, IConfiguration config, ILogger<HttpMcpClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;

        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var baseUrl = config["McpServerUrl"] ?? config["AiSettings:McpUrl"];
        _mcpUrl = baseUrl?.TrimEnd('/') + "/api/mcp";
    }

    public async Task<string> GetToolsDescriptionAsync()
    {
        try
        {
            var request = new JsonRpcRequest("tools/list", new { }, 1);
            
            _logger.LogInformation("Requesting tools from {Url}", _mcpUrl);

            var response = await _httpClient.PostAsJsonAsync(_mcpUrl, request);
            var rawContent = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Failed to list tools. Status: {Status}", response.StatusCode);
                return string.Empty;
            }

            var jsonString = ExtractJsonFromSse(rawContent);

            var result = JsonSerializer.Deserialize<JsonRpcResponse<McpListToolsResult>>(jsonString, _jsonOptions);
            
            if (result?.Result?.Tools == null) return string.Empty;

            var sb = new System.Text.StringBuilder();
            foreach (var tool in result.Result.Tools)
            {
                var paramName = tool.InputSchema?.Properties?.Keys
                    .FirstOrDefault(k => !k.Equals("siteId", StringComparison.OrdinalIgnoreCase)) ?? "query";

                sb.AppendLine($"- {tool.Name}({paramName}: string): {tool.Description}");
            }
            return sb.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching tools instructions");
            return string.Empty;
        }
    }

    public async Task<string> CallToolAsync(string toolName, string queryValue, int siteId)
    {
        try
        {
            var paramName = toolName.Contains("unit", StringComparison.OrdinalIgnoreCase) ? "unit" : "query";
            
            var request = new JsonRpcRequest("tools/call", new
            {
                name = toolName,
                arguments = new Dictionary<string, object>
                {
                    { paramName, queryValue },
                    { "siteId", siteId }
                }
            }, 1);

            _logger.LogInformation("Calling tool {Tool} with query {Query}", toolName, queryValue);

            var response = await _httpClient.PostAsJsonAsync(_mcpUrl, request);
            var rawContent = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Tool call failed. Status: {Status}", response.StatusCode);
                return "Database error.";
            }

            var jsonString = ExtractJsonFromSse(rawContent);

            var result = JsonSerializer.Deserialize<JsonRpcResponse<McpCallResult>>(jsonString, _jsonOptions);

            if (result?.Error != null) 
            {
                _logger.LogWarning("MCP Server Error: {Msg}", result.Error.Message);
                return $"Error: {result.Error.Message}";
            }
            
            var text = result?.Result?.Content?.FirstOrDefault()?.Text ?? "No data returned.";
            _logger.LogInformation("Tool returned: {Text}", text);
            
            return text;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling tool {ToolName}", toolName);
            return $"Connection error: {ex.Message}";
        }
    }

    private string ExtractJsonFromSse(string rawContent)
    {
        if (rawContent.TrimStart().StartsWith("{"))
        {
            return rawContent;
        }

        var lines = rawContent.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            if (line.StartsWith("data:"))
            {
                return line.Substring(5).Trim();
            }
        }

        _logger.LogWarning("Could not find 'data:' prefix in SSE response. Content: {Content}", rawContent);
        return rawContent;
    }
}