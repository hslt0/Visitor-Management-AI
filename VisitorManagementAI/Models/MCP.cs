namespace VisitorManagementAI.Models;

public record McpTool(string Name, string Description, McpInputSchema InputSchema);

public record McpInputSchema(Dictionary<string, object> Properties);

public record McpListToolsResult(List<McpTool> Tools);

public record McpCallContent(string Type, string Text);

public record McpCallResult(List<McpCallContent> Content);