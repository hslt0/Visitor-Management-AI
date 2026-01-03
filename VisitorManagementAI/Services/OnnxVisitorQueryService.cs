using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.ML.OnnxRuntimeGenAI;
using VisitorManagementAI.Data;

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
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var data = await db.Checkins
            .Where(c => c.SiteId == siteId)
            .OrderByDescending(c => c.CheckinTimestamp)
            .Take(20)
            .Select(c => new {
                Who = c.VisitorName,
                Plate = c.VisitorVehicleRegistrationPlate,
                Unit = c.VisitorCustomOne,
                Arrived = c.CheckinTimestamp.ToString("yyyy-MM-dd HH:mm")
            })
            .ToListAsync();

        var jsonData = System.Text.Json.JsonSerializer.Serialize(data);

        return await Task.Run(() =>
        {
            var systemPrompt = "You are a database assistant. Use this data to answer questions:\n" + jsonData;
            var fullPrompt = $"<|system|>\n{systemPrompt}<|end|>\n<|user|>\n{userPrompt}<|end|>\n<|assistant|>";

            using var sequences = _tokenizer.Encode(fullPrompt);
            using var generatorParams = new GeneratorParams(_model);
            generatorParams.SetSearchOption("max_length", 4096);
            generatorParams.SetInputSequences(sequences);

            using var tokenizerStream = _tokenizer.CreateStream();
            using var generator = new Generator(_model, generatorParams);

            var sb = new StringBuilder();
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