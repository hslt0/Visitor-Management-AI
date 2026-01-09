using System.Text;
using System.Text.Json;
using Microsoft.ML.OnnxRuntimeGenAI;
using VisitorManagementAI.Models;
using VisitorManagementAI.Utilities;

namespace VisitorManagementAI.Services;

public interface IVisitorQueryService
{
    Task<string> ChatAsync(string userPrompt, int siteId);
}

public class OnnxVisitorQueryService : IVisitorQueryService, IDisposable
{
    private readonly Model _model;
    private readonly Tokenizer _tokenizer;
    private readonly IMcpClient _mcpClient;
    private readonly ILogger<OnnxVisitorQueryService> _logger;

    public OnnxVisitorQueryService(IConfiguration config, IMcpClient mcpClient, ILogger<OnnxVisitorQueryService> logger)
    {
        _mcpClient = mcpClient;
        _logger = logger;
        var modelPath = config["AiSettings:ModelPath"] ?? throw new InvalidOperationException("Model path not configured.");
        _model = new Model(Path.GetFullPath(modelPath));
        _tokenizer = new Tokenizer(_model);
    }

    public async Task<string> ChatAsync(string userPrompt, int siteId)
    {
        var now = DateTime.Now.ToString("F", System.Globalization.CultureInfo.InvariantCulture);
        var tools = await _mcpClient.GetToolsAsync();

        var systemPrompt = BuildNativeSystemPrompt(tools, now);
        
        _logger.LogInformation("System prompt with native tools generated.");

        var firstResponse = await RunInferenceAsync(userPrompt, systemPrompt);
        _logger.LogInformation("RAW AI RESPONSE: >>> {Response} <<<", firstResponse);

        var (toolName, queryValue) = ParseNativeToolOutput(firstResponse);

        if (string.IsNullOrEmpty(toolName))
        {
            return firstResponse;
        }
        
        var validTools = _mcpClient.GetKnownTools();
        if (validTools.Any() && !validTools.Contains(toolName))
        {
             var bestMatch = validTools
                .Select(validName => new { Name = validName, Distance = AiUtilities.CalculateDistance(toolName, validName) })
                .OrderBy(x => x.Distance)
                .First();
             toolName = bestMatch.Name;
        }

        _logger.LogInformation("AI requesting tool: {Tool} with query: {Query}", toolName, queryValue);

        var rawJsonResult = await _mcpClient.CallToolAsync(toolName, queryValue, siteId);
        var humanReadableData = AiUtilities.FormatJsonForAi(rawJsonResult);

        var finalPrompt = $"""
                           Current System Time: {now}
                           User Question: "{userPrompt}"
                           Data: {humanReadableData}
                           Instruction: Summarize naturally.
                           """;
        
        return await RunInferenceAsync(finalPrompt, "You are a helpful assistant.");
    }

    private (string toolName, string queryValue) ParseNativeToolOutput(string response)
    {
        try
        {
            var jsonStart = response.IndexOf('[');
            var jsonEnd = response.LastIndexOf(']');

            if (jsonStart == -1 || jsonEnd == -1 || jsonEnd < jsonStart)
                return (string.Empty, string.Empty);

            var json = response.Substring(jsonStart, jsonEnd - jsonStart + 1);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0)
                return (string.Empty, string.Empty);

            var toolObj = root[0];
            
            if (!toolObj.TryGetProperty("name", out var nameProp))
                return (string.Empty, string.Empty);

            var toolName = nameProp.GetString();
            var queryValue = string.Empty;

            if (toolObj.TryGetProperty("parameters", out var paramsProp))
            {
                if (paramsProp.TryGetProperty("Properties", out var propsProp))
                {
                    queryValue = ExtractValue(propsProp);
                }
                else
                {
                    queryValue = ExtractValue(paramsProp);
                }
            }

            return (toolName ?? string.Empty, queryValue);
        }
        catch
        {
            return (string.Empty, string.Empty);
        }
    }

    private string ExtractValue(JsonElement element)
    {
        if (element.TryGetProperty("query", out var q)) return q.GetString() ?? "";
        if (element.TryGetProperty("unit", out var u)) return u.GetString() ?? "";
        
        foreach (var prop in element.EnumerateObject())
        {
            if (prop.Value.ValueKind == JsonValueKind.String)
                return prop.Value.GetString() ?? "";
        }
        return "";
    }

    private string BuildNativeSystemPrompt(List<McpTool> tools, string now)
    {
        var toolsForAi = tools.Select(t => new 
        {
            name = t.Name,
            description = t.Description,
            parameters = t.InputSchema
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

    private async Task<string> RunInferenceAsync(string prompt, string systemMessage)
    {
        return await Task.Run(() =>
        {
            var sb = new StringBuilder();
            var fullPrompt = $"<|system|>\n{systemMessage}<|end|>\n<|user|>\n{prompt}<|end|>\n<|assistant|>";

            using var sequences = _tokenizer.Encode(fullPrompt);
            using var generatorParams = new GeneratorParams(_model);
            generatorParams.SetSearchOption("max_length", 2048);
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
        GC.SuppressFinalize(this);
    }
}