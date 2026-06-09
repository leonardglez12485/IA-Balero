# Financial Solution — .NET 9

Chat financiero con IA que conecta **Blazor Server** → **Codex CLI** → **MCP Server .NET** → **Base de datos**.

---

## Estructura

```
FinancialSolution/
├── FinancialSolution.sln         ← Abrí esto en Visual Studio
├── FinancialChat/                ← Frontend Blazor Server
│   ├── Components/Pages/Chat.razor
│   ├── Services/CodexService.cs  ← Habla con codex app-server
│   └── appsettings.json          ← Configurar URL y token MCP
└── FinancialMcpServer/           ← Middleware protege credenciales BD
    ├── Tools/FinancialTools.cs   ← Herramientas financieras para Codex
    └── appsettings.json          ← Configurar BD y tokens por cliente
```

---

## Setup rápido (primera vez)

### Requisitos
- .NET 9 SDK
- Node.js
- Codex CLI: `npm install -g @openai/codex`
- Cuenta ChatGPT Plus / Pro / Business

### Pasos

```bash
# 1. Login con ChatGPT (una sola vez en el servidor)
codex login

# 2. Verificar autenticación
codex login status   # debe decir: Authenticated via ChatGPT

# 3. Configurar el MCP Server
# Editar FinancialMcpServer/appsettings.json:
# - Database.ConnectionString → tu base de datos
# - McpSecurity.ValidTokens  → tokens por cliente

# 4. Levantar el MCP Server (terminal 1)
cd FinancialMcpServer
dotnet run

# 5. Configurar el Frontend
# Editar FinancialChat/appsettings.json:
# - Codex.McpServerUrl → URL donde corre el MCP Server
# - Codex.McpToken     → token del cliente (mismo que en paso 3)

# 6. Levantar el Frontend (terminal 2)
cd FinancialChat
dotnet run

# 7. Abrir en el browser
# https://localhost:5001
```

---

## Flujo de una consulta

```
Usuario escribe pregunta
    ↓ SignalR (Blazor Server)
CodexService.SendMessageAsync()
    ↓ stdin JSON-RPC
codex app-server
    ↓ MCP tool call (automático)
FinancialMcpServer /mcp
    ↓ SQL readonly
Base de datos del cliente
    ↑ datos JSON
FinancialMcpServer
    ↑ resultado al tool
codex app-server — genera respuesta
    ↑ stdout streaming tokens
CodexService — eventos OnTokenReceived
    ↑ SignalR push
Chat.razor — muestra token a token en UI
```

---

## Configuración de producción

### FinancialMcpServer — `appsettings.json`
```json
{
  "Database": {
    "Provider": "PostgreSQL",
    "ConnectionString": "Host=...;Database=...;Username=readonly_user;Password=..."
  },
  "McpSecurity": {
    "ValidTokens": [ "token-cliente-abc" ]
  }
}
```

### FinancialChat — `appsettings.json`
```json
{
  "Codex": {
    "ExecutablePath": "codex",
    "McpServerUrl":   "https://tu-dominio.com/mcp",
    "McpToken":       "token-cliente-abc"
  }
}
```

---

## Deploy en IIS (Windows Server)

```bash
# MCP Server
cd FinancialMcpServer
dotnet publish -c Release -o ./publish
# Crear site IIS → puerto 8080 (o el que elijas)

# Frontend
cd FinancialChat
dotnet publish -c Release -o ./publish
# Crear site IIS → puerto 443 con HTTPS
```
