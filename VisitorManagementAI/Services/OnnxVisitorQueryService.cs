using System.Text;
using Microsoft.ML.OnnxRuntimeGenAI;

namespace VisitorManagementAI.Services;

public interface IVisitorQueryService
{
    Task<string> ChatAsync(string userPrompt);
}

public class OnnxVisitorQueryService : IVisitorQueryService, IDisposable
{
    private readonly Model _model;
    private readonly Tokenizer _tokenizer;

    public OnnxVisitorQueryService(IConfiguration config)
    {
        var modelPath = config["AiSettings:ModelPath"];

        if (string.IsNullOrEmpty(modelPath))
            throw new InvalidOperationException("Model path is not configured.");

        var fullPath = Path.GetFullPath(modelPath);

        _model = new Model(fullPath);
        _tokenizer = new Tokenizer(_model);
    }

    public async Task<string> ChatAsync(string userPrompt)
    {
        return await Task.Run(() =>
        {
            if (_model == null || _tokenizer == null)
                throw new InvalidOperationException("Model is not ready");

            var sb = new StringBuilder();
            var fullPrompt = $"<|user|>\n{userPrompt}<|end|>\n<|assistant|>";

            using var sequences = _tokenizer.Encode(fullPrompt);

            using var generatorParams = new GeneratorParams(_model);

            generatorParams.SetSearchOption("max_length", 2048.0);

            generatorParams.SetInputSequences(sequences);

            generatorParams.TryGraphCaptureWithMaxBatchSize(1);

            using var tokenizerStream = _tokenizer.CreateStream();

            using var generator = new Generator(_model, generatorParams);

            while (!generator.IsDone())
            {
                generator.ComputeLogits();
                generator.GenerateNextToken();

                var part = tokenizerStream.Decode(generator.GetSequence(0)[^1]);
            
                sb.Append(part);

                if (sb.ToString().EndsWith("<|end|>") || sb.ToString().EndsWith("<|user|>"))
                {
                    break;
                }
            }

            return sb.ToString().Replace("<|end|>", "").Replace("<|user|>", "").Trim();
        });
    }

    public void Dispose()
    {
        _tokenizer.Dispose();
        _model.Dispose();
    }
}