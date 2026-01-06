using System.Text;
using System.Text.RegularExpressions;
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
    private readonly IMcpClient _mcpClient;
    private readonly ILogger<OnnxVisitorQueryService> _logger;

    private static readonly Regex ToolCallRegex = new(@"\[CALL:\s*([a-zA-Z0-9_]+)\s*\(\s*(?:.*?=)?\s*(.*)\s*\)\]", RegexOptions.Compiled);

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
        var toolsDescription = await _mcpClient.GetToolsDescriptionAsync();
        if (string.IsNullOrEmpty(toolsDescription))
        {
             toolsDescription = "- find_visitor(query: string): Search by name/plate.\n- get_unit_visitors(unit: string): Search unit.";
        }

        var systemPrompt = BuildSystemPrompt(toolsDescription);

        var firstResponse = await RunInferenceAsync(userPrompt, systemPrompt);

        var match = ToolCallRegex.Match(firstResponse);
        if (!match.Success) return firstResponse;

        var (toolName, queryValue) = ParseAndCorrectToolCall(match);
        
        _logger.LogInformation("AI requesting tool: {Tool} with query: {Query}", toolName, queryValue);

        var toolResult = await _mcpClient.CallToolAsync(toolName, queryValue, siteId);

        var finalPrompt = $"User asked: {userPrompt}. Database result: {toolResult}. Summarize naturally.";
        return await RunInferenceAsync(finalPrompt, "You are a helpful security assistant.");
    }

    private (string toolName, string queryValue) ParseAndCorrectToolCall(Match match)
    {
        var toolName = match.Groups[1].Value;
        var queryValue = match.Groups[2].Value.Trim().Trim('"', '\'');
        
        return (toolName, queryValue);
    }

    private string BuildSystemPrompt(string toolsDescription)
    {
        return $"""
                You are a security assistant. Current time: {DateTime.Now:f}.

                AVAILABLE TOOLS:
                {toolsDescription}

                CRITICAL RULES:
                1. USE ONLY THE EXACT TOOL NAMES from the list above.
                2. The tool for searching visitors is 'find_visitor'.
                3. The tool for checking unit residents is 'get_unit_visitors'.
                4. NEVER invent names like 'check_unit_members'.
                
                EXAMPLES:
                - User: "Who is in unit 505?" -> Assistant: [CALL: get_unit_visitors(unit="505")]
                - User: "Find Alex" -> Assistant: [CALL: find_visitor(query="Alex")]
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