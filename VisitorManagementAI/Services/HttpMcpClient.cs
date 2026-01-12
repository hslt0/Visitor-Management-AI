using System.Net.Http.Headers;
using System.Text.Json;
using VisitorManagementAI.Models;

namespace VisitorManagementAI.Services;

/// <summary>
/// Implementation of <see cref="IMcpClient"/> that communicates with an MCP server over HTTP.
/// </summary>
public class HttpMcpClient : IMcpClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<HttpMcpClient> _logger;
    private readonly string _mcpUrl;
    private List<string> _validToolNames = [];
    
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// Initializes a new instance of the <see cref="HttpMcpClient"/> class.
    /// </summary>
    /// <param name="httpClient">The HTTP client instance.</param>
    /// <param name="config">The configuration instance.</param>
    /// <param name="logger">The logger instance.</param>
    public HttpMcpClient(HttpClient httpClient, IConfiguration config, ILogger<HttpMcpClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;

        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var baseUrl = config["McpServerUrl"] ?? config["AiSettings:McpUrl"];
        _mcpUrl = baseUrl?.TrimEnd('/') + "/api/mcp";
    }

    /// <inheritdoc />
    public async Task<List<McpTool>> GetToolsAsync()
    {
        try
        {
            // Create a JSON-RPC request to list available tools
            var request = new JsonRpcRequest("tools/list", new { }, 1);
            
            _logger.LogInformation("Requesting tools JSON from {Url}", _mcpUrl);

            var response = await _httpClient.PostAsJsonAsync(_mcpUrl, request);
            var rawContent = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Failed to list tools. Status: {Status}", response.StatusCode);
                return [];
            }

            // Extract JSON from potential SSE format
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

    /// <inheritdoc />
    public async Task<string> CallToolAsync(string toolName, Dictionary<string, object> arguments, int siteId)
    {
        try
        {
            // Inject siteId into arguments for context
            var finalArguments = new Dictionary<string, object>(arguments)
            {
                { "siteId", siteId }
            };

            // Create a JSON-RPC request to call the specified tool
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

    /// <inheritdoc />
    public List<string> GetKnownTools()
    {
        return _validToolNames;
    }

    /// <summary>
    /// Extracts the JSON payload from a Server-Sent Events (SSE) formatted string.
    /// </summary>
    /// <param name="rawContent">The raw content string.</param>
    /// <returns>The extracted JSON string.</returns>
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