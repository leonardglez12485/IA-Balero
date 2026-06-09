using System.ComponentModel;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dapper;
using FinancialMcpServer.Data;
using ModelContextProtocol.Server;

namespace FinancialMcpServer.Tools;

[McpServerToolType]
public class FinancialTools(DatabaseFactory db, ILogger<FinancialTools> logger)
{
    private const int MaxRows = 500;

    [McpServerTool, Description(
        "Retorna el esquema de SQL Server con schemas, tablas, columnas, tipos y nulabilidad. " +
        "Codex debe usar esta herramienta antes de generar SQL.")]
    public async Task<string> ObtenerEsquemaBaseDatos()
    {
        const string sql = """
            SELECT
                s.name AS Esquema,
                t.name AS Tabla,
                c.column_id AS Orden,
                c.name AS Columna,
                ty.name AS Tipo,
                c.max_length AS Largo,
                c.precision AS Precision,
                c.scale AS Escala,
                c.is_nullable AS EsNullable
            FROM sys.tables t
            INNER JOIN sys.schemas s ON s.schema_id = t.schema_id
            INNER JOIN sys.columns c ON c.object_id = t.object_id
            INNER JOIN sys.types ty ON ty.user_type_id = c.user_type_id
            WHERE t.is_ms_shipped = 0
            ORDER BY s.name, t.name, c.column_id;
            """;

        try
        {
            using var conn = db.CreateConnection();
            var columnas = (await conn.QueryAsync(sql)).ToList();

            if (columnas.Count == 0)
                return "No se encontraron tablas de usuario en la base de datos.";

            var sb = new StringBuilder();
            sb.AppendLine("=== ESQUEMA SQL SERVER ===");

            foreach (var tabla in columnas.GroupBy(r => $"{r.Esquema}.{r.Tabla}"))
            {
                sb.AppendLine();
                sb.AppendLine($"TABLA: {tabla.Key}");

                foreach (var col in tabla)
                {
                    var tipo = FormatSqlType(col.Tipo, col.Largo, col.Precision, col.Escala);
                    var nullable = col.EsNullable ? "NULL" : "NOT NULL";
                    sb.AppendLine($"  - {col.Columna} ({tipo}) {nullable}");
                }
            }

            return sb.ToString();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error obteniendo esquema SQL Server");
            return "Error al obtener el esquema de la base de datos.";
        }
    }

    [McpServerTool, Description(
        "Ejecuta una consulta SQL Server readonly generada por Codex. " +
        "Solo acepta SELECT o CTE WITH que terminen en SELECT. No modifica datos.")]
    public async Task<string> EjecutarConsultaSelect(
        [Description("SQL Server SELECT readonly generado por Codex para responder al usuario")]
        string sqlSelect,
        [Description("Resumen breve de la consulta en lenguaje natural")]
        string descripcion)
    {
        var validation = ValidateSelect(sqlSelect);
        if (!validation.IsValid)
            return validation.Error!;

        var sql = EnsureTopLimit(validation.Sql!);

        try
        {
            logger.LogInformation("Consulta readonly: {Descripcion} | SQL: {Sql}", descripcion, sql);

            using var conn = db.CreateConnection();
            var resultados = (await conn.QueryAsync(sql)).ToList();

            if (resultados.Count == 0)
                return "La consulta no retornó resultados.";

            return JsonSerializer.Serialize(resultados, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error ejecutando consulta readonly");
            return "Error al ejecutar la consulta. Revisar logs del servidor.";
        }
    }

    private static (bool IsValid, string? Sql, string? Error) ValidateSelect(string? sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return (false, null, "Error: la consulta SQL está vacía.");

        var clean = RemoveSqlComments(sql).Trim();
        if (string.IsNullOrWhiteSpace(clean))
            return (false, null, "Error: la consulta SQL está vacía.");

        if (clean.Contains(';') && clean.TrimEnd().TrimEnd(';').Contains(';'))
            return (false, null, "Error: solo se permite una consulta por ejecución.");

        clean = clean.TrimEnd().TrimEnd(';').Trim();

        if (!Regex.IsMatch(clean, @"^\s*(SELECT|WITH)\b", RegexOptions.IgnoreCase))
            return (false, null, "Error: solo se permiten consultas SELECT.");

        var blocked = new[]
        {
            "ALTER", "CREATE", "DELETE", "DROP", "EXEC", "EXECUTE", "INSERT", "MERGE",
            "TRUNCATE", "UPDATE", "GRANT", "REVOKE", "DENY", "BACKUP", "RESTORE"
        };

        foreach (var keyword in blocked)
        {
            if (Regex.IsMatch(clean, $@"\b{keyword}\b", RegexOptions.IgnoreCase))
                return (false, null, $"Error: la consulta contiene una operación no permitida ({keyword}).");
        }

        return (true, clean, null);
    }

    private static string EnsureTopLimit(string sql)
    {
        if (!Regex.IsMatch(sql, @"^\s*SELECT\b", RegexOptions.IgnoreCase))
            return sql;

        if (Regex.IsMatch(sql, @"^\s*SELECT\s+(DISTINCT\s+)?TOP\s*\(", RegexOptions.IgnoreCase) ||
            Regex.IsMatch(sql, @"^\s*SELECT\s+(DISTINCT\s+)?TOP\s+\d+", RegexOptions.IgnoreCase))
            return sql;

        return Regex.Replace(
            sql,
            @"^\s*SELECT\s+(DISTINCT\s+)?",
            m => m.Value + $"TOP ({MaxRows}) ",
            RegexOptions.IgnoreCase);
    }

    private static string RemoveSqlComments(string sql)
    {
        var noBlockComments = Regex.Replace(sql, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
        return Regex.Replace(noBlockComments, @"--.*?$", string.Empty, RegexOptions.Multiline);
    }

    private static string FormatSqlType(string tipo, short largo, byte precision, byte escala)
    {
        return tipo.ToLowerInvariant() switch
        {
            "varchar" or "char" or "binary" or "varbinary" => $"{tipo}({FormatLength(largo)})",
            "nvarchar" or "nchar" => $"{tipo}({FormatLength((short)(largo / 2))})",
            "decimal" or "numeric" => $"{tipo}({precision},{escala})",
            "datetime2" or "datetimeoffset" or "time" => $"{tipo}({escala})",
            _ => tipo
        };
    }

    private static string FormatLength(short largo) => largo == -1 ? "max" : largo.ToString();
}
