using System.Net.Http.Headers;
using System.Text.Json;
using VisitorManagementAI.Models;

namespace VisitorManagementAI.Services;

public class HttpMcpClient : IMcpClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<HttpMcpClient> _logger;
    private readonly string _mcpUrl;
    private List<string> _validToolNames = [];
    
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

    public async Task<List<McpTool>> GetToolsAsync()
    {
        try
        {
            var request = new JsonRpcRequest("tools/list", new { }, 1);
            
            _logger.LogInformation("Requesting tools JSON from {Url}", _mcpUrl);

            var response = await _httpClient.PostAsJsonAsync(_mcpUrl, request);
            var rawContent = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Failed to list tools. Status: {Status}", response.StatusCode);
                return [];
            }

            var jsonString = ExtractJsonFromSse(rawContent);

            var result = JsonSerializer.Deserialize<JsonRpcResponse<McpListToolsResult>>(jsonString, _jsonOptions);
            
            if (result?.Result.Tools == null) return [];

            _validToolNames = result.Result.Tools.Select(t => t.Name).ToList();

            return result.Result.Tools;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching tools");
            return new List<McpTool>();
        }
    }

    public async Task<string> CallToolAsync(string toolName, Dictionary<string, object> arguments, int siteId)
    {
        try
        {
            var finalArguments = new Dictionary<string, object>(arguments)
            {
                { "siteId", siteId }
            };

            var request = new JsonRpcRequest("tools/call", new
            {
                name = toolName,
                arguments = finalArguments
            }, 1);

            _logger.LogInformation("Calling tool {Tool} with arguments {Arguments}", toolName, finalArguments);

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
            
            var text = result?.Result.Content.FirstOrDefault()?.Text ?? "No data returned.";
            _logger.LogInformation("Tool returned: {Text}", text);
            
            return text;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling tool {ToolName}", toolName);
            return $"Connection error: {ex.Message}";
        }
    }

    public List<string> GetKnownTools()
    {
        return _validToolNames;
    }

    private string ExtractJsonFromSse(string rawContent)
    {
        if (rawContent.TrimStart().StartsWith("{"))
        {
            return rawContent;
        }

        var lines = rawContent.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);

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