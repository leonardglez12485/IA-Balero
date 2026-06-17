using FinancialChat.Services;

var builder = WebApplication.CreateBuilder(args);

var pathToExe = builder.Environment.ContentRootPath;
Directory.SetCurrentDirectory(pathToExe);
var configfile = Path.Combine(pathToExe, "config.json");

File.WriteAllLines(Path.Combine(pathToExe, "log.txt"), new[] { $"pathToExe={pathToExe}", $"configfile={configfile}" });

builder.Configuration.AddJsonFile(configfile, optional: true, reloadOnChange: true);

// ── Blazor Server ─────────────────────────────────────────────────────────────
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddHttpClient();

// ── CodexService como Singleton ───────────────────────────────────────────────
// Singleton porque queremos UNA instancia de codex app-server corriendo
// Si necesitás una instancia por usuario, cambialo a Scoped y manejá el ciclo de vida
builder.Services.AddSingleton<McpContextClient>();
builder.Services.AddSingleton<CodexService>();

// ── Logging ───────────────────────────────────────────────────────────────────
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

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
app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<FinancialChat.Components.App>()
    .AddInteractiveServerRenderMode();

app.Run();
