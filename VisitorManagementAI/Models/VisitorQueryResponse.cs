namespace VisitorManagementAI.Models;

public record VisitorQueryResponse(string AiResponseText, string? DataContext, string? ToolUsed, DateTime Timestamp);