using System.Data;
using Microsoft.Data.SqlClient;
using MySql.Data.MySqlClient;
using Npgsql;

namespace FinancialMcpServer.Data;

/// <summary>
/// Fábrica de conexiones. Lee el proveedor y connection string desde appsettings.
/// Las credenciales nunca salen del servidor — el cliente solo ve la URL del MCP.
/// </summary>
public class DatabaseFactory(IConfiguration config)
{
    private readonly string _provider         = config["Database:Provider"] ?? "SqlServer";
    private readonly string _connectionString = config["Database:ConnectionString"]
        ?? throw new InvalidOperationException("Database:ConnectionString no configurado.");

    public IDbConnection CreateConnection() => _provider switch
    {
        "PostgreSQL" => new NpgsqlConnection(_connectionString),
        "MySQL"      => new MySqlConnection(_connectionString),
        "SqlServer"  => new SqlConnection(_connectionString),
        _            => throw new NotSupportedException($"Proveedor '{_provider}' no soportado.")
    };
}
