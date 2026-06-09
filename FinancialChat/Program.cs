using FinancialChat.Services;

var builder = WebApplication.CreateBuilder(args);

// ── Blazor Server ─────────────────────────────────────────────────────────────
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// ── CodexService como Singleton ───────────────────────────────────────────────
// Singleton porque queremos UNA instancia de codex app-server corriendo
// Si necesitás una instancia por usuario, cambialo a Scoped y manejá el ciclo de vida
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
