using Microsoft.ML.OnnxRuntimeGenAI;

namespace VisitorManagementAI.Services;

public interface IVisitorQueryService
{
}

public class OnnxVisitorQueryService : IVisitorQueryService, IDisposable
{
    private readonly ILogger<OnnxVisitorQueryService> _logger;
    private readonly Model _model;
    private readonly Tokenizer _tokenizer;

    public OnnxVisitorQueryService(ILogger<OnnxVisitorQueryService> logger, IConfiguration config)
    {
        _logger = logger;
        var modelPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, config["AiSettings:ModelPath"]!);
        
        _logger.LogInformation("Loading model from: {Path}", modelPath);
        _model = new Model(modelPath);
        _tokenizer = new Tokenizer(_model);
    }

    public void Dispose()
    {
        _tokenizer.Dispose();
        _model.Dispose();
    }
}