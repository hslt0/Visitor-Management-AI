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

        var toolsDescription = """
                               - find_visitor(query: string): Search by name or license plate.
                               - get_unit_visitors(unit: string): List everyone in a specific unit.
                               """;

        var systemPrompt = $"""
                            You are a helpful and polite security assistant. Current time: {now}.

                            {toolsDescription}

                            GUIDELINES:
                            1. You can chat naturally (say hello, answer general questions).
                            2. If the user asks about visitors, units, or people, YOU MUST use: [CALL: tool_name(parameter="value")]
                            3. Use the current year 2026 for all time calculations.

                            IMPORTANT PARAMETER RULES:
                            - The 'query' parameter must contain ONLY the Name or License Plate.
                            - DO NOT include words like "check", "search", "last visit", "who is".

                            EXAMPLES:
                            - User: "When was Alex here?" -> [CALL: find_visitor(query="Alex")]
                            - User: "Check plate ABC123" -> [CALL: find_visitor(query="ABC123")]
                            - User: "Who visited unit 505?" -> [CALL: get_unit_visitors(unit="505")]
                            """;

        var firstResponse = await RunInferenceAsync(userPrompt, systemPrompt);

        var match = Regex.Match(firstResponse, @"\[CALL:\s*([a-zA-Z0-9__]+)\s*\(\s*(?:.*?=)?\s*(.*)\s*\)\]");

        if (!match.Success) return firstResponse;

        var toolName = match.Groups[1].Value;
        var rawValue = match.Groups[2].Value;

        var queryValue = rawValue.Trim().Trim('"', '\'');

        Console.WriteLine($"[DEBUG] Tool: {toolName}, Query: '{queryValue}'");

        var requestBody = new
        {
            jsonrpc = "2.0",
            method = "tools/call",
            @params = new
            {
                name = toolName,
                arguments = new Dictionary<string, object>
                {
                    { toolName.Contains("unit") ? "unit" : "query", queryValue },
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
            return $"I encountered a connection error: {ex.Message}";
        }
    }

    private string ExtractMcpContent(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("error", out var error))
            {
                return $"Error from server: {error.GetProperty("message").GetString()}";
            }

            if (root.TryGetProperty("result", out var result) && 
                result.TryGetProperty("content", out var content) && 
                content.GetArrayLength() > 0)
            {
                return content[0].GetProperty("text").GetString() ?? "No data returned.";
            }
        }
        catch { /* Fallback to raw JSON if parsing fails */ }
        
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