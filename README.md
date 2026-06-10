# Financial Solution - .NET 9

Chat financiero con IA que conecta **Blazor Server** -> **Codex CLI** -> **MCP Server .NET** -> **Base de datos**.

## Estructura

```text
FinancialSolution/
├── FinancialSolution.sln
├── FinancialChat/
│   ├── Components/Pages/Chat.razor
│   ├── Services/CodexService.cs
│   └── appsettings.json
└── FinancialMcpServer/
    ├── Tools/FinancialTools.cs
    └── appsettings.json
```

## Setup rápido

Requisitos:
- .NET 9 SDK
- Node.js
- Codex CLI: `npm install -g @openai/codex`
- API key de OpenAI configurada en `FinancialChat/config.json`

```json
{
  "Codex": {
    "ApiKey": "sk-..."
  }
}
```

Configurar `FinancialChat/appsettings.json`:

```json
"Codex": {
  "ExecutablePath": "C:\\Users\\Leonardo\\AppData\\Roaming\\npm\\codex.cmd",
  "CodexHome": ".codex-backend",
  "Model": "gpt-5.5",
  "AvailableModels": [ "gpt-5.5", "gpt-5.4", "gpt-5.4-mini" ],
  "McpServerName": "financial",
  "McpServerUrl": "http://localhost:54751/mcp",
  "McpToken": "token-cliente"
}
```

Levantar:

```powershell
cd FinancialMcpServer
dotnet run

cd ..\FinancialChat
dotnet run
```

## Flujo

```text
Usuario
  -> FinancialChat Blazor
  -> CodexService
  -> codex login --with-api-key en CODEX_HOME aislado
  -> codex app-server con modelo seleccionado
  -> FinancialMcpServer /mcp
  -> Base de datos readonly
  -> Respuesta streaming al front
```
