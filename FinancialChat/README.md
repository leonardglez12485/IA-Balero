# FinancialChat — Blazor Server Frontend

Chat financiero con IA que usa **Codex app-server** como motor y se conecta
al **FinancialMcpServer** (middleware .NET) para consultar la base de datos del cliente.

---

## Arquitectura completa

```
Browser (Blazor Server via SignalR)
    ↕ WebSocket tiempo real
ASP.NET Core + Blazor Server  ←── este proyecto
    ↕ stdin/stdout JSON-RPC
codex app-server
    ↕ MCP Streamable HTTP + Bearer token
FinancialMcpServer (.NET)     ←── proyecto hermano
    ↕ SQL readonly
Base de datos del cliente (PostgreSQL / MySQL / SQL Server)
```

---

## Requisitos previos

- .NET 9 SDK
- Node.js (para instalar Codex CLI)
- Codex CLI instalado: `npm install -g @openai/codex`
- API key configurada en `FinancialChat/config.json`
- **FinancialMcpServer** corriendo (ver su README)

---

## Configuración

Editar `appsettings.json`:

```json
{
  "Codex": {
    "ExecutablePath": "codex",
    "McpServerUrl":  "https://tu-dominio.com/mcp",
    "McpToken":      "token-del-cliente",
    "SystemPrompt":  "Eres un analista financiero experto..."
  }
}
```

| Campo | Descripción |
|---|---|
| `ExecutablePath` | Path al ejecutable de Codex (si está en PATH, `"codex"` alcanza) |
| `McpServerUrl` | URL del FinancialMcpServer desplegado |
| `McpToken` | Token Bearer del cliente (definido en FinancialMcpServer) |
| `SystemPrompt` | Instrucciones de comportamiento para Codex |

---

## Levantar en desarrollo

```bash
cd FinancialChat
dotnet run
# Abre https://localhost:5001
```

---

## Deploy en producción (IIS / Windows Server)

```bash
dotnet publish -c Release -o ./publish

# Copiar ./publish al servidor
# Crear site en IIS apuntando a publish/
# Asegurarse de que el Application Pool sea .NET CLR Version: No Managed Code
# y que el usuario del pool tenga acceso a ejecutar "codex"
```

---

## Estructura del proyecto

```
FinancialChat/
├── Components/
│   ├── App.razor              ← HTML shell
│   ├── Routes.razor           ← Router
│   ├── _Imports.razor         ← Usings globales
│   ├── Layout/
│   │   └── MainLayout.razor   ← Layout base
│   └── Pages/
│       └── Chat.razor         ← UI del chat (toda la lógica de UI)
├── Models/
│   └── ChatMessage.cs         ← Modelo de mensajes
├── Services/
│   └── CodexService.cs        ← Comunicación con codex app-server
├── wwwroot/
│   ├── app.css                ← Estilos del chat
│   └── chat.js                ← JS interop (scroll, resize)
├── Properties/
│   └── launchSettings.json
├── Program.cs                 ← Entry point
└── appsettings.json           ← Configuración (credenciales)
```

---

## Cómo funciona el streaming

1. El usuario escribe y apreta Enter
2. `Chat.razor` llama a `CodexService.SendMessageAsync()`
3. `CodexService` manda el mensaje via JSON-RPC a `codex app-server` (stdin)
4. Codex procesa, consulta el MCP server (que va a la BD), y empieza a streamear tokens
5. Cada token llega por stdout como `thread/message/delta`
6. `CodexService` dispara el evento `OnTokenReceived`
7. `Chat.razor` lo escucha, agrega el token al mensaje en pantalla, y llama `StateHasChanged()`
8. SignalR empuja el cambio al browser en tiempo real — sin recargas
