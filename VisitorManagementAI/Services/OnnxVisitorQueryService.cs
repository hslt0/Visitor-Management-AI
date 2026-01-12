using System.Text;
using Microsoft.ML.OnnxRuntimeGenAI;
using VisitorManagementAI.Models;
using VisitorManagementAI.Utilities;

namespace VisitorManagementAI.Services;

/// <summary>
/// Service for handling visitor queries using an ONNX Runtime GenAI model.
/// </summary>
public class OnnxVisitorQueryService : IVisitorQueryService, IDisposable
{
    private readonly Model _model;
    private readonly Tokenizer _tokenizer;
    private readonly IMcpClient _mcpClient;
    private readonly ILogger<OnnxVisitorQueryService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="OnnxVisitorQueryService"/> class.
    /// </summary>
    /// <param name="config">The configuration instance.</param>
    /// <param name="mcpClient">The MCP client instance.</param>
    /// <param name="logger">The logger instance.</param>
    /// <exception cref="InvalidOperationException">Thrown if the model path is not configured.</exception>
    public OnnxVisitorQueryService(IConfiguration config, IMcpClient mcpClient, ILogger<OnnxVisitorQueryService> logger)
    {
        _mcpClient = mcpClient;
        _logger = logger;
        var modelPath = config["AiSettings:ModelPath"] ?? throw new InvalidOperationException("Model path not configured.");
        _model = new Model(Path.GetFullPath(modelPath));
        _tokenizer = new Tokenizer(_model);
    }

    /// <inheritdoc />
    public async Task<VisitorQueryResponse> ChatAsync(string userPrompt, int siteId)
    {
        var now = DateTime.Now.ToString("F", System.Globalization.CultureInfo.InvariantCulture);
        
        // 1. Fetch available tools from the MCP server
        var tools = await _mcpClient.GetToolsAsync();

        // 2. Build the system prompt, including tool definitions (hiding siteId)
        var systemPrompt = AiUtilities.BuildNativeSystemPrompt(tools, now);
        
        _logger.LogInformation("System prompt with native tools generated.");

        // 3. Run the first inference to see if the AI wants to call a tool
        var firstResponse = await RunInferenceAsync(userPrompt, systemPrompt);
        _logger.LogInformation("RAW AI RESPONSE: >>> {Response} <<<", firstResponse);

        // 4. Parse the AI's response to check for tool calls
        var (toolName, arguments) = AiUtilities.ParseNativeToolOutput(firstResponse);

        // If no tool is called, return the AI's direct response
        if (string.IsNullOrEmpty(toolName))
        {
            return new VisitorQueryResponse(
                firstResponse,
                null,
                null,
                DateTime.Now
                );
        }
        
        // 5. Validate and correct the tool name if necessary (fuzzy matching)
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

        // 6. Execute the tool call
        var rawJsonResult = await _mcpClient.CallToolAsync(toolName, arguments, siteId);
        
        // 7. Format the tool output for the AI
        var humanReadableData = AiUtilities.FormatJsonForAi(rawJsonResult);

        // 8. Construct a final prompt with the tool data for summarization
        var finalPrompt = $"""
                           Current System Time: {now}
                           User Question: "{userPrompt}"
                           Data: {humanReadableData}
                           Instruction: Summarize naturally.
                           """;
        
        // 9. Run the second inference to generate the final natural language response
        var finalResponse = await RunInferenceAsync(finalPrompt, "You are a helpful assistant.");

        return new VisitorQueryResponse(
            finalResponse,
            rawJsonResult,
            toolName,
            DateTime.Now
            );
    }
    
    /// <summary>
    /// Runs the AI inference using the ONNX model.
    /// </summary>
    /// <param name="prompt">The user prompt.</param>
    /// <param name="systemMessage">The system message/context.</param>
    /// <returns>The generated text response.</returns>
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

    /// <inheritdoc />
    public void Dispose()
    {
        _tokenizer.Dispose();
        _model.Dispose();
        GC.SuppressFinalize(this);
    }
}