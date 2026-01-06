using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Net.Http.Headers;
using Microsoft.ML.OnnxRuntimeGenAI;

namespace VisitorManagementAI.Services;

public interface IVisitorQueryService
{
    Task<string> ChatAsync(string userPrompt, int siteId);
}

public class OnnxVisitorQueryService : IVisitorQueryService, IDisposable
{
    private readonly Model _model;
    private readonly Tokenizer _tokenizer;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _mcpUrl;

    public OnnxVisitorQueryService(IConfiguration config, IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
        var baseUrl = config["McpServerUrl"] ?? config["AiSettings:McpUrl"];
        _mcpUrl = baseUrl?.TrimEnd('/') + "/api/mcp";
        var modelPath = config["AiSettings:ModelPath"] ?? throw new InvalidOperationException("Model path not configured.");
        _model = new Model(Path.GetFullPath(modelPath));
        _tokenizer = new Tokenizer(_model);
    }

    public async Task<string> ChatAsync(string userPrompt, int siteId)
    {
        var now = DateTime.Now.ToString("f");
        
        var toolsDescription = await FetchToolsInstructionAsync();

        var systemPrompt = $"""
                            You are a security assistant. Current time: {now}.

                            AVAILABLE TOOLS:
                            {toolsDescription}

                            CRITICAL RULES:
                            1. USE ONLY THE EXACT TOOL NAMES from the list above.
                            2. The tool for searching visitors is 'find_visitor'.
                            3. The tool for checking unit residents is 'get_unit_visitors'.
                            4. NEVER invent names like 'check_unit_members'.

                            EXAMPLES:
                            - User: "Who is in unit 505?" 
                              Assistant: [CALL: get_unit_visitors(unit="505")]

                            - User: "Find Alex"
                              Assistant: [CALL: find_visitor(query="Alex")]
                            """;

        var firstResponse = await RunInferenceAsync(userPrompt, systemPrompt);

        var match = Regex.Match(firstResponse, @"\[CALL:\s*([a-zA-Z0-9__]+)\s*\(\s*(?:.*?=)?\s*(.*)\s*\)\]");

        if (!match.Success) return firstResponse;

        var toolName = match.Groups[1].Value;
        var rawValue = match.Groups[2].Value;
        var queryValue = rawValue.Trim().Trim('"', '\'');

        Console.WriteLine($"[DEBUG] Tool: {toolName}, Query: '{queryValue}'");

        var paramName = "query"; 
        if (toolName.Contains("unit")) paramName = "unit"; 
        
        var requestBody = new
        {
            jsonrpc = "2.0",
            method = "tools/call",
            @params = new
            {
                name = toolName,
                arguments = new Dictionary<string, object>
                {
                    { paramName, queryValue },
                    { "siteId", siteId }
                }
            },
            id = Guid.NewGuid().ToString()
        };

        using var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        try
        {
            var response = await client.PostAsJsonAsync(_mcpUrl, requestBody);
            var jsonResponse = await response.Content.ReadAsStringAsync();
            var toolResult = ExtractMcpContent(jsonResponse);

            if (string.IsNullOrEmpty(toolResult)) return firstResponse;

            var finalPrompt = $"User: {userPrompt}. Database result: {toolResult}. Summarize naturally for the user.";
            return await RunInferenceAsync(finalPrompt, "You are a helpful security assistant.");
        }
        catch (Exception ex)
        {
            return $"Connection error: {ex.Message}";
        }
    }

    private async Task<string> FetchToolsInstructionAsync()
    {
        try
        {
            var requestBody = new
            {
                jsonrpc = "2.0",
                method = "tools/list",
                @params = new { }, 
                id = 1 
            };

            using var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
            
            var response = await client.PostAsJsonAsync(_mcpUrl, requestBody);
            
            if (!response.IsSuccessStatusCode) return string.Empty;

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            
            Console.WriteLine($"[DEBUG] RAW TOOLS JSON: {json}");
            
            if (doc.RootElement.TryGetProperty("result", out var result) && 
                result.TryGetProperty("tools", out var tools))
            {
                var sb = new StringBuilder();
                foreach (var tool in tools.EnumerateArray())
                {
                    var name = tool.GetProperty("name").GetString();
                    var desc = tool.GetProperty("description").GetString();
                    
                    var paramName = "query"; 
                    if (tool.TryGetProperty("inputSchema", out var schema) && 
                        schema.TryGetProperty("properties", out var props))
                    {
                        foreach (var prop in props.EnumerateObject())
                        {
                            if (prop.Name == "siteId") continue;
                            
                            paramName = prop.Name;
                        }
                    }

                    sb.AppendLine($"- {name}({paramName}: string): {desc}");
                }
                
                var finalString = sb.ToString();
                
                Console.WriteLine($"[DEBUG] PARSED TOOLS INSTRUCTION:\n{finalString}");
                
                return finalString;
            }
        }
        catch 
        {
            return string.Empty;
        }
        return string.Empty;
    }

    private string ExtractMcpContent(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("error", out var error))
            {
                return $"Error: {error.GetProperty("message").GetString()}";
            }

            if (root.TryGetProperty("result", out var result) && 
                result.TryGetProperty("content", out var content) && 
                content.GetArrayLength() > 0)
            {
                return content[0].GetProperty("text").GetString() ?? "No data.";
            }
        }
        catch
        {
            // ignored
        }

        return json;
    }

    private async Task<string> RunInferenceAsync(string prompt, string systemMessage)
    {
        return await Task.Run(() =>
        {
            var sb = new StringBuilder();
            var fullPrompt = $"<|system|>\n{systemMessage}<|end|>\n<|user|>\n{prompt}<|end|>\n<|assistant|>";

            using var sequences = _tokenizer.Encode(fullPrompt);
            using var generatorParams = new GeneratorParams(_model);
            generatorParams.SetSearchOption("max_length", 4096);
            generatorParams.SetInputSequences(sequences);

            using var tokenizerStream = _tokenizer.CreateStream();
            using var generator = new Generator(_model, generatorParams);

            while (!generator.IsDone())
            {
                generator.ComputeLogits();
                generator.GenerateNextToken();
                var part = tokenizerStream.Decode(generator.GetSequence(0)[^1]);
                sb.Append(part);
                if (part.Contains("<|end|>")) break;
            }

            return sb.ToString().Replace("<|end|>", "").Trim();
        });
    }

    public void Dispose()
    {
        _tokenizer.Dispose();
        _model.Dispose();
    }
}