# Financial MCP Server — .NET 9

Middleware entre **OpenAI Codex** y la base de datos del cliente.
Las credenciales de la BD viven solo en este servidor. El cliente solo recibe una URL y un token.

---

## Estructura

```
FinancialMcpServer/
├── Program.cs                  ← Entry point, configura MCP + middleware
├── appsettings.json            ← Credenciales BD y tokens (NUNCA compartir)
├── FinancialMcpServer.csproj
├── Data/
│   └── DatabaseFactory.cs     ← Conexión a PostgreSQL / MySQL / SQL Server
├── Middleware/
│   └── TokenAuthMiddleware.cs ← Valida Bearer token en cada request
└── Tools/
    └── FinancialTools.cs      ← Herramientas expuestas a Codex via MCP
```

---

## Setup rápido

### 1. Configurar credenciales (solo en el servidor)

Editar `appsettings.json`:

```json
{
  "Database": {
    "Provider": "PostgreSQL",        // o "MySQL" o "SqlServer"
    "ConnectionString": "Host=...;Database=...;Username=readonly_user;Password=..."
  },
  "McpSecurity": {
    "ValidTokens": [
      "token-cliente-acme-abc123",   // un token por cliente
      "token-cliente-beta-xyz789"
    ]
  }
}
```

> **Importante:** usar un usuario de BD con permisos **solo lectura** (SELECT).
>
> En PostgreSQL:
> ```sql
> CREATE USER readonly_user WITH PASSWORD 'secreto';
> GRANT CONNECT ON DATABASE financiero TO readonly_user;
> GRANT USAGE ON SCHEMA public TO readonly_user;
> GRANT SELECT ON ALL TABLES IN SCHEMA public TO readonly_user;
> ```

### 2. Levantar el servidor

```bash
dotnet run
# Escucha en https://localhost:5001 (o el puerto configurado)
```

### 3. Deploy en producción

**IIS / Windows Server** (encaja perfecto en stack .NET):
```bash
dotnet publish -c Release -o ./publish
# Copiar ./publish al servidor IIS
# Configurar site en IIS apuntando al publish/
```

**Docker:**
```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY publish/ .
ENTRYPOINT ["dotnet", "FinancialMcpServer.dll"]
```

---

## Configuración en Codex (lo que recibe el cliente)

El cliente pone esto en `~/.codex/config.json` — **sin credenciales de BD**:

```json
{
  "mcpServers": {
    "financiero": {
      "type": "streamable-http",
      "url": "https://tu-dominio.com/mcp",
      "headers": {
        "Authorization": "Bearer token-cliente-acme-abc123"
      }
    }
  }
}
```

---

## Herramientas disponibles para Codex

| Herramienta | Cuándo la usa Codex |
|---|---|
| `ObtenerEsquemaBaseDatos` | Antes de generar cualquier SQL |
| `ConsultarDatosFinancieros` | Preguntas ad-hoc del usuario |
| `ObtenerKpisFinancieros` | "Dame el resumen del Q1 2024" |
| `CompararPeriodos` | "Compará Q1 vs Q2" |

---

## Adaptar las queries al cliente

Las queries en `FinancialTools.cs` asumen una tabla `transacciones` con columnas
`fecha`, `tipo` (ingreso/gasto) y `monto`. Ajustar a las tablas reales del cliente.

Codex descubrirá el esquema real con `ObtenerEsquemaBaseDatos()` y generará
el SQL correcto automáticamente para el resto de las consultas.

---

## Seguridad

- Credenciales de BD nunca salen del servidor
- Solo queries `SELECT` — bloquea DROP, DELETE, UPDATE, etc.
- Token por cliente — revocar uno sin afectar al resto
- Logs de cada consulta para auditoría
