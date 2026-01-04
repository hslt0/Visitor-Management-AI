using System.Text;
using System.Text.RegularExpressions;
using Microsoft.ML.OnnxRuntimeGenAI;
using VisitorManagementAI.Models;

namespace VisitorManagementAI.Services;

public interface IVisitorQueryService
{
    Task<string> ChatAsync(string userPrompt, int siteId);
}

public class OnnxVisitorQueryService : IVisitorQueryService, IDisposable
{
    private readonly Model _model;
    private readonly Tokenizer _tokenizer;
    private readonly IServiceScopeFactory _scopeFactory;

    public OnnxVisitorQueryService(IConfiguration config, IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
        var modelPath = config["AiSettings:ModelPath"] ?? throw new InvalidOperationException();
        _model = new Model(Path.GetFullPath(modelPath));
        _tokenizer = new Tokenizer(_model);
    }

    public async Task<string> ChatAsync(string userPrompt, int siteId)
    {
        var now = DateTime.Now.ToString("f");

        var toolsDescription = """
                               Available tools:
                               - find_visitor(query: string): Search by name or license plate.
                               - get_unit_visitors(unit: string): List everyone in a specific unit.
                               """;

        var systemPrompt = $"""
                            Instruction: You are a security assistant with real-time access to the system clock.
                            The current date and time is strictly: {now}.

                            {toolsDescription}

                            RULES:
                            1. NEVER claim you do not know the date. You MUST use the date provided above.
                            2. If a user asks for the date, state it clearly as {now}.
                            3. For visitor lookups, ONLY reply with: [CALL: tool_name(parameter="value")]
                            """;

        var firstResponse = await RunInferenceAsync(userPrompt, systemPrompt);

        var match = Regex.Match(firstResponse, @"\[CALL:\s*([a-zA-Z0-9__]+)\s*\(\s*(?:.*?=)?\s*""?([^""]+)""?\s*\)\]");

        if (!match.Success) return firstResponse;
        
        var functionName = match.Groups[1].Value.ToLower();
        var queryValue = match.Groups[2].Value;

        using var scope = _scopeFactory.CreateScope();
        var visitorTools = scope.ServiceProvider.GetRequiredService<VisitorTools>();
        
        var toolResult = "";
        if (functionName.Contains("unit_visitors"))
        {
            toolResult = await visitorTools.GetUnitVisitors(queryValue, siteId);
        }
        else if (functionName.Contains("find_visitor"))
        {
            toolResult = await visitorTools.FindVisitor(queryValue, siteId);
        }

        if (string.IsNullOrEmpty(toolResult)) return firstResponse;
        var finalPrompt = $"User: {userPrompt}. Database: {toolResult}. Summarize naturally.";
        return await RunInferenceAsync(finalPrompt, "You are a helpful security assistant.");

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