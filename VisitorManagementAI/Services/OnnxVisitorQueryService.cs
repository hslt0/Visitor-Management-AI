using System.Text;
using System.Text.Json;
using Microsoft.ML.OnnxRuntimeGenAI;
using VisitorManagementAI.Models;
using VisitorManagementAI.Utilities;

namespace VisitorManagementAI.Services;

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

    public async Task<VisitorQueryResponse> ChatAsync(string userPrompt, int siteId)
    {
        var now = DateTime.Now.ToString("F", System.Globalization.CultureInfo.InvariantCulture);
        var tools = await _mcpClient.GetToolsAsync();

        var systemPrompt = AiUtilities.BuildNativeSystemPrompt(tools, now);
        
        _logger.LogInformation("System prompt with native tools generated.");

        var firstResponse = await RunInferenceAsync(userPrompt, systemPrompt);
        _logger.LogInformation("RAW AI RESPONSE: >>> {Response} <<<", firstResponse);

        var (toolName, arguments) = ParseNativeToolOutput(firstResponse);

        if (string.IsNullOrEmpty(toolName))
        {
            return new VisitorQueryResponse(
                firstResponse,
                null,
                null,
                DateTime.Now
                );
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

        _logger.LogInformation("AI requesting tool: {Tool} with arguments: {Arguments}", toolName, arguments);

        var rawJsonResult = await _mcpClient.CallToolAsync(toolName, arguments, siteId);
        var humanReadableData = AiUtilities.FormatJsonForAi(rawJsonResult);

        var finalPrompt = $"""
                           Current System Time: {now}
                           User Question: "{userPrompt}"
                           Data: {humanReadableData}
                           Instruction: Summarize naturally.
                           """;
        
        var finalResponse = await RunInferenceAsync(finalPrompt, "You are a helpful assistant.");

        return new VisitorQueryResponse(
            finalResponse,
            rawJsonResult,
            toolName,
            DateTime.Now
            );
    }

    private (string, Dictionary<string, object> arguments) ParseNativeToolOutput(string response)
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

    private Dictionary<string, object> ExtractArguments(JsonElement element)
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
                    result[prop.Name] = ""; // Handle null as empty string or appropriate default
                    break;
                default:
                    result[prop.Name] = prop.Value.ToString();
                    break;
            }
        }
        
        return result;
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