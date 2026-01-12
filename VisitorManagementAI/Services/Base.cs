using VisitorManagementAI.Models;

namespace VisitorManagementAI.Services;

/// <summary>
/// Interface for a client that communicates with a Model Context Protocol (MCP) server.
/// </summary>
public interface IMcpClient
{
    /// <summary>
    /// Retrieves the list of available tools from the MCP server.
    /// </summary>
    /// <returns>A list of <see cref="McpTool"/> objects.</returns>
    Task<List<McpTool>> GetToolsAsync();

    /// <summary>
    /// Calls a specific tool on the MCP server.
    /// </summary>
    /// <param name="toolName">The name of the tool to call.</param>
    /// <param name="arguments">The arguments to pass to the tool.</param>
    /// <param name="siteId">The site ID context for the tool execution.</param>
    /// <returns>The result of the tool execution as a string.</returns>
    Task<string> CallToolAsync(string toolName, Dictionary<string, object> arguments, int siteId);

    /// <summary>
    /// Gets a list of known tool names that have been retrieved.
    /// </summary>
    /// <returns>A list of tool names.</returns>
    List<string> GetKnownTools();
}

/// <summary>
/// Interface for a service that handles visitor-related natural language queries.
/// </summary>
public interface IVisitorQueryService
{
    /// <summary>
    /// Processes a user prompt and generates a response.
    /// </summary>
    /// <param name="userPrompt">The user's question or command.</param>
    /// <param name="siteId">The site ID context for the query.</param>
    /// <returns>A <see cref="VisitorQueryResponse"/> containing the AI's answer and metadata.</returns>
    Task<VisitorQueryResponse> ChatAsync(string userPrompt, int siteId);
}