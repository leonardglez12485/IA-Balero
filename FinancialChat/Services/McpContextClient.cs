using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace FinancialChat.Services;

public sealed class McpContextClient(IConfiguration config, IHttpClientFactory httpClientFactory, ILogger<McpContextClient> logger)
{
    private static readonly TimeSpan ResolveTimeout = TimeSpan.FromSeconds(5);

    public async Task<string> ResolveContextAsync(string question, CancellationToken cancellationToken = default)
    {
        var endpoint = GetContextEndpoint();
        if (endpoint is null)
            return BuildFallbackContext();

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(ResolveTimeout);

            var client = httpClientFactory.CreateClient(nameof(McpContextClient));
            ApplyBearerToken(client);

            using var response = await client.PostAsJsonAsync(endpoint, new McpContextRequest(question), cts.Token);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("No se pudo resolver contexto MCP. Status={StatusCode}", response.StatusCode);
                return BuildFallbackContext();
            }

            var resolved = await response.Content.ReadFromJsonAsync<McpContextResponse>(cancellationToken: cts.Token);
            if (!string.IsNullOrWhiteSpace(resolved?.Context))
                return resolved.Context;

            logger.LogWarning("El endpoint de contexto MCP respondio sin contexto util.");
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("Timeout resolviendo contexto Zafiro desde MCP.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error resolviendo contexto Zafiro desde MCP.");
        }

        return BuildFallbackContext();
    }

    private Uri? GetContextEndpoint()
    {
        var mcpUrl = config["Codex:McpServerUrl"];
        if (string.IsNullOrWhiteSpace(mcpUrl) || !Uri.TryCreate(mcpUrl, UriKind.Absolute, out var uri))
        {
            logger.LogWarning("Codex:McpServerUrl no configurado o invalido. No se resolvera contexto previo por MCP.");
            return null;
        }

        var builder = new UriBuilder(uri)
        {
            Path = "/context/resolve",
            Query = string.Empty,
            Fragment = string.Empty
        };

        return builder.Uri;
    }

    private void ApplyBearerToken(HttpClient client)
    {
        var token = config["Codex:McpToken"];
        if (!string.IsNullOrWhiteSpace(token))
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    private static string BuildFallbackContext()
    {
        return """
            Contexto Zafiro minimo:
            - Resolver contexto especifico desde MCP no estuvo disponible.
            - Si la pregunta pide datos reales, usar las herramientas MCP disponibles antes de responder.
            - Ejecutar solo SELECT/CTE readonly.
            - No inventar tablas ni columnas; si falta contexto, usar ObtenerEsquemaBaseDatos.
            - Para metricas, explicar filtros y si el resultado es neto o bruto.
            """;
    }

    private sealed record McpContextRequest(string Question);

    private sealed record McpContextResponse(string? Context, string[]? Intents, string? Source);
}
