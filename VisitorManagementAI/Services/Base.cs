using VisitorManagementAI.Models;

namespace VisitorManagementAI.Services;

public interface IMcpClient
{
    Task<List<McpTool>> GetToolsAsync();

    Task<string> CallToolAsync(string toolName, Dictionary<string, object> arguments, int siteId);
    List<string> GetKnownTools();
}

public interface IVisitorQueryService
{
    Task<VisitorQueryResponse> ChatAsync(string userPrompt, int siteId);
}