using MCPServer.Data;
using MCPServer.Tools;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

builder.Services.AddScoped<VisitorTools>();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
            .AllowAnyMethod()
            .AllowAnyHeader();
    });
});

builder.Services.AddMcpServer(options =>
    {
        options.ServerInfo?.Name = "mcp-server";
        options.ServerInfo?.Version = "0.0.1";
        options.ServerInfo?.Title =  "MCP Server";
        options.Capabilities?.Tools?.ListChanged = true;
    })
    .WithHttpTransport()
    .WithToolsFromAssembly();

builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection"));
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
}

app.MapMcp("/api/mcp");
app.UseCors();

app.UseHttpsRedirection();

app.Run();

