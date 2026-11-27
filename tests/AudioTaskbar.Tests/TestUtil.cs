using System.Net;
using System.Net.Sockets;
using System.Text;

namespace AudioTaskbar.Tests;

/// <summary>Tests touching process-wide statics (SceneClock.HourOverride, ConfigStore.LastLoadError) run serially.</summary>
[CollectionDefinition("Statics", DisableParallelization = true)]
public sealed class StaticsCollection { }

internal sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "at-tests-" + Guid.NewGuid().ToString("N"));
    public TempDir() => Directory.CreateDirectory(Path);
    public string File(string name) => System.IO.Path.Combine(Path, name);
    public void Dispose() { try { Directory.Delete(Path, true); } catch { } }
}

/// <summary>Mock network: every request goes to a delegate (no real internet).</summary>
internal sealed class MockHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _fn;
    public List<string> Requests { get; } = new();
    public MockHandler(Func<HttpRequestMessage, HttpResponseMessage> fn) : this((r, _) => Task.FromResult(fn(r))) { }
    public MockHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> fn) => _fn = fn;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        lock (Requests) Requests.Add(request.RequestUri!.ToString());
        return _fn(request, cancellationToken);
    }

    public static HttpResponseMessage Json(string json, HttpStatusCode code = HttpStatusCode.OK)
        => new(code) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    public static HttpResponseMessage Bytes(byte[] data)
        => new(HttpStatusCode.OK) { Content = new ByteArrayContent(data) };
    public static HttpResponseMessage Status(HttpStatusCode code) => new(code) { Content = new StringContent("") };
}

/// <summary>Tiny real HTTP/1.1 server on 127.0.0.1 (TcpListener, no admin / URL ACL needed).</summary>
internal sealed class LocalHttpServer : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly Dictionary<string, (int Code, byte[] Body)> _routes = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _cts = new();
    public string BaseUrl { get; }
    public int Hits;

    public LocalHttpServer()
    {
        _listener.Start();
        BaseUrl = $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/";
        _ = Task.Run(LoopAsync);
    }

    public void Map(string path, byte[] body, int code = 200) { lock (_routes) _routes["/" + path.TrimStart('/')] = (code, body); }
    public void Map(string path, string body, int code = 200) => Map(path, Encoding.UTF8.GetBytes(body), code);

    private async Task LoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await _listener.AcceptTcpClientAsync(_cts.Token); } catch { return; }
            _ = Task.Run(async () =>
            {
                using (client)
                {
                    var stream = client.GetStream();
                    var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true);
                    var line = await reader.ReadLineAsync() ?? "";
                    string? h;
                    while (!string.IsNullOrEmpty(h = await reader.ReadLineAsync())) { }
                    Interlocked.Increment(ref Hits);
                    var path = line.Split(' ').ElementAtOrDefault(1) ?? "/";
                    int q = path.IndexOf('?'); if (q >= 0) path = path[..q];
                    (int Code, byte[] Body) r;
                    lock (_routes) r = _routes.TryGetValue(path, out var x) ? x : (404, Encoding.UTF8.GetBytes("not found"));
                    var head = Encoding.ASCII.GetBytes($"HTTP/1.1 {r.Code} X\r\nContent-Length: {r.Body.Length}\r\nContent-Type: application/octet-stream\r\nConnection: close\r\n\r\n");
                    await stream.WriteAsync(head);
                    await stream.WriteAsync(r.Body);
                    await stream.FlushAsync();
                }
            });
        }
    }

    public void Dispose() { _cts.Cancel(); _listener.Stop(); }
}
