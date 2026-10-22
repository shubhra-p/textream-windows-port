using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Textream.Windows.Common;
using Textream.Windows.Models;

namespace Textream.Windows.Services;

public record BrowserState(
    List<string> words,
    int highlightedCharCount,
    int totalCharCount,
    List<double> audioLevels,
    bool isListening,
    bool isDone,
    string fontColor,
    string cueColor,
    bool hasNextPage,
    bool isActive,
    bool highlightWords,
    string lastSpokenText);

public interface IBrowserServer : IDisposable
{
    bool IsRunning { get; }
    int ConnectedClients { get; }
    void Start();
    void Stop();
    void UpdateState(IReadOnlyList<string> words, int highlightedCharCount, IReadOnlyList<double> audioLevels, bool isListening, bool isDone, bool hasNextPage, string lastSpokenText);
}

public class BrowserServer : IBrowserServer
{
    private readonly ISettingsService _settingsService;
    private HttpListener? _httpListener;
    private HttpListener? _wsListener;
    private readonly List<WebSocket> _clients = new();
    private readonly object _clientsLock = new();
    private System.Threading.Timer? _broadcastTimer;

    private BrowserState _latestState;
    private bool _isRunning;

    public bool IsRunning => _isRunning;
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

    public BrowserServer(ISettingsService settingsService)
    {
        _settingsService = settingsService;
        _latestState = new BrowserState(
            new List<string>(), 0, 0, new List<double>(), false, false, "#FFFFFF", "#FFB340", false, false, true, "");
    }

    public void Start()
    {
        if (_isRunning) return;

        ushort httpPort = _settingsService.Settings.BrowserServerPort;
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

            _broadcastTimer = new System.Threading.Timer(OnBroadcastTick, null, 50, 50); // 20Hz
            _isRunning = true;
            AppLogger.Info($"BrowserServer listening on HTTP {httpPort} and WS {wsPort}");
        }
        catch (Exception)
        {
            // If binding to * requires admin on certain Windows configs, fall back to localhost
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

                _broadcastTimer = new System.Threading.Timer(OnBroadcastTick, null, 50, 50);
                _isRunning = true;
                AppLogger.Info($"BrowserServer listening on localhost: HTTP {httpPort} and WS {wsPort}");
            }
            catch (Exception ex2)
            {
                AppLogger.Error("Failed to start BrowserServer", ex2);
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
        bool hasNextPage,
        string lastSpokenText)
    {
        var settings = _settingsService.Settings;
        var wordsList = new List<string>(words);
        int totalChars = wordsList.Sum(w => w.Length + 1);

        _latestState = new BrowserState(
            wordsList,
            highlightedCharCount,
            totalChars,
            new List<double>(audioLevels),
            isListening,
            isDone,
            settings.FontColor,
            settings.CueColor,
            hasNextPage,
            true,
            settings.HighlightWords,
            lastSpokenText);
    }

    private void OnHttpRequest(IAsyncResult ar)
    {
        if (_httpListener == null || !_httpListener.IsListening) return;

        try
        {
            var ctx = _httpListener.EndGetContext(ar);
            _httpListener.BeginGetContext(OnHttpRequest, _httpListener);

            ushort wsPort = (ushort)(_settingsService.Settings.BrowserServerPort + 1);
            string html = GetClientHtml(wsPort);
            byte[] bytes = Encoding.UTF8.GetBytes(html);

            ctx.Response.ContentType = "text/html; charset=utf-8";
            ctx.Response.ContentLength64 = bytes.Length;
            ctx.Response.Headers.Add("Cache-Control", "no-cache");
            ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
            ctx.Response.OutputStream.Close();
        }
        catch (Exception ex)
        {
            AppLogger.Debug($"BrowserServer HTTP error: {ex.Message}");
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

                AppLogger.Info($"BrowserServer WebSocket client connected ({_clients.Count} total).");
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
            AppLogger.Debug($"BrowserServer WS error: {ex.Message}");
        }
    }

    private async Task ListenWebSocket(WebSocket socket)
    {
        var buffer = new byte[1024];
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
            }
            socket.Dispose();
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
                catch
                {
                    // Ignored, cleaned up on next read
                }
            }
        }
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
        }

        try { _httpListener?.Stop(); _httpListener?.Close(); } catch { }
        try { _wsListener?.Stop(); _wsListener?.Close(); } catch { }

        _httpListener = null;
        _wsListener = null;
        AppLogger.Info("BrowserServer stopped.");
    }

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }

    private static string GetClientHtml(ushort wsPort)
    {
        return $$"""
<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8"/>
<meta name="viewport" content="width=device-width, initial-scale=1.0"/>
<title>Textream Web Teleprompter</title>
<style>
  * { box-sizing: border-box; margin: 0; padding: 0; }
  body {
    background-color: #0b0b0e;
    color: #ffffff;
    font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif;
    overflow: hidden;
    height: 100vh;
    display: flex;
    flex-direction: column;
  }
  #header {
    height: 48px;
    background: rgba(20, 20, 24, 0.8);
    border-bottom: 1px solid rgba(255, 255, 255, 0.08);
    display: flex;
    align-items: center;
    justify-content: space-between;
    padding: 0 20px;
    font-size: 13px;
    font-weight: 600;
  }
  #status { color: #4cd964; }
  #container {
    flex: 1;
    overflow: hidden;
    position: relative;
    padding: 40px 60px;
    display: flex;
    justify-content: center;
  }
  #words {
    max-width: 900px;
    display: flex;
    flex-wrap: wrap;
    align-content: flex-start;
    gap: 8px 10px;
    font-size: 32px;
    font-weight: 600;
    line-height: 1.5;
    transition: transform 0.15s ease-out;
  }
  .word {
    padding: 2px 6px;
    border-radius: 6px;
    transition: all 0.1s ease;
    opacity: 0.85;
  }
  .word.active {
    background: rgba(10, 132, 255, 0.4);
    opacity: 1;
    color: #ffffff;
    box-shadow: 0 0 12px rgba(10, 132, 255, 0.5);
  }
  .word.read {
    opacity: 0.35;
  }
  .word.cue {
    color: #ffb340;
    opacity: 0.5;
  }
  #visualizer {
    height: 16px;
    background: rgba(0,0,0,0.4);
    display: flex;
    align-items: flex-end;
    gap: 3px;
    padding: 0 20px 4px 20px;
  }
  .bar {
    flex: 1;
    background: #0a84ff;
    border-radius: 2px;
    min-height: 2px;
  }
</style>
</head>
<body>
<div id="header">
  <div>TEXTREAM REMOTE PROMPTER</div>
  <div id="status">CONNECTING...</div>
</div>
<div id="container">
  <div id="words">Waiting for prompter session...</div>
</div>
<div id="visualizer"></div>

<script>
  const wsHost = window.location.hostname || 'localhost';
  const wsUrl = `ws://${wsHost}:{{wsPort}}/`;
  const statusEl = document.getElementById('status');
  const wordsEl = document.getElementById('words');
  const visEl = document.getElementById('visualizer');

  // Build 30 visualizer bars
  for(let i=0; i<30; i++) {
    const b = document.createElement('div');
    b.className = 'bar';
    visEl.appendChild(b);
  }
  const bars = document.querySelectorAll('.bar');

  let socket;
  function connect() {
    socket = new WebSocket(wsUrl);
    socket.onopen = () => {
      statusEl.textContent = 'LIVE';
      statusEl.style.color = '#4cd964';
    };
    socket.onclose = () => {
      statusEl.textContent = 'DISCONNECTED';
      statusEl.style.color = '#ff453a';
      setTimeout(connect, 2000);
    };
    socket.onmessage = (event) => {
      const state = JSON.parse(event.data);
      render(state);
    };
  }

  let lastWordCount = 0;
  function render(state) {
    if (!state.isActive || !state.words || state.words.length === 0) {
      wordsEl.innerHTML = '<div style="opacity:0.4;">Prompter is currently idle</div>';
      return;
    }

    if (state.words.length !== lastWordCount) {
      wordsEl.innerHTML = '';
      state.words.forEach((w, i) => {
        const span = document.createElement('span');
        span.id = 'w_' + i;
        span.className = 'word' + (w.startsWith('[') && w.endsWith(']') ? ' cue' : '');
        span.textContent = w;
        wordsEl.appendChild(span);
      });
      lastWordCount = state.words.length;
    }

    // Determine active word index from char count
    let charAcc = 0;
    let activeIdx = 0;
    for(let i=0; i<state.words.length; i++) {
      if (charAcc >= state.highlightedCharCount) {
        activeIdx = i;
        break;
      }
      charAcc += state.words[i].length + 1;
      activeIdx = i;
    }

    state.words.forEach((w, i) => {
      const el = document.getElementById('w_' + i);
      if (!el) return;
      if (i < activeIdx) {
        el.className = 'word read' + (w.startsWith('[') ? ' cue' : '');
      } else if (i === activeIdx) {
        el.className = 'word active';
      } else {
        el.className = 'word' + (w.startsWith('[') ? ' cue' : '');
      }
    });

    // Auto-scroll
    const activeEl = document.getElementById('w_' + activeIdx);
    if (activeEl) {
      const containerRect = wordsEl.parentElement.getBoundingClientRect();
      const elRect = activeEl.getBoundingClientRect();
      const offset = elRect.top - containerRect.top - (containerRect.height / 3);
      if (Math.abs(offset) > 10) {
        wordsEl.style.transform = `translateY(${-Math.max(0, activeEl.offsetTop - 120)}px)`;
      }
    }

    // Audio levels
    if (state.audioLevels) {
      state.audioLevels.forEach((lvl, i) => {
        if (bars[i]) {
          bars[i].style.height = `${Math.max(2, lvl * 14)}px`;
        }
      });
    }
  }

  connect();
</script>
</body>
</html>
""";
    }
}
