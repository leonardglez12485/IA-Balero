using FinancialMcpServer.Data;
using FinancialMcpServer.Middleware;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Services.AddSingleton<DatabaseFactory>();

builder.Services.AddMcpServer(options =>
    {
        options.ServerInfo = new()
        {
            Name = "financial-sql-server",
            Version = "1.0.0"
        };
    })
    .WithHttpTransport()
    .WithToolsFromAssembly();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}
app.UseMiddleware<TokenAuthMiddleware>();

app.MapGet("/", () => Results.Ok("Financial MCP Server OK"));
app.MapMcp("/mcp");

app.Run();
