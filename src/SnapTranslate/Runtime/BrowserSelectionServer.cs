using System;
using System.Drawing;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using SnapTranslate.Services;

namespace SnapTranslate.Runtime;

public sealed class BrowserSelectionServer : IDisposable
{
    public const int Port = 49387;
    public const string SelectionEndpoint = "selection";

    private const int MaxRequestBytes = 64 * 1024;
    private readonly HttpListener _listener = new();
    private CancellationTokenSource? _serverCts;
    private Task? _serverTask;

    public BrowserSelectionServer()
    {
        _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
    }

    public event EventHandler<BrowserSelectionReceivedEventArgs>? SelectionReceived;

    public void Start()
    {
        if (_serverTask is not null)
        {
            return;
        }

        _serverCts = new CancellationTokenSource();
        _listener.Start();
        _serverTask = Task.Run(() => RunAsync(_serverCts.Token));
    }

    public void Dispose()
    {
        _serverCts?.Cancel();
        if (_listener.IsListening)
        {
            _listener.Stop();
        }

        _listener.Close();
        _serverCts?.Dispose();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _listener.IsListening)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync();
            }
            catch when (cancellationToken.IsCancellationRequested || !_listener.IsListening)
            {
                break;
            }
            catch
            {
                continue;
            }

            _ = Task.Run(() => HandleRequestAsync(context, cancellationToken), cancellationToken);
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        AddCorsHeaders(context.Response);

        try
        {
            if (context.Request.HttpMethod.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = (int)HttpStatusCode.NoContent;
                return;
            }

            string requestPath = context.Request.Url?.AbsolutePath.Trim('/') ?? string.Empty;
            if (!context.Request.HttpMethod.Equals("POST", StringComparison.OrdinalIgnoreCase) ||
                !requestPath.Equals(SelectionEndpoint, StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = (int)HttpStatusCode.NotFound;
                return;
            }

            if (context.Request.ContentLength64 > MaxRequestBytes)
            {
                context.Response.StatusCode = (int)HttpStatusCode.RequestEntityTooLarge;
                return;
            }

            using MemoryStream bodyStream = new();
            await context.Request.InputStream.CopyToAsync(bodyStream, cancellationToken);
            if (bodyStream.Length > MaxRequestBytes)
            {
                context.Response.StatusCode = (int)HttpStatusCode.RequestEntityTooLarge;
                return;
            }

            BrowserSelectionPayload? payload = JsonSerializer.Deserialize<BrowserSelectionPayload>(bodyStream.ToArray());
            string text = TextSanitizer.NormalizeForTranslation(payload?.Text ?? string.Empty);
            if (!TextSanitizer.IsUsefulForTranslation(text))
            {
                context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                return;
            }

            Point screenPoint = new(payload?.ScreenX ?? 0, payload?.ScreenY ?? 0);
            SelectionReceived?.Invoke(this, new BrowserSelectionReceivedEventArgs(text, screenPoint));

            context.Response.StatusCode = (int)HttpStatusCode.OK;
            await WriteJsonAsync(context.Response, "{\"ok\":true}", cancellationToken);
        }
        catch
        {
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
        }
        finally
        {
            try
            {
                context.Response.Close();
            }
            catch
            {
            }
        }
    }

    private static void AddCorsHeaders(HttpListenerResponse response)
    {
        response.Headers["Access-Control-Allow-Origin"] = "*";
        response.Headers["Access-Control-Allow-Methods"] = "POST, OPTIONS";
        response.Headers["Access-Control-Allow-Headers"] = "Content-Type";
        response.Headers["Access-Control-Max-Age"] = "86400";
    }

    private static async Task WriteJsonAsync(HttpListenerResponse response, string json, CancellationToken cancellationToken)
    {
        response.ContentType = "application/json; charset=utf-8";
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes, cancellationToken);
    }

    private sealed record BrowserSelectionPayload(
        [property: JsonPropertyName("text")] string? Text,
        [property: JsonPropertyName("screenX")] int ScreenX,
        [property: JsonPropertyName("screenY")] int ScreenY);
}
