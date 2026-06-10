using System.Text;

namespace FinancialMcpServer.Services;

public sealed class ZafiroContextService(IWebHostEnvironment env, ILogger<ZafiroContextService> logger)
{
    private const int MaxContextChars = 6_000;
    private string? _businessKnowledgeMarkdown;

    public ZafiroContextResponse Resolve(string? question)
    {
        var userText = NormalizeForIntent(question ?? string.Empty);
        var markdown = LoadBusinessKnowledgeMarkdown();
        var selectedIntents = new List<string>();
        var sb = new StringBuilder();

        sb.AppendLine("Contexto Zafiro minimo resuelto por MCP:");
        sb.AppendLine("- El MCP selecciono este contexto antes de enviar la pregunta a OpenAI.");
        sb.AppendLine("- Usar MCP financiero y SQL Server para datos reales.");
        sb.AppendLine("- Ejecutar solo SELECT/CTE readonly.");
        sb.AppendLine("- No inventar tablas ni columnas; si falta contexto, usar ObtenerEsquemaBaseDatos.");
        sb.AppendLine("- Para metricas, explicar filtros y si el resultado es neto o bruto.");

        AddSectionIfAny(sb, selectedIntents, markdown, userText,
            "ventas",
            ["venta", "ventas", "factura", "facturas", "ticket", "tickets", "credito", "creditos", "nota", "notas", "nc", "vendedor", "vendedores", "sucursal", "sucursales", "margen", "producto", "productos"],
            ["## Claves y joins frecuentes", "## Reglas de ventas y notas de credito", "## Filtros de validez"]);

        AddSectionIfAny(sb, selectedIntents, markdown, userText,
            "stock",
            ["stock", "inventario", "rotacion", "deposito", "depositos", "ajuste", "ajustes", "existencia", "existencias"],
            ["## Stock y rotacion"]);

        AddSectionIfAny(sb, selectedIntents, markdown, userText,
            "compras",
            ["compra", "compras", "proveedor", "proveedores", "costo", "costos"],
            ["## Compras"]);

        AddSectionIfAny(sb, selectedIntents, markdown, userText,
            "ordenes-trabajo",
            ["orden", "ordenes", "trabajo", "ot", "receta", "cristal", "cristales", "armazon", "armazones", "lente", "lentes"],
            ["## Arquitectura de datos observada", "## Claves y joins frecuentes"]);

        if (selectedIntents.Count == 0)
        {
            selectedIntents.Add("minimo");
            logger.LogInformation("Contexto Zafiro minimo resuelto sin intencion especifica.");
        }

        var context = sb.ToString();
        if (context.Length > MaxContextChars)
            context = context[..MaxContextChars] + "\n\n[Contexto Zafiro truncado por MCP para reducir tokens.]";

        return new ZafiroContextResponse(context, selectedIntents.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), "FinancialMcpServer");
    }

    private string LoadBusinessKnowledgeMarkdown()
    {
        if (_businessKnowledgeMarkdown is not null)
            return _businessKnowledgeMarkdown;

        var path = Path.Combine(env.ContentRootPath, "Knowledge", "ZafiroBusinessKnowledge.md");
        if (!File.Exists(path))
        {
            logger.LogWarning("No se encontro la base de conocimiento Zafiro en {Path}", path);
            _businessKnowledgeMarkdown = string.Empty;
            return _businessKnowledgeMarkdown;
        }

        _businessKnowledgeMarkdown = File.ReadAllText(path);
        return _businessKnowledgeMarkdown;
    }

    private static void AddSectionIfAny(
        StringBuilder sb,
        List<string> selectedIntents,
        string markdown,
        string text,
        string intent,
        string[] keywords,
        string[] headings)
    {
        if (!keywords.Any(text.Contains))
            return;

        selectedIntents.Add(intent);

        foreach (var heading in headings)
        {
            var section = ExtractMarkdownSection(markdown, heading);
            if (!string.IsNullOrWhiteSpace(section) && !sb.ToString().Contains(heading, StringComparison.Ordinal))
                sb.AppendLine().AppendLine(section.Trim());
        }
    }

    private static string ExtractMarkdownSection(string markdown, string heading)
    {
        if (string.IsNullOrWhiteSpace(markdown))
            return string.Empty;

        var start = markdown.IndexOf(heading, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
            return string.Empty;

        var next = markdown.IndexOf("\n## ", start + heading.Length, StringComparison.OrdinalIgnoreCase);
        return next < 0 ? markdown[start..] : markdown[start..next];
    }

    private static string NormalizeForIntent(string text)
    {
        return (text ?? string.Empty)
            .ToLowerInvariant()
            .Replace('á', 'a')
            .Replace('é', 'e')
            .Replace('í', 'i')
            .Replace('ó', 'o')
            .Replace('ú', 'u');
    }
}

public sealed record ZafiroContextRequest(string? Question);

public sealed record ZafiroContextResponse(string Context, string[] Intents, string Source);
