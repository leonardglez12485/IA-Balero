namespace FinancialMcpServer.Middleware;

/// <summary>
/// Valida el Bearer token en cada request al endpoint /mcp.
/// Si el token no está en la lista de appsettings, devuelve 401.
/// </summary>
public class TokenAuthMiddleware(RequestDelegate next, IConfiguration config, ILogger<TokenAuthMiddleware> logger)
{
    private readonly HashSet<string> _validTokens = new(
        config.GetSection("McpSecurity:ValidTokens").Get<string[]>() ?? [],
        StringComparer.Ordinal
    );

    public async Task InvokeAsync(HttpContext context)
    {
        // Solo protegemos el endpoint MCP
        if (context.Request.Path.StartsWithSegments("/mcp"))
        {
            context.Request.Headers.TryGetValue("Authorization", out var authValues);
            var authHeader = authValues.FirstOrDefault();
            var token = authHeader?.StartsWith("Bearer ") == true
                ? authHeader["Bearer ".Length..].Trim()
                : null;

            if (token is null || !_validTokens.Contains(token))
            {
                logger.LogWarning("Acceso denegado desde {IP} — token inválido o ausente.",
                    context.Connection.RemoteIpAddress);
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsync("Token inválido o ausente.");
                return;
            }

            // Loguear qué cliente está consultando (útil para auditoría)
            logger.LogInformation("Request MCP autorizado — token: {Token} | path: {Path}",
                token[..Math.Min(8, token.Length)] + "***",
                context.Request.Path);
        }

        await next(context);
    }
}
