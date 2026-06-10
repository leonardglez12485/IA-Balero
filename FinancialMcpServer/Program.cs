using FinancialMcpServer.Data;
using FinancialMcpServer.Middleware;
using FinancialMcpServer.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Services.AddSingleton<DatabaseFactory>();
builder.Services.AddSingleton<ZafiroContextService>();

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
app.MapPost("/context/resolve", (ZafiroContextRequest request, ZafiroContextService contextService) =>
{
    if (string.IsNullOrWhiteSpace(request.Question))
        return Results.BadRequest(new { error = "La pregunta no puede estar vacia." });

    return Results.Ok(contextService.Resolve(request.Question));
});
app.MapMcp("/mcp");

app.Run();
