var builder = DistributedApplication.CreateBuilder(args);

var mcpServer = builder.AddProject<Projects.MCPServer>("mcp-server");

builder.AddProject<Projects.VisitorManagementAI>("visitor-management-ai")
    .WithReference(mcpServer)
    .WithEnvironment("McpServerUrl", mcpServer.GetEndpoint("https"));

builder.Build().Run();