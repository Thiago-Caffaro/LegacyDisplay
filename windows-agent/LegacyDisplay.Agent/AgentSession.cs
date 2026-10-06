using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using LegacyDisplay.Protocol;
using LegacyDisplay.Windows;
using LegacyDisplay.Windows.Actions;

namespace LegacyDisplay.Agent;

public sealed class AgentSession
{
    public async Task RunAsync(DeviceCredential credential, bool demo, int intervalMs, CancellationToken cancellationToken, string? actionsPath = null)
    {
        using var metrics = new WindowsMetrics();
        using var actions = new ActionExecutor(() => ActionStore.Load(actionsPath ?? ActionStore.DefaultPath));
        var mining = new MiningRoutineStatus(() => ActionStore.Load(actionsPath ?? ActionStore.DefaultPath));
        var device = new Uri(credential.Device);
        var delay = 1;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var client = new DeviceClient(device, credential.Token);
                var layout = await client.GetLayoutAsync(cancellationToken);
                using var socket = new ClientWebSocket();
                socket.Options.SetRequestHeader("Authorization", "Bearer " + credential.Token);
                socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(5);
                var endpoint = new UriBuilder(device) { Scheme = device.Scheme == "https" ? "wss" : "ws", Path = "/api/v1/live" }.Uri;
                using (var connect = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    connect.CancelAfter(TimeSpan.FromSeconds(10));
                    await socket.ConnectAsync(endpoint, connect.Token);
                }
                Console.WriteLine($"Connected to {device.Host}:{device.Port}. Sending metrics every {intervalMs} ms.");
                var connectedAt = Environment.TickCount64;
                using var session = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                using var sendLock = new SemaphoreSlim(1);
                var sender = SendMetricsAsync(metrics, mining, socket, sendLock, demo, intervalMs, session.Token);
                var receiver = ReceiveAsync(actions, socket, sendLock, layout, session.Token);
                try { await await Task.WhenAny(sender, receiver); }
                finally
                {
                    session.Cancel(); socket.Abort();
                    try { await Task.WhenAll(sender, receiver); }
                    catch (Exception) when (!cancellationToken.IsCancellationRequested) { }
                    if (Environment.TickCount64 - connectedAt >= 30_000) delay = 1;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                throw new InvalidOperationException("Tablet rejected the credential. Pair again using the code shown on the tablet.", ex);
            }
            catch (Exception ex) when (ex is HttpRequestException or WebSocketException or IOException or OperationCanceledException or JsonException)
            {
                Console.WriteLine($"Connection lost ({ex.GetType().Name}). Retrying in {delay}s.");
            }
            await Task.Delay(TimeSpan.FromSeconds(delay), cancellationToken);
            delay = Math.Min(delay * 2, 30);
        }
    }

    private static async Task SendMetricsAsync(WindowsMetrics metrics, MiningRoutineStatus mining, ClientWebSocket socket, SemaphoreSlim sendLock, bool demo, int intervalMs, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var values = metrics.Sample(demo);
            foreach (var value in mining.Sample()) values[value.Key] = value.Value;
            await SendAsync(socket, sendLock, new { type = "data.update", values }, cancellationToken);
            await Task.Delay(intervalMs, cancellationToken);
        }
    }

    private static async Task ReceiveAsync(ActionExecutor actions, ClientWebSocket socket, SemaphoreSlim sendLock, LayoutDocument layout, CancellationToken cancellationToken)
    {
        using var actionLifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task? activeAction = null;
        var seen = new HashSet<Guid>(); var order = new Queue<Guid>();
        var buffer = new byte[4096];
        try
        {
        while (!cancellationToken.IsCancellationRequested)
        {
            using var frame = new MemoryStream();
            WebSocketReceiveResult result;
            do
            {
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                deadline.CancelAfter(TimeSpan.FromSeconds(20));
                result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), deadline.Token);
                if (result.MessageType == WebSocketMessageType.Close) throw new IOException("Tablet closed connection.");
                // Tablet hello/layout.changed wraps a layout of up to 64 KiB.
                if (result.MessageType != WebSocketMessageType.Text || frame.Length + result.Count > 131_072)
                    throw new JsonException("Invalid live frame.");
                frame.Write(buffer, 0, result.Count);
            } while (!result.EndOfMessage);
            using var document = JsonDocument.Parse(frame.ToArray());
            var message = document.RootElement;
            switch (message.GetProperty("type").GetString())
            {
                case "hello":
                    if (message.GetProperty("protocolVersion").GetInt32() != 1) throw new JsonException("Unsupported protocol version.");
                    layout = LayoutDocument.Parse(message.GetProperty("layout").GetRawText());
                    break;
                case "heartbeat":
                    break;
                case "layout.changed":
                    layout = LayoutDocument.Parse(message.GetProperty("layout").GetRawText());
                    break;
                case "action.invoke":
                    var id = message.GetProperty("requestId").GetString();
                    var widgetId = message.GetProperty("widgetId").GetString();
                    var action = message.GetProperty("action").GetString();
                    if (!Guid.TryParse(id, out var requestId) || !seen.Add(requestId))
                    {
                        await SendAsync(socket, sendLock, new { type = "action.result", requestId = id, success = false, message = "Solicitação de ação inválida ou repetida" }, cancellationToken);
                        break;
                    }
                    order.Enqueue(requestId); if (order.Count > 128) seen.Remove(order.Dequeue());
                    if (activeAction is { IsCompleted: false })
                        await SendAsync(socket, sendLock, new { type = "action.result", requestId = id, success = false, message = "Outra ação está em andamento. Aguarde e tente novamente." }, cancellationToken);
                    else
                    {
                        if (activeAction != null) await activeAction;
                        activeAction = HandleActionAsync(actions, socket, sendLock, layout, id, widgetId, action, actionLifetime.Token);
                    }
                    break;
                case "error":
                    throw new JsonException("Tablet rejected a live message.");
                default:
                    throw new JsonException("Unsupported live message.");
            }
        }
        }
        finally
        {
            actionLifetime.Cancel();
            if (activeAction != null)
            {
                try { await activeAction; }
                catch (OperationCanceledException) when (actionLifetime.IsCancellationRequested) { }
            }
        }
    }

    private static async Task HandleActionAsync(ActionExecutor actions, ClientWebSocket socket, SemaphoreSlim sendLock, LayoutDocument layout,
        string? id, string? widgetId, string? action, CancellationToken cancellationToken)
    {
        // The tablet provides identifiers only; all executable parameters come from the local protected catalog.
        var result = await actions.InvokeAsync(id, widgetId, action, layout, cancellationToken);
        Console.WriteLine(result.Success ? "Tablet action completed." : "Tablet action rejected or failed.");
        await SendAsync(socket, sendLock, new { type = "action.result", requestId = id, success = result.Success, message = result.Message }, cancellationToken);
    }

    private static async Task SendAsync(ClientWebSocket socket, SemaphoreSlim sendLock, object message, CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(message);
        await sendLock.WaitAsync(cancellationToken);
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(10));
            await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, deadline.Token);
        }
        finally { sendLock.Release(); }
    }
}
