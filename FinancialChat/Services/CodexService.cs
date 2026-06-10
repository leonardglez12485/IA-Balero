using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FinancialChat.Services;

public class CodexService : IAsyncDisposable
{
    private static readonly string[] DefaultModels = ["gpt-5.5", "gpt-5.4", "gpt-5.4-mini"];

    private readonly IConfiguration _config;
    private readonly ILogger<CodexService> _logger;
    private string _selectedModel;

    private Process? _codexProcess;
    private int _requestId = 1;
    private string? _currentThreadId;
    private bool _systemPromptSent = false;

    // FIX BUG #1: flag para no iniciar dos veces (varios usuarios en Singleton)
    private bool _isStarting = false;
    private bool _isStarted = false;

    public bool IsRunning => _codexProcess is { HasExited: false };
    public bool IsAuthed { get; private set; }
    public string AuthError { get; private set; } = string.Empty;
    public string McpStatus { get; private set; } = "No inicializado";
    public IReadOnlyList<string> AvailableModels { get; }
    public string CurrentModel => _selectedModel;

    public event Func<string, Task>? OnTokenReceived;
    public event Func<Task>? OnResponseComplete;
    public event Func<string, Task>? OnError;
    public event Func<Task>? OnReady;

    public CodexService(IConfiguration config, ILogger<CodexService> logger)
    {
        _config = config;
        _logger = logger;
        AvailableModels = LoadAvailableModels();
        _selectedModel = NormalizeModel(_config["Codex:Model"]);
    }

    // ─────────────────────────────────────────────────────────────
    // Arranque — llamado desde Chat.razor cuando ya hay suscriptores
    // ─────────────────────────────────────────────────────────────
    public async Task StartAsync()
    {
        // Si ya arrancó, disparar OnReady de nuevo para el nuevo suscriptor
        if (_isStarted && IsRunning)
        {
            if (OnReady is not null) await OnReady.Invoke();
            return;
        }

        // Evitar doble arranque concurrente
        if (_isStarting) return;
        _isStarting = true;

        try
        {
            if (!await CheckAuthAsync()) return;
            await LaunchAppServerAsync();
            _isStarted = true;
        }
        finally
        {
            _isStarting = false;
        }
    }

    // ─────────────────────────────────────────────────────────────
    // 1. Verificar auth — FIX BUG #2: usar stderr además de exit code
    // ─────────────────────────────────────────────────────────────
    private async Task<bool> CheckAuthAsync()
    {
        if (string.IsNullOrWhiteSpace(GetOpenAiApiKey()))
        {
            AuthError = "API key de OpenAI no configurada. Definila en FinancialChat/config.json.";
            _logger.LogWarning("OPENAI API key no configurada. Configurar Codex:ApiKey en FinancialChat/config.json.");
            if (OnError is not null) await OnError.Invoke(AuthError);
            return false;
        }

        AuthError = string.Empty;
        IsAuthed = true;
        _logger.LogInformation("Codex configurado con autenticación por API key y modelo {Model}", _selectedModel);
        return IsAuthed;
    }

    // ─────────────────────────────────────────────────────────────
    // 2. Lanzar codex app-server
    // ─────────────────────────────────────────────────────────────
    private async Task LaunchAppServerAsync()
    {
        var execPath = _config["Codex:ExecutablePath"] ?? "codex";

        var startInfo = new ProcessStartInfo
        {
            FileName = execPath,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
                StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                StandardOutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
        };

        ConfigureAppServerArguments(startInfo);
        ConfigureOpenAiAuthentication(startInfo);

        _codexProcess = new Process
        {
            StartInfo = startInfo
        };

        _codexProcess.Start();
        _logger.LogInformation("codex app-server iniciado PID={Pid}", _codexProcess.Id);

        _ = Task.Run(ReadStderrLoopAsync);
        _ = Task.Run(ReadOutputLoopAsync);

        await InitializeSessionAsync();
    }

    // ─────────────────────────────────────────────────────────────
    // 3. Handshake — FIX BUG #3: esperar respuesta real, no delay fijo
    // ─────────────────────────────────────────────────────────────
    private async Task InitializeSessionAsync()
    {
        var workingDir = Directory.GetCurrentDirectory();

        // initialize — esperamos respuesta real con timeout
        var initResp = await SendRpcAndWaitAsync("initialize", new
        {
            clientInfo = new { name = "FinancialChat", version = "1.0.0" },
            capabilities = (object?)null
        }, timeoutMs: 15_000);

        if (initResp is null)
        {
            _logger.LogError("Timeout esperando respuesta de 'initialize'");
            if (OnError is not null)
                await OnError.Invoke("Codex no respondió al initialize. Verificá que esté corriendo correctamente.");
            return;
        }

        _logger.LogInformation("Initialize OK: {R}", initResp.ToJsonString());

        await SendNotificationAsync("initialized");

        // thread/start — esperamos respuesta real
        var threadResp = await SendRpcAndWaitAsync("thread/start", new
        {
            cwd = workingDir,
            model = _selectedModel
        }, timeoutMs: 15_000);

        if (threadResp is not null)
        {
            _currentThreadId = threadResp["result"]?["thread"]?["id"]?.GetValue<string>();
            _logger.LogInformation("Thread creado: {Id}", _currentThreadId);
        }
        else
        {
            _logger.LogWarning("thread/start no devolvió respuesta en tiempo");
        }

        if (!await VerifyMcpServerAsync())
            return;

        if (OnReady is not null)
            await OnReady.Invoke();
    }

    // ─────────────────────────────────────────────────────────────
    // Enviar mensaje
    // ─────────────────────────────────────────────────────────────
    public async Task SendMessageAsync(string userMessage)
    {
        if (!IsRunning)
        {
            await StartAsync();
            // Esperar hasta que esté listo o timeout
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            while (!_isStarted && !cts.Token.IsCancellationRequested)
                await Task.Delay(200, cts.Token).ContinueWith(_ => { });
        }

        await SendRpcAsync("turn/start", new
        {
            threadId = _currentThreadId,
            input = new[]
            {
                new { type = "text", text = BuildUserInput(userMessage) }
            }
        });
    }

    public async Task ChangeModelAsync(string model)
    {
        var normalized = NormalizeModel(model);
        if (string.Equals(_selectedModel, normalized, StringComparison.Ordinal))
            return;

        _selectedModel = normalized;
        await RestartAsync();
    }

    private string BuildUserInput(string userMessage)
    {
        var sysPrompt = _config["Codex:SystemPrompt"];
        if (_systemPromptSent || string.IsNullOrWhiteSpace(sysPrompt))
            return userMessage;

        _systemPromptSent = true;
        return $"{sysPrompt}\n\nPregunta del usuario:\n{userMessage}";
    }

    // ─────────────────────────────────────────────────────────────
    // Loop de lectura stdout
    // ─────────────────────────────────────────────────────────────
    private async Task ReadOutputLoopAsync()
    {
        try
        {
            var stream = _codexProcess!.StandardOutput.BaseStream;
            var decoder = Encoding.UTF8.GetDecoder();
            var bytes = new byte[4096];
            var chars = new char[4096];
            var buffer = new StringBuilder();

            while (!_codexProcess.HasExited)
            {
                var read = await stream.ReadAsync(bytes);
                if (read == 0) break;

                var charCount = decoder.GetChars(bytes, 0, read, chars, 0);
                buffer.Append(chars, 0, charCount);

                foreach (var json in ExtractJsonMessages(buffer))
                {
                    _logger.LogDebug("← Codex: {Json}", json);
                    await ProcessRpcMessageAsync(json);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error leyendo stdout de Codex");
            if (OnError is not null)
                await OnError.Invoke("Se perdió la conexión con Codex. Recargá la página.");
        }
    }

    // ─────────────────────────────────────────────────────────────
    // Procesar mensajes JSON-RPC
    // ─────────────────────────────────────────────────────────────
    private async Task ProcessRpcMessageAsync(string jsonLine)
    {
        try
        {
            var node = JsonNode.Parse(jsonLine);
            if (node is null) return;

            var method = node["method"]?.GetValue<string>();
            var param = node["params"];

            // Resolver promises pendientes
            if (node["id"] is JsonNode idNode && (node["result"] is not null || node["error"] is not null))
            {
                if (int.TryParse(idNode.ToJsonString(), out var responseId)
                    && _pending.TryGetValue(responseId, out var pendingTcs))
                    pendingTcs.TrySetResult(node);
            }

            // Capturar threadId si viene en alguna respuesta
            var resultThreadId = node["result"]?["thread"]?["id"]?.GetValue<string>();
            if (resultThreadId is not null && _currentThreadId is null)
            {
                _currentThreadId = resultThreadId;
                _logger.LogInformation("ThreadId capturado: {Id}", _currentThreadId);
            }

            switch (method)
            {
                case "item/agentMessage/delta":
                    var delta = param?["delta"]?.GetValue<string>() ?? "";
                    if (!string.IsNullOrEmpty(delta) && OnTokenReceived is not null)
                        await OnTokenReceived.Invoke(delta);
                    break;

                case "turn/completed":
                    if (OnResponseComplete is not null)
                        await OnResponseComplete.Invoke();
                    break;

                case "item/completed":
                case "item/started":
                case "thread/started":
                case "thread/status/changed":
                case "thread/tokenUsage/updated":
                case "account/rateLimits/updated":
                case "mcpServer/startupStatus/updated":
                case "remoteControl/status/changed":
                    _logger.LogDebug("Evento Codex: {M}", method);
                    break;

                case "error":
                    var errMsg = node["error"]?["message"]?.GetValue<string>() ?? "Error desconocido";
                    _logger.LogWarning("Error Codex: {Msg}", errMsg);
                    if (OnError is not null) await OnError.Invoke(errMsg);
                    break;

                default:
                    _logger.LogDebug("Método no manejado: {M}", method ?? "(respuesta sin método)");
                    break;
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "JSON no parseable: {L}", jsonLine);
        }
    }

    // ─────────────────────────────────────────────────────────────
    // RPC helpers
    // ─────────────────────────────────────────────────────────────
    private readonly Dictionary<int, TaskCompletionSource<JsonNode?>> _pending = new();

    private void ConfigureAppServerArguments(ProcessStartInfo startInfo)
    {
        startInfo.ArgumentList.Add("app-server");
        startInfo.ArgumentList.Add("--listen");
        startInfo.ArgumentList.Add("stdio://");

        var mcpUrl = _config["Codex:McpServerUrl"];
        if (string.IsNullOrWhiteSpace(mcpUrl))
        {
            _logger.LogWarning("Codex:McpServerUrl no configurado. Codex arrancará sin MCP financiero inyectado por la app.");
            return;
        }

        var serverName = GetMcpServerName();
        AddConfigOverride(startInfo, $"mcp_servers.{serverName}.url={ToTomlString(mcpUrl)}");
        AddConfigOverride(startInfo, $"mcp_servers.{serverName}.enabled=true");
        AddConfigOverride(startInfo, $"mcp_servers.{serverName}.required=true");
        AddConfigOverride(startInfo, $"mcp_servers.{serverName}.default_tools_approval_mode=\"approve\"");
        AddConfigOverride(startInfo, $"model={ToTomlString(_selectedModel)}");
        AddConfigOverride(startInfo, "model_provider=\"openai\"");
        AddConfigOverride(startInfo, "forced_login_method=\"api\"");

        var token = _config["Codex:McpToken"];
        if (!string.IsNullOrWhiteSpace(token))
        {
            startInfo.Environment["FINANCIAL_MCP_TOKEN"] = token;
            AddConfigOverride(startInfo, $"mcp_servers.{serverName}.bearer_token_env_var=\"FINANCIAL_MCP_TOKEN\"");
        }
    }

    private static List<string> ExtractJsonMessages(StringBuilder buffer)
    {
        var messages = new List<string>();
        var start = -1;
        var depth = 0;
        var inString = false;
        var escape = false;

        for (var i = 0; i < buffer.Length; i++)
        {
            var c = buffer[i];

            if (start < 0)
            {
                if (char.IsWhiteSpace(c)) continue;
                if (c != '{')
                {
                    buffer.Remove(0, i + 1);
                    i = -1;
                    continue;
                }

                start = i;
                depth = 0;
                inString = false;
                escape = false;
            }

            if (inString)
            {
                if (escape)
                {
                    escape = false;
                }
                else if (c == '\\')
                {
                    escape = true;
                }
                else if (c == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (c == '"')
            {
                inString = true;
                continue;
            }

            if (c == '{') depth++;
            else if (c == '}') depth--;

            if (start >= 0 && depth == 0)
            {
                messages.Add(buffer.ToString(start, i - start + 1));
                buffer.Remove(0, i + 1);
                i = -1;
                start = -1;
            }
        }

        return messages;
    }

    private static void AddConfigOverride(ProcessStartInfo startInfo, string value)
    {
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add(value);
    }

    private string GetMcpServerName() => _config["Codex:McpServerName"] ?? "financial";

    private void ConfigureOpenAiAuthentication(ProcessStartInfo startInfo)
    {
        var apiKey = GetOpenAiApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
            return;

        startInfo.Environment["OPENAI_API_KEY"] = apiKey;
        startInfo.Environment["CODEX_API_KEY"] = apiKey;
    }

    private string? GetOpenAiApiKey()
    {
        var fromConfig = _config["Codex:ApiKey"];
        return string.IsNullOrWhiteSpace(fromConfig) ? null : fromConfig;
    }

    private IReadOnlyList<string> LoadAvailableModels()
    {
        var configured = _config.GetSection("Codex:AvailableModels").Get<string[]>();
        return configured is { Length: > 0 }
            ? configured.Where(model => !string.IsNullOrWhiteSpace(model)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
            : DefaultModels;
    }

    private string NormalizeModel(string? model)
    {
        if (string.IsNullOrWhiteSpace(model))
            return AvailableModels.FirstOrDefault() ?? DefaultModels[0];

        var trimmed = model.Trim();
        return AvailableModels.Contains(trimmed, StringComparer.OrdinalIgnoreCase)
            ? AvailableModels.First(m => string.Equals(m, trimmed, StringComparison.OrdinalIgnoreCase))
            : trimmed;
    }

    private static string ToTomlString(string value)
    {
        return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }

    private async Task<bool> VerifyMcpServerAsync()
    {
        var serverName = GetMcpServerName();
        var statusResp = await SendRpcAndWaitAsync("mcpServerStatus/list", new
        {
            threadId = _currentThreadId,
            detail = "full"
        }, timeoutMs: 20_000);

        if (statusResp is null)
        {
            McpStatus = "No se pudo verificar el MCP financiero.";
            _logger.LogWarning(McpStatus);
            if (OnError is not null) await OnError.Invoke(McpStatus);
            return false;
        }

        var json = statusResp.ToJsonString();
        _logger.LogInformation("Estado MCP: {Status}", json);

        if (!json.Contains(serverName, StringComparison.OrdinalIgnoreCase))
        {
            McpStatus = $"MCP '{serverName}' no aparece configurado en Codex.";
            _logger.LogWarning(McpStatus);
            if (OnError is not null) await OnError.Invoke(McpStatus);
            return false;
        }

        if (!json.Contains("ObtenerEsquemaBaseDatos", StringComparison.OrdinalIgnoreCase) ||
            !json.Contains("EjecutarConsultaSelect", StringComparison.OrdinalIgnoreCase))
        {
            McpStatus = $"MCP '{serverName}' conectado, pero no expone las tools financieras esperadas.";
            _logger.LogWarning(McpStatus);
            if (OnError is not null) await OnError.Invoke(McpStatus);
            return false;
        }

        McpStatus = $"MCP '{serverName}' conectado.";
        _logger.LogInformation(McpStatus);
        return true;
    }

    private async Task<JsonNode?> SendRpcAndWaitAsync(string method, object? @params = null, int timeoutMs = 10_000)
    {
        var id = _requestId++;
        var tcs = new TaskCompletionSource<JsonNode?>();
        _pending[id] = tcs;

        var msg = JsonSerializer.Serialize(new { method, id, @params });
        _logger.LogDebug("→ Codex: {Msg}", msg);
        await _codexProcess!.StandardInput.WriteLineAsync(msg);
        await _codexProcess.StandardInput.FlushAsync();

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(timeoutMs));
        _pending.Remove(id);

        return completed == tcs.Task ? await tcs.Task : null;
    }

    private async Task<int> SendRpcAsync(string method, object? @params = null)
    {
        var id = _requestId++;
        var msg = JsonSerializer.Serialize(new { method, id, @params });
        _logger.LogDebug("→ Codex: {Msg}", msg);
        await _codexProcess!.StandardInput.WriteLineAsync(msg);
        await _codexProcess.StandardInput.FlushAsync();
        return id;
    }

    private async Task SendNotificationAsync(string method, object? @params = null)
    {
        var msg = @params is null
            ? JsonSerializer.Serialize(new { method })
            : JsonSerializer.Serialize(new { method, @params });

        _logger.LogDebug("→ Codex notification: {Msg}", msg);
        await _codexProcess!.StandardInput.WriteLineAsync(msg);
        await _codexProcess.StandardInput.FlushAsync();
    }

    private async Task ReadStderrLoopAsync()
    {
        while (!_codexProcess!.StandardError.EndOfStream)
        {
            var line = await _codexProcess.StandardError.ReadLineAsync();
            if (!string.IsNullOrEmpty(line))
                _logger.LogDebug("[codex stderr] {L}", line);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopProcessAsync();
    }

    private async Task RestartAsync()
    {
        await StopProcessAsync();
        _requestId = 1;
        _currentThreadId = null;
        _systemPromptSent = false;
        _isStarted = false;
        _isStarting = false;
        McpStatus = "No inicializado";
        await StartAsync();
    }

    private async Task StopProcessAsync()
    {
        if (_codexProcess is { HasExited: false })
        {
            _codexProcess.Kill();
            await _codexProcess.WaitForExitAsync();
        }
        _codexProcess?.Dispose();
        _codexProcess = null;
    }
}
