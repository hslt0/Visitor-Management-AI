namespace VisitorManagementAI.Models;

/// <summary>
/// Represents a tool available via the Model Context Protocol (MCP).
/// </summary>
/// <param name="Name">The name of the tool.</param>
/// <param name="Description">A description of what the tool does.</param>
/// <param name="InputSchema">The schema defining the tool's input parameters.</param>
public record McpTool(string Name, string Description, McpInputSchema InputSchema);

/// <summary>
/// Defines the input schema for an MCP tool.
/// </summary>
/// <param name="Properties">A dictionary of property names and their definitions.</param>
public record McpInputSchema(Dictionary<string, object> Properties);

/// <summary>
/// Represents the result of a "tools/list" MCP request.
/// </summary>
/// <param name="Tools">The list of available tools.</param>
public record McpListToolsResult(List<McpTool> Tools);

/// <summary>
/// Represents a content item in an MCP tool call result.
/// </summary>
/// <param name="Type">The type of content (e.g., "text").</param>
/// <param name="Text">The content text.</param>
public record McpCallContent(string Type, string Text);

/// <summary>
/// Represents the result of a "tools/call" MCP request.
/// </summary>
/// <param name="Content">The list of content items returned by the tool.</param>
public record McpCallResult(List<McpCallContent> Content);