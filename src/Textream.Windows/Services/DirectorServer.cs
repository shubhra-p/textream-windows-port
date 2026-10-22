using System.Net;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Textream.Windows.Common;
using Textream.Windows.Models;

namespace Textream.Windows.Services;

public record DirectorState(
    List<string> words,
    int highlightedCharCount,
    int totalCharCount,
    bool isActive,
    bool isDone,
    bool isListening,
    string fontColor,
    string cueColor,
    string lastSpokenText,
    List<double> audioLevels);

public record DirectorCommand(
    string type,
    string? text,
    int? readCharCount,
    string? token);

public interface IDirectorServer : IDisposable
{
    bool IsRunning { get; }
    string AuthToken { get; }
    int ConnectedClients { get; }
    event Action<string>? SetTextRequested;
    event Action<string, int>? UpdateTextRequested;
    event Action? StopRequested;

    void Start();
    void Stop();
    void UpdateState(IReadOnlyList<string> words, int highlightedCharCount, IReadOnlyList<double> audioLevels, bool isListening, bool isDone, string lastSpokenText);
}

public class DirectorServer : IDirectorServer
{
    private readonly ISettingsService _settingsService;
    private HttpListener? _httpListener;
    private HttpListener? _wsListener;
    private readonly List<WebSocket> _clients = new();
    private readonly HashSet<WebSocket> _authenticatedClients = new();
    private readonly object _clientsLock = new();
    private System.Threading.Timer? _broadcastTimer;

    private string _authToken = string.Empty;
    private DirectorState _latestState;
    private bool _isRunning;

    public bool IsRunning => _isRunning;
    public string AuthToken => _authToken;
    public int ConnectedClients
    {
        get
        {
            lock (_clientsLock)
            {
                return _clients.Count;
            }
        }
    }

    public event Action<string>? SetTextRequested;
    public event Action<string, int>? UpdateTextRequested;
    public event Action? StopRequested;

    public DirectorServer(ISettingsService settingsService)
    {
        _settingsService = settingsService;
        _latestState = new DirectorState(
            new List<string>(), 0, 0, false, false, false, "#FFFFFF", "#FFB340", "", new List<double>());
    }

    public void Start()
    {
        if (_isRunning) return;

        _authToken = GenerateToken();
        ushort httpPort = _settingsService.Settings.DirectorServerPort;
        ushort wsPort = (ushort)(httpPort + 1);

        try
        {
            _httpListener = new HttpListener();
            _httpListener.Prefixes.Add($"http://*:{httpPort}/");
            _httpListener.Start();
            _httpListener.BeginGetContext(OnHttpRequest, _httpListener);

            _wsListener = new HttpListener();
            _wsListener.Prefixes.Add($"http://*:{wsPort}/");
            _wsListener.Start();
            _wsListener.BeginGetContext(OnWsRequest, _wsListener);

            _broadcastTimer = new System.Threading.Timer(OnBroadcastTick, null, 100, 100);
            _isRunning = true;
            AppLogger.Info($"DirectorServer started on HTTP {httpPort} and WS {wsPort} with token prefix {_authToken[..8]}...");
        }
        catch (Exception)
        {
            try
            {
                _httpListener?.Close();
                _wsListener?.Close();

                _httpListener = new HttpListener();
                _httpListener.Prefixes.Add($"http://localhost:{httpPort}/");
                _httpListener.Start();
                _httpListener.BeginGetContext(OnHttpRequest, _httpListener);

                _wsListener = new HttpListener();
                _wsListener.Prefixes.Add($"http://localhost:{wsPort}/");
                _wsListener.Start();
                _wsListener.BeginGetContext(OnWsRequest, _wsListener);

                _broadcastTimer = new System.Threading.Timer(OnBroadcastTick, null, 100, 100);
                _isRunning = true;
                AppLogger.Info($"DirectorServer started on localhost: HTTP {httpPort} and WS {wsPort}");
            }
            catch (Exception ex2)
            {
                AppLogger.Error("Failed to start DirectorServer", ex2);
                Stop();
            }
        }
    }

    public void UpdateState(
        IReadOnlyList<string> words,
        int highlightedCharCount,
        IReadOnlyList<double> audioLevels,
        bool isListening,
        bool isDone,
        string lastSpokenText)
    {
        var settings = _settingsService.Settings;
        var wordsList = new List<string>(words);
        int totalChars = wordsList.Sum(w => w.Length + 1);

        _latestState = new DirectorState(
            wordsList,
            highlightedCharCount,
            totalChars,
            true,
            isDone,
            isListening,
            settings.FontColor,
            settings.CueColor,
            lastSpokenText,
            new List<double>(audioLevels));
    }

    private void OnHttpRequest(IAsyncResult ar)
    {
        if (_httpListener == null || !_httpListener.IsListening) return;

        try
        {
            var ctx = _httpListener.EndGetContext(ar);
            _httpListener.BeginGetContext(OnHttpRequest, _httpListener);

            ushort wsPort = (ushort)(_settingsService.Settings.DirectorServerPort + 1);
            string html = GetDirectorHtml(wsPort, _authToken);
            byte[] bytes = Encoding.UTF8.GetBytes(html);

            ctx.Response.ContentType = "text/html; charset=utf-8";
            ctx.Response.ContentLength64 = bytes.Length;
            ctx.Response.Headers.Add("Cache-Control", "no-store");
            ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
            ctx.Response.OutputStream.Close();
        }
        catch (Exception ex)
        {
            AppLogger.Debug($"DirectorServer HTTP error: {ex.Message}");
        }
    }

    private async void OnWsRequest(IAsyncResult ar)
    {
        if (_wsListener == null || !_wsListener.IsListening) return;

        try
        {
            var ctx = _wsListener.EndGetContext(ar);
            _wsListener.BeginGetContext(OnWsRequest, _wsListener);

            if (ctx.Request.IsWebSocketRequest)
            {
                var wsContext = await ctx.AcceptWebSocketAsync(null);
                var socket = wsContext.WebSocket;

                lock (_clientsLock)
                {
                    _clients.Add(socket);
                }

                AppLogger.Info($"DirectorServer WebSocket connection established ({_clients.Count} total).");
                await ListenWebSocket(socket);
            }
            else
            {
                ctx.Response.StatusCode = 400;
                ctx.Response.Close();
            }
        }
        catch (Exception ex)
        {
            AppLogger.Debug($"DirectorServer WS error: {ex.Message}");
        }
    }

    private async Task ListenWebSocket(WebSocket socket)
    {
        var buffer = new byte[8192];
        try
        {
            while (socket.State == WebSocketState.Open)
            {
                var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                    break;
                }

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    string message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    HandleClientMessage(socket, message);
                }
            }
        }
        catch
        {
            // Client disconnected
        }
        finally
        {
            lock (_clientsLock)
            {
                _clients.Remove(socket);
                _authenticatedClients.Remove(socket);
            }
            socket.Dispose();
        }
    }

    private void HandleClientMessage(WebSocket socket, string json)
    {
        try
        {
            var cmd = JsonSerializer.Deserialize<DirectorCommand>(json);
            if (cmd == null) return;

            // Auth check
            if (cmd.type == "auth")
            {
                if (cmd.token == _authToken)
                {
                    lock (_clientsLock)
                    {
                        _authenticatedClients.Add(socket);
                    }
                    AppLogger.Info("Director client authenticated successfully.");
                }
                return;
            }

            bool isAuthed;
            lock (_clientsLock)
            {
                isAuthed = _authenticatedClients.Contains(socket) || cmd.token == _authToken;
            }

            if (!isAuthed)
            {
                AppLogger.Warn("Unauthenticated director command rejected.");
                return;
            }

            switch (cmd.type)
            {
                case "setText":
                    if (!string.IsNullOrEmpty(cmd.text))
                    {
                        SetTextRequested?.Invoke(cmd.text);
                    }
                    break;
                case "updateText":
                    if (!string.IsNullOrEmpty(cmd.text))
                    {
                        UpdateTextRequested?.Invoke(cmd.text, cmd.readCharCount ?? 0);
                    }
                    break;
                case "stop":
                    StopRequested?.Invoke();
                    break;
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error("Error processing director command", ex);
        }
    }

    private void OnBroadcastTick(object? state)
    {
        List<WebSocket> activeClients;
        lock (_clientsLock)
        {
            if (_clients.Count == 0) return;
            activeClients = new List<WebSocket>(_clients);
        }

        string json = JsonSerializer.Serialize(_latestState);
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        var segment = new ArraySegment<byte>(bytes);

        foreach (var client in activeClients)
        {
            if (client.State == WebSocketState.Open)
            {
                try
                {
                    _ = client.SendAsync(segment, WebSocketMessageType.Text, true, CancellationToken.None);
                }
                catch { }
            }
        }
    }

    private static string GenerateToken()
    {
        byte[] bytes = new byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public void Stop()
    {
        _isRunning = false;
        _broadcastTimer?.Dispose();
        _broadcastTimer = null;

        lock (_clientsLock)
        {
            foreach (var client in _clients)
            {
                try { client.Dispose(); } catch { }
            }
            _clients.Clear();
            _authenticatedClients.Clear();
        }

        try { _httpListener?.Stop(); _httpListener?.Close(); } catch { }
        try { _wsListener?.Stop(); _wsListener?.Close(); } catch { }

        _httpListener = null;
        _wsListener = null;
        AppLogger.Info("DirectorServer stopped.");
    }

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }

    private static string GetDirectorHtml(ushort wsPort, string token)
    {
        return $$"""
<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8"/>
<meta name="viewport" content="width=device-width, initial-scale=1.0"/>
<title>Textream Director Console</title>
<style>
  * { box-sizing: border-box; margin: 0; padding: 0; }
  body {
    background-color: #121216;
    color: #ffffff;
    font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif;
    padding: 24px;
  }
  header {
    display: flex;
    justify-content: space-between;
    align-items: center;
    border-bottom: 1px solid rgba(255,255,255,0.1);
    padding-bottom: 16px;
    margin-bottom: 20px;
  }
  h1 { font-size: 20px; font-weight: 700; }
  .badge {
    background: #0a84ff;
    padding: 4px 10px;
    border-radius: 12px;
    font-size: 12px;
    font-weight: 600;
  }
  .grid {
    display: grid;
    grid-template-columns: 1fr 1fr;
    gap: 20px;
  }
  .card {
    background: #1c1c22;
    border: 1px solid rgba(255,255,255,0.08);
    border-radius: 12px;
    padding: 16px;
  }
  textarea {
    width: 100%;
    height: 320px;
    background: #0f0f13;
    border: 1px solid rgba(255,255,255,0.12);
    border-radius: 8px;
    color: #fff;
    padding: 12px;
    font-size: 15px;
    line-height: 1.5;
    font-family: inherit;
    resize: none;
  }
  .btn-row {
    margin-top: 12px;
    display: flex;
    gap: 10px;
  }
  button {
    background: #0a84ff;
    color: white;
    border: none;
    border-radius: 6px;
    padding: 10px 16px;
    font-weight: 600;
    cursor: pointer;
  }
  button.danger { background: #ff453a; }
  #liveScript {
    height: 320px;
    overflow-y: auto;
    background: #0f0f13;
    border: 1px solid rgba(255,255,255,0.08);
    border-radius: 8px;
    padding: 12px;
    font-size: 16px;
    line-height: 1.6;
  }
</style>
</head>
<body>
<header>
  <h1>TEXTREAM DIRECTOR CONSOLE</h1>
  <div class="badge" id="connStatus">CONNECTING...</div>
</header>
<div class="grid">
  <div class="card">
    <h3 style="margin-bottom:12px; font-size:14px; color:#aaa;">LIVE TELEPROMPTER STATUS</h3>
    <div id="liveScript">Connecting to prompter...</div>
    <div class="btn-row">
      <button class="danger" onclick="sendStop()">STOP PROMPTER</button>
    </div>
  </div>
  <div class="card">
    <h3 style="margin-bottom:12px; font-size:14px; color:#aaa;">EDIT SCRIPT REMOTELY</h3>
    <textarea id="editor" placeholder="Type new script or notes..."></textarea>
    <div class="btn-row">
      <button onclick="sendUpdateText()">UPDATE (PRESERVE PROGRESS)</button>
      <button style="background:#30d158;" onclick="sendSetText()">REPLACE TEXT</button>
    </div>
  </div>
</div>

<script>
  const wsHost = window.location.hostname || 'localhost';
  const token = '{{token}}';
  const wsUrl = `ws://${wsHost}:{{wsPort}}/`;
  let socket;
  let lastHighlightedCharCount = 0;

  function connect() {
    socket = new WebSocket(wsUrl);
    socket.onopen = () => {
      document.getElementById('connStatus').textContent = 'CONNECTED';
      document.getElementById('connStatus').style.background = '#30d158';
      socket.send(JSON.stringify({ type: 'auth', token: token }));
    };
    socket.onclose = () => {
      document.getElementById('connStatus').textContent = 'DISCONNECTED';
      document.getElementById('connStatus').style.background = '#ff453a';
      setTimeout(connect, 2000);
    };
    socket.onmessage = (e) => {
      const state = JSON.parse(e.data);
      lastHighlightedCharCount = state.highlightedCharCount;
      const container = document.getElementById('liveScript');
      if (state.words && state.words.length > 0) {
        container.innerHTML = state.words.join(' ');
      }
    };
  }

  function sendSetText() {
    const text = document.getElementById('editor').value;
    if (socket && socket.readyState === WebSocket.OPEN) {
      socket.send(JSON.stringify({ type: 'setText', text: text, token: token }));
    }
  }

  function sendUpdateText() {
    const text = document.getElementById('editor').value;
    if (socket && socket.readyState === WebSocket.OPEN) {
      socket.send(JSON.stringify({
        type: 'updateText',
        text: text,
        readCharCount: lastHighlightedCharCount,
        token: token
      }));
    }
  }

  function sendStop() {
    if (socket && socket.readyState === WebSocket.OPEN) {
      socket.send(JSON.stringify({ type: 'stop', token: token }));
    }
  }

  connect();
</script>
</body>
</html>
""";
    }
}
